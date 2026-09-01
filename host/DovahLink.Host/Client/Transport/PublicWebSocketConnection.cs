using System.Net.WebSockets;
using System.Threading.Channels;
using DovahLink.Host.Time;

namespace DovahLink.Host.Client.Transport;

/// <summary>
/// The byte-level I/O shell for one accepted public WebSocket connection: performs the HTTP upgrade
/// handshake under a deadline, owns the single reader and single serialized writer over the resulting
/// <see cref="WebSocket"/>, enforces the approved input/outbound bounds, and converges cancellation,
/// peer failure, timeout, and normal close on one deterministic teardown path. Delegates all message
/// interpretation to an injected <see cref="IPublicWebSocketMessageHandler"/>; owns no protocol
/// policy of its own.
/// </summary>
public interface IPublicWebSocketConnection
{
    /// <summary>
    /// Runs the connection to completion: performs the handshake, then serves inbound messages until
    /// the peer closes, the transport fails, the keep-alive liveness check fails, or
    /// <paramref name="cancellationToken"/> is cancelled. Whenever the handshake completed, makes a
    /// bounded best-effort attempt to notify the message handler of disconnection, then always
    /// disposes the underlying transport before returning regardless of whether that notification
    /// succeeded, failed, or timed out.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop the connection.</param>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>Attempts to enqueue an outbound message for the writer loop to send as a WebSocket text frame.</summary>
    /// <param name="payload">The complete message payload to send.</param>
    /// <returns><see langword="true"/> when the message was accepted onto the bounded outbound queue.</returns>
    bool TrySend(ReadOnlyMemory<byte> payload);
}

/// <inheritdoc cref="IPublicWebSocketConnection"/>
public sealed class PublicWebSocketConnection : IPublicWebSocketConnection
{
    /// <summary>The size of one chunk read from the WebSocket while accumulating a fragmented message.</summary>
    private const int ReceiveChunkBytes = 4096;

    /// <summary>The underlying transport, owned by this connection for its lifetime.</summary>
    private readonly Stream stream;

    /// <summary>The handler this connection delegates inbound messages and disconnection to.</summary>
    private readonly IPublicWebSocketMessageHandler messageHandler;

    /// <summary>The clock used to enforce the inbound message rate.</summary>
    private readonly IClock clock;

    /// <summary>The bounded configuration this connection enforces.</summary>
    private readonly PublicWebSocketTransportOptions options;

    /// <summary>The bounded outbound frame queue drained by the writer loop.</summary>
    private readonly Channel<byte[]> outbound;

    /// <summary>The total encoded byte size of frames currently queued in <see cref="outbound"/>.</summary>
    private long outboundQueuedBytes;

    /// <summary>The reused buffer one inbound message is accumulated into, bounded to <see cref="PublicWebSocketTransportOptions.MaxMessageBytes"/>.</summary>
    private readonly byte[] messageBuffer;

    /// <summary>The chunk buffer one <see cref="WebSocket.ReceiveAsync(Memory{byte}, CancellationToken)"/> call reads into.</summary>
    private readonly byte[] receiveChunk = new byte[ReceiveChunkBytes];

    /// <summary>The timestamps of inbound messages still inside the rate-limit window.</summary>
    private readonly Queue<DateTimeOffset> inboundMessageTimes = [];

    /// <summary>
    /// Whether the read loop ended for a reason that requires an abort rather than a graceful close:
    /// a missed keep-alive pong, invalid framing, an oversized message, binary input, or an exceeded
    /// inbound rate. A transport already known to be broken is not worth a graceful close attempt.
    /// </summary>
    private bool forceCloseRequested;

    /// <summary>
    /// Whether the writer loop ended because a send failed. Set before the writer cancels
    /// <c>ioCancellation</c>, so it is safely observable once the read loop reacts to that
    /// cancellation and returns.
    /// </summary>
    private bool writerFaulted;

    /// <summary>Creates a connection over an already-accepted transport.</summary>
    /// <param name="stream">The underlying transport, owned by this connection for its lifetime.</param>
    /// <param name="messageHandler">The handler this connection delegates inbound messages and disconnection to.</param>
    /// <param name="clock">The clock used to enforce the inbound message rate.</param>
    /// <param name="options">The bounded configuration this connection enforces.</param>
    public PublicWebSocketConnection(
        Stream stream, IPublicWebSocketMessageHandler messageHandler, IClock clock, PublicWebSocketTransportOptions options)
    {
        this.stream = stream;
        this.messageHandler = messageHandler;
        this.clock = clock;
        this.options = options;
        messageBuffer = new byte[options.MaxMessageBytes];
        outbound = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(options.OutboundQueueMaxMessages) { SingleReader = true, SingleWriter = false });
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        WebSocket? webSocket = null;
        Task writerTask = Task.CompletedTask;
        bool upgraded = false;
        try
        {
            webSocket = await UpgradeAsync(ioCancellation.Token).ConfigureAwait(false);
            if (webSocket is not null)
            {
                upgraded = true;
                writerTask = WriterLoopAsync(webSocket, ioCancellation);
                await ReadLoopAsync(webSocket, ioCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ioCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A write failure cancelled the reader so both halves leave together.
        }
        finally
        {
            outbound.Writer.TryComplete();
            if (upgraded)
            {
                await NotifyDisconnectedAsync().ConfigureAwait(false);
            }

            bool forceClose = forceCloseRequested || writerFaulted;
            if (webSocket is not null)
            {
                if (forceClose)
                {
                    webSocket.Abort();
                }
                else
                {
                    // Neither a protocol violation nor a write failure occurred, so the transport is
                    // presumed healthy: attempt a real close handshake (replying fast when the peer
                    // already sent its own Close frame, or initiating one for cancellation/normal
                    // shutdown), bounded so a peer that withholds completing its side cannot hang
                    // teardown.
                    try
                    {
                        await writerTask.WaitAsync(options.GracefulCloseTimeout).ConfigureAwait(false);
                        using var closeDeadline = new CancellationTokenSource(options.GracefulCloseTimeout);
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, closeDeadline.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        webSocket.Abort();
                    }
                }
            }

            await writerTask.ConfigureAwait(false);
            webSocket?.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Notifies the message handler of disconnection, bounded by
    /// <see cref="PublicWebSocketTransportOptions.DisconnectNotificationTimeout"/> and tolerant of
    /// any handler failure. A handler that throws or never returns must not prevent this
    /// connection's own socket teardown from completing, and must not hold the listener's single
    /// connection admission slot open indefinitely.
    /// </summary>
    private async Task NotifyDisconnectedAsync()
    {
        try
        {
            await messageHandler.HandleDisconnectedAsync(CancellationToken.None)
                .WaitAsync(options.DisconnectNotificationTimeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort notification only: delivery is never a precondition for this connection's
            // own teardown to proceed, so a failed or hung handler cannot leak the socket or wedge
            // the listener's admission slot.
        }
    }

    /// <inheritdoc/>
    public bool TrySend(ReadOnlyMemory<byte> payload)
    {
        long queuedAfterReserve = Interlocked.Add(ref outboundQueuedBytes, payload.Length);
        if (queuedAfterReserve > options.OutboundQueueMaxBytes)
        {
            Interlocked.Add(ref outboundQueuedBytes, -payload.Length);
            return false;
        }

        byte[] frame = payload.ToArray();
        if (!outbound.Writer.TryWrite(frame))
        {
            Interlocked.Add(ref outboundQueuedBytes, -payload.Length);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the raw HTTP Upgrade request byte-by-byte -- never over-reading past the header
    /// terminator, so no leftover bytes are ever lost to the WebSocket framing that follows -- within
    /// <see cref="PublicWebSocketTransportOptions.HandshakeTimeout"/>, validates it, and writes the
    /// matching <c>101 Switching Protocols</c> response. A peer that withholds or malforms its
    /// handshake past the deadline is treated the same as one that disconnects before completing it,
    /// so it cannot hold the connection open indefinitely.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop waiting for the handshake.</param>
    /// <returns>The upgraded <see cref="WebSocket"/> on success; otherwise <see langword="null"/>.</returns>
    private async Task<WebSocket?> UpgradeAsync(CancellationToken cancellationToken)
    {
        using var handshakeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeDeadline.CancelAfter(options.HandshakeTimeout);

        byte[] requestBuffer = new byte[options.MaxHandshakeRequestBytes];
        int length = 0;
        try
        {
            while (true)
            {
                if (length == requestBuffer.Length)
                {
                    return null;
                }

                int read;
                try
                {
                    read = await stream.ReadAsync(requestBuffer.AsMemory(length, 1), handshakeDeadline.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    return null;
                }

                if (read == 0)
                {
                    return null;
                }

                length++;
                if (length >= 4 &&
                    requestBuffer[length - 4] == (byte)'\r' && requestBuffer[length - 3] == (byte)'\n' &&
                    requestBuffer[length - 2] == (byte)'\r' && requestBuffer[length - 1] == (byte)'\n')
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (handshakeDeadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (!PublicWebSocketHandshake.TryParseUpgradeRequest(requestBuffer.AsSpan(0, length), out string acceptKey))
        {
            return null;
        }

        byte[] response = PublicWebSocketHandshake.BuildSwitchingProtocolsResponse(acceptKey);
        try
        {
            await stream.WriteAsync(response, handshakeDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (handshakeDeadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return null;
        }

        return WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
        {
            IsServer = true,
            KeepAliveInterval = options.IdleTimeout,
            KeepAliveTimeout = options.KeepAlivePongTimeout,
        });
    }

    /// <summary>
    /// Serves inbound WebSocket messages after a successful upgrade until the connection ends.
    /// Accumulates fragments into <see cref="messageBuffer"/> up to
    /// <see cref="PublicWebSocketTransportOptions.MaxMessageBytes"/>, never allocating past that
    /// bound; rejects binary input as an unsupported message type; and enforces the inbound rate
    /// limit once a message completes.
    /// </summary>
    /// <param name="webSocket">The upgraded connection to read from.</param>
    /// <param name="cancellationToken">The token used to stop the loop.</param>
    private async Task ReadLoopAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        int messageLength = 0;
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await webSocket.ReceiveAsync(receiveChunk, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The keep-alive watchdog aborted the connection after a missed pong reply; the
                // passed token was never itself cancelled, so this is a peer-liveness failure.
                forceCloseRequested = true;
                return;
            }
            catch (WebSocketException)
            {
                forceCloseRequested = true;
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                forceCloseRequested = true;
                return;
            }

            if (messageLength + result.Count > messageBuffer.Length)
            {
                forceCloseRequested = true;
                return;
            }

            Array.Copy(receiveChunk, 0, messageBuffer, messageLength, result.Count);
            messageLength += result.Count;

            if (!result.EndOfMessage)
            {
                continue;
            }

            if (!TryAcceptInboundMessage())
            {
                forceCloseRequested = true;
                return;
            }

            await messageHandler.HandleMessageAsync(messageBuffer.AsMemory(0, messageLength), cancellationToken).ConfigureAwait(false);
            messageLength = 0;
        }
    }

    /// <summary>Records one inbound message if the connection remains within its bounded rate window.</summary>
    /// <returns><see langword="true"/> when the message may be delivered; otherwise the connection must close.</returns>
    private bool TryAcceptInboundMessage()
    {
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset windowStart = now - options.InboundMessageRateWindow;
        while (inboundMessageTimes.Count > 0 && inboundMessageTimes.Peek() < windowStart)
        {
            inboundMessageTimes.Dequeue();
        }

        if (inboundMessageTimes.Count >= options.MaxInboundMessagesPerSecond)
        {
            return false;
        }

        inboundMessageTimes.Enqueue(now);
        return true;
    }

    /// <summary>
    /// Drains the outbound queue and sends each frame as a WebSocket text message in order, until the
    /// queue is completed. Tolerates transport faults by cancelling <paramref name="ioCancellation"/>
    /// and ending the loop rather than throwing, so a broken connection cannot leave this task running
    /// or crash the caller awaiting it.
    /// </summary>
    /// <param name="webSocket">The upgraded connection to write to.</param>
    /// <param name="ioCancellation">Cancelled by this loop when a write fails, so the reader stops too.</param>
    private async Task WriterLoopAsync(WebSocket webSocket, CancellationTokenSource ioCancellation)
    {
        try
        {
            await foreach (byte[] frame in outbound.Reader.ReadAllAsync(ioCancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await webSocket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, ioCancellation.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    writerFaulted = true;
                    ioCancellation.Cancel();
                    return;
                }
                finally
                {
                    Interlocked.Add(ref outboundQueuedBytes, -frame.Length);
                }
            }
        }
        catch (OperationCanceledException) when (ioCancellation.IsCancellationRequested)
        {
        }
    }
}
