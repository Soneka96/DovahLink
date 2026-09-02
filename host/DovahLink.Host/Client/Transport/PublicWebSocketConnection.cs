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
    /// succeeded, failed, or timed out. Every implementation must complete within a bounded time
    /// under every one of these conditions -- <see cref="PublicWebSocketListener"/> awaits this call
    /// after its own accept loops stop, so an implementation that can hang here would hold the
    /// listener's single connection admission slot open indefinitely. This bound is only as strong
    /// as the injected <see cref="IPublicWebSocketMessageHandler"/>'s own contract: it can hold
    /// hostage a handler call already in progress that ignores its cancellation, but it cannot
    /// interrupt a handler that blocks the calling thread synchronously before ever returning
    /// control -- see <see cref="IPublicWebSocketMessageHandler.HandleMessageAsync"/>'s documented
    /// requirement not to do that.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop the connection.</param>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to enqueue an outbound message for the writer loop to send as a WebSocket text frame.
    /// A message that cannot be admitted also requests this connection's own forced close (see
    /// <see cref="RequestClose"/> for the distinct orderly close a caller can request instead), per
    /// the transport's contract that an unadmittable response must not be dropped silently while the
    /// connection stays open; the caller does not need to close the connection itself after a
    /// <see langword="false"/> result.
    /// </summary>
    /// <param name="payload">The complete message payload to send.</param>
    /// <returns>
    /// <see langword="true"/> when the message was accepted onto the bounded outbound queue;
    /// otherwise <see langword="false"/>, and the connection is now closing.
    /// </returns>
    bool TrySend(ReadOnlyMemory<byte> payload);

    /// <summary>
    /// Requests this connection's own orderly close: the read loop stops serving further inbound
    /// messages, but unlike the forced close <see cref="TrySend"/> requests for an unadmittable
    /// message, any frame already admitted onto the bounded outbound queue keeps its normal bounded
    /// opportunity to drain before teardown. Safe to call from any thread and at any point in the
    /// connection's lifetime, including before <see cref="RunAsync"/> has started; idempotent with
    /// every other reason this connection can end.
    /// </summary>
    void RequestClose();
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

    /// <summary>The Host-local abnormal-termination reporting sink this connection reports its root-cause end reason through, at most once.</summary>
    private readonly IPublicWebSocketTransportDiagnostics diagnostics;

    /// <summary>The bounded outbound frame queue drained by the writer loop.</summary>
    private readonly Channel<byte[]> outbound;

    /// <summary>
    /// The total encoded byte size of frames this connection currently owns for outbound delivery --
    /// admitted by <see cref="TrySend"/> and not yet released by the writer loop, which covers a frame
    /// still waiting in <see cref="outbound"/> as well as one the writer has already dequeued and is
    /// still sending. Not merely the bytes presently sitting in the channel.
    /// </summary>
    private long outboundQueuedBytes;

    /// <summary>The timestamps of inbound messages still inside the rate-limit window.</summary>
    private readonly Queue<DateTimeOffset> inboundMessageTimes = [];

    /// <summary>
    /// Whether the connection ended for a reason that requires an abort rather than a graceful close:
    /// a missed keep-alive pong, invalid framing, an oversized message, binary input, an exceeded
    /// inbound rate, or an outbound message that <see cref="TrySend"/> could not admit onto the
    /// bounded queue. A transport already known to be broken, or whose peer is not draining its
    /// outbound queue, is not worth a graceful close attempt.
    /// </summary>
    private bool forceCloseRequested;

    /// <summary>
    /// Whether the writer loop ended because a send failed. Set before the writer cancels
    /// <c>writerCancellation</c>, so it is safely observable once the read loop reacts to that
    /// cancellation and returns.
    /// </summary>
    private bool writerFaulted;

    /// <summary>
    /// A cancellation source this connection owns for its entire lifetime, independent of
    /// <see cref="RunAsync"/>'s external <see cref="CancellationToken"/> parameter. <see
    /// cref="RunAsync"/> links its own shared I/O cancellation to this source alongside the external
    /// token, so <see cref="TrySend"/> can request the connection's own teardown from any thread at
    /// any point in the connection's lifetime -- including before <see cref="RunAsync"/> has ever
    /// been called -- without depending on the timing of when <see cref="RunAsync"/> happens to set
    /// up its own state. Calling <see cref="CancellationTokenSource.Cancel()"/> on this source before
    /// <see cref="RunAsync"/> links it is never lost: linking an already-cancelled source still
    /// produces an already-cancelled linked token, so this needs no separate lock or nullable guard.
    /// </summary>
    private readonly CancellationTokenSource selfRequestedClose = new();

    /// <summary>
    /// A cancellation source this connection owns for its entire lifetime, used by
    /// <see cref="RequestClose"/> to request this connection's own orderly close. Unlike
    /// <see cref="selfRequestedClose"/>, cancelling this source is deliberately deferred until the
    /// outbound queue has had a bounded opportunity to drain (see
    /// <see cref="InterruptReadOnceOutboundDrainsAsync"/>) rather than firing the instant
    /// <see cref="RequestClose"/> is called, and it is never linked into the writer loop's own
    /// cancellation -- so an outbound frame already admitted onto the bounded queue is not abandoned
    /// mid-send. The same before-<see cref="RunAsync"/>-links-it guarantee documented on
    /// <see cref="selfRequestedClose"/> applies here too.
    /// </summary>
    private readonly CancellationTokenSource orderlyCloseRequested = new();

    /// <summary>
    /// The single per-connection capability handed to <see cref="messageHandler"/> on every call to
    /// <see cref="IPublicWebSocketMessageHandler.HandleMessageAsync"/>, scoped to this exact
    /// connection instance for its entire lifetime.
    /// </summary>
    private readonly IPublicConnectionContext connectionContext;

    /// <summary>
    /// Whether <see cref="RequestClose"/> has already been called. Checked by
    /// <see cref="RequestForcedCloseForUnadmittedMessage"/> so a send rejected only because the
    /// outbound queue was already completed by an in-progress orderly close is never misclassified
    /// as a queue overflow: forcing in that case would cancel the shared writer cancellation
    /// immediately, destroying the very drain opportunity <see cref="RequestClose"/> exists to give
    /// an already-admitted frame. <see cref="RequestClose"/> and <see cref="TrySend"/> are both
    /// documented as callable from any thread at any point in the connection's lifetime, so every
    /// access goes through <see cref="Volatile"/> rather than a plain read/write: without it, a
    /// concurrent <see cref="TrySend"/> could observe a stale <see langword="false"/> published by a
    /// racing <see cref="RequestClose"/> call and still take the forced-close path this field exists
    /// to rule out.
    /// </summary>
    private bool orderlyCloseInProgress;

    /// <summary>
    /// The writer loop's own task once <see cref="RunAsync"/> has upgraded the connection, or an
    /// already-completed task before that point or when the upgrade never happens. A field rather
    /// than a <see cref="RunAsync"/>-local variable so <see cref="InterruptReadOnceOutboundDrainsAsync"/>
    /// can wait on the writer actually finishing -- including a frame still in flight on a slow
    /// write -- rather than only on the outbound queue emptying, which happens the moment a frame is
    /// dequeued and can complete well before that frame's send over the wire actually does.
    /// </summary>
    private Task writerTask = Task.CompletedTask;

    /// <summary>
    /// The count of outbound frames this connection currently owns, mirroring
    /// <see cref="outboundQueuedBytes"/>: reserved by <see cref="TrySend"/> on admission and released
    /// only once the writer loop has fully relinquished ownership of that frame (successful send,
    /// failed send, or cancellation). A bounded <see cref="Channel{T}"/> alone would under-count this --
    /// its capacity frees the instant a frame is dequeued, before that frame's send over the wire has
    /// actually finished -- so this field, not <see cref="outbound"/>'s own capacity, is what
    /// <see cref="PublicWebSocketTransportOptions.OutboundQueueMaxMessages"/> actually bounds.
    /// </summary>
    private int outboundOutstandingMessages;

    /// <summary>
    /// Guards <see cref="ReportAbnormalEnd"/> so at most one root-cause reason ever reaches
    /// <see cref="diagnostics"/> for this connection, even when concurrent paths (for example a
    /// <see cref="TrySend"/> rejection on one thread cascading into the read or writer loop's own
    /// cancellation reaction) could otherwise each attempt to attribute a different reason to the
    /// same underlying failure.
    /// </summary>
    private int diagnosticReported;

    /// <summary>Creates a connection over an already-accepted transport.</summary>
    /// <param name="stream">The underlying transport, owned by this connection for its lifetime.</param>
    /// <param name="messageHandler">The handler this connection delegates inbound messages and disconnection to.</param>
    /// <param name="clock">The clock used to enforce the inbound message rate.</param>
    /// <param name="options">The bounded configuration this connection enforces.</param>
    /// <param name="diagnostics">The Host-local abnormal-termination reporting sink this connection reports its root-cause end reason through, at most once.</param>
    public PublicWebSocketConnection(
        Stream stream,
        IPublicWebSocketMessageHandler messageHandler,
        IClock clock,
        PublicWebSocketTransportOptions options,
        IPublicWebSocketTransportDiagnostics diagnostics)
    {
        this.stream = stream;
        this.messageHandler = messageHandler;
        this.clock = clock;
        this.options = options;
        this.diagnostics = diagnostics;
        outbound = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(options.OutboundQueueMaxMessages) { SingleReader = true, SingleWriter = false });
        connectionContext = new PublicConnectionContext(this);
    }

    /// <summary>
    /// Reports <paramref name="reason"/> as this connection's authoritative root-cause abnormal-end
    /// reason, but only the first time this is called for this connection instance -- every later
    /// call is a silent no-op, so a race between multiple paths independently deciding the connection
    /// must end abnormally can never produce more than one diagnostic report, and whichever call wins
    /// is treated as the true root cause.
    /// </summary>
    /// <param name="reason">The structured, non-sensitive reason enforcement occurred.</param>
    private void ReportAbnormalEnd(PublicWebSocketConnectionEndReason reason)
    {
        if (Interlocked.CompareExchange(ref diagnosticReported, 1, 0) == 0)
        {
            diagnostics.ReportAbnormalEnd(reason);
        }
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var writerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, selfRequestedClose.Token);
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(writerCancellation.Token, orderlyCloseRequested.Token);
        WebSocket? webSocket = null;
        bool upgraded = false;
        try
        {
            webSocket = await UpgradeAsync(readCancellation.Token).ConfigureAwait(false);
            if (webSocket is not null)
            {
                upgraded = true;
                writerTask = WriterLoopAsync(webSocket, writerCancellation);
                byte[] messageBuffer = new byte[options.MaxMessageBytes];
                byte[] receiveChunk = new byte[ReceiveChunkBytes];
                await ReadLoopAsync(webSocket, messageBuffer, receiveChunk, readCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (readCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A write failure, a forced self-requested close, or an orderly close request unblocked
            // the reader so teardown can proceed; only the caller's own token should ever propagate
            // as a thrown cancellation from this method.
        }
        finally
        {
            outbound.Writer.TryComplete();
            if (upgraded)
            {
                InvalidateConnectionState();
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

                // The abort above (whichever branch reached it) is expected to unblock a writer
                // stuck in a per-write send, since each send now carries its own bounded deadline;
                // this second bounded wait is a belt-and-suspenders check, not the primary bound. If
                // the writer still has not ended, teardown proceeds and disposes the transport out
                // from under it regardless, rather than ever waiting on it unconditionally.
                try
                {
                    await writerTask.WaitAsync(options.GracefulCloseTimeout).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            webSocket?.Dispose();
            await stream.DisposeAsync().ConfigureAwait(false);
            selfRequestedClose.Dispose();
            orderlyCloseRequested.Dispose();
        }
    }

    /// <summary>
    /// Runs the handler's mandatory session invalidation before any best-effort disconnect cleanup or
    /// physical teardown proceeds, so an authenticated session cannot outlive the connection it
    /// belongs to. Tolerant of a throwing handler -- the handler's own contract requires this call to
    /// be fast and non-blocking, but a bug in it must still not prevent this connection's own
    /// teardown from completing.
    /// </summary>
    private void InvalidateConnectionState()
    {
        try
        {
            messageHandler.HandleConnectionEnded();
        }
        catch (Exception)
        {
            // Mandatory invalidation is expected to be a fast, local, non-throwing operation; a
            // failure here must still not prevent this connection's own teardown from completing.
        }
    }

    /// <summary>
    /// Notifies the message handler of disconnection, bounded by
    /// <see cref="PublicWebSocketTransportOptions.DisconnectNotificationTimeout"/> and tolerant of
    /// any handler failure. A handler that throws or never returns must not prevent this
    /// connection's own socket teardown from completing, and must not hold the listener's single
    /// connection admission slot open indefinitely. The handler receives a real token tied to that
    /// same deadline, so a cooperative implementation gets a genuine chance to unwind rather than
    /// being merely abandoned; <see cref="Task.WaitAsync(TimeSpan)"/> below still bounds this
    /// connection's own wait even if the handler ignores the token entirely.
    /// </summary>
    private async Task NotifyDisconnectedAsync()
    {
        using var notifyDeadline = new CancellationTokenSource(options.DisconnectNotificationTimeout);
        try
        {
            await messageHandler.HandleDisconnectedAsync(notifyDeadline.Token)
                .WaitAsync(options.DisconnectNotificationTimeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort notification only: delivery is never a precondition for this connection's
            // own teardown to proceed, so a failed or hung handler cannot leak the socket or wedge
            // the listener's admission slot.
        }
        finally
        {
            // notifyDeadline's own timeout and the WaitAsync bound above are two independent timers
            // sharing one duration, not one shared deadline; either can fire first. If WaitAsync gives
            // up before notifyDeadline's internal timer has actually run, disposing notifyDeadline
            // (via the using statement, once this method returns) silently discards that still-pending
            // timer callback, so the token would never observe cancellation and a hung handler awaiting
            // it would never unwind. Cancelling explicitly here -- idempotent if the timer already
            // fired -- guarantees the handler's token is always cancelled before this method gives up
            // on it, regardless of which timer would otherwise have won the race.
            notifyDeadline.Cancel();
        }
    }

    /// <inheritdoc/>
    public bool TrySend(ReadOnlyMemory<byte> payload)
    {
        int outstandingAfterReserve = Interlocked.Increment(ref outboundOutstandingMessages);
        if (outstandingAfterReserve > options.OutboundQueueMaxMessages)
        {
            Interlocked.Decrement(ref outboundOutstandingMessages);
            RequestForcedCloseForUnadmittedMessage();
            return false;
        }

        long queuedAfterReserve = Interlocked.Add(ref outboundQueuedBytes, payload.Length);
        if (queuedAfterReserve > options.OutboundQueueMaxBytes)
        {
            Interlocked.Add(ref outboundQueuedBytes, -payload.Length);
            Interlocked.Decrement(ref outboundOutstandingMessages);
            RequestForcedCloseForUnadmittedMessage();
            return false;
        }

        byte[] frame = payload.ToArray();
        if (!outbound.Writer.TryWrite(frame))
        {
            Interlocked.Add(ref outboundQueuedBytes, -payload.Length);
            Interlocked.Decrement(ref outboundOutstandingMessages);
            RequestForcedCloseForUnadmittedMessage();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Requests this connection's own forced close after <see cref="TrySend"/> could not admit a
    /// message onto the bounded outbound queue, rather than leaving the connection open with the
    /// message silently dropped. A no-op while <see cref="orderlyCloseInProgress"/> is already set:
    /// once an orderly close has started, the outbound queue is deliberately completed and every
    /// further <see cref="TrySend"/> naturally fails the same way a genuine overflow would, but that
    /// failure must not be treated as a fresh reason to force-cancel the writer out from under a
    /// frame <see cref="RequestClose"/> already admitted and is still giving a chance to drain. Safe
    /// to call from any thread and at any point in the connection's lifetime: a call before
    /// <see cref="RunAsync"/> has started is never lost, since <see cref="RunAsync"/> always links
    /// its shared I/O cancellation to <see cref="selfRequestedClose"/>; a call after
    /// <see cref="RunAsync"/> has already ended and disposed it is a no-op instead.
    /// </summary>
    private void RequestForcedCloseForUnadmittedMessage()
    {
        if (Volatile.Read(ref orderlyCloseInProgress))
        {
            return;
        }

        forceCloseRequested = true;
        ReportAbnormalEnd(PublicWebSocketConnectionEndReason.OutboundCapacityExceeded);
        try
        {
            selfRequestedClose.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <inheritdoc/>
    public void RequestClose()
    {
        Volatile.Write(ref orderlyCloseInProgress, true);
        outbound.Writer.TryComplete();
        _ = InterruptReadOnceOutboundDrainsAsync();
    }

    /// <summary>
    /// Gives the writer loop a bounded opportunity to actually finish sending whatever was already
    /// admitted before <see cref="RequestClose"/> was called -- so an admitted terminal frame is not
    /// abandoned mid-send the instant an orderly close is requested -- before interrupting the read
    /// loop. Waits on <see cref="writerTask"/> itself rather than <c>outbound.Reader.Completion</c>:
    /// the latter completes the moment a frame is dequeued, which can happen well before that
    /// frame's send over the wire actually finishes on a slow or blocked peer, making it too early a
    /// signal here. Interrupting the read before the send truly finishes would not merely skip the
    /// drain: cancelling a pending <see cref="WebSocket.ReceiveAsync(Memory{byte}, CancellationToken)"/>
    /// call leaves the underlying <see cref="WebSocket"/> unable to complete any further operation,
    /// including a send still in flight on the same object, so an early cancellation can destroy an
    /// already-admitted send that had not yet reached the wire. Bounded by
    /// <see cref="PublicWebSocketTransportOptions.GracefulCloseTimeout"/>, the same window this
    /// connection's own teardown already uses elsewhere for a bounded drain; the wait proceeds
    /// regardless of whether it elapses, the writer finishes, or the connection has already ended
    /// through an unrelated path by the time it runs.
    /// </summary>
    private async Task InterruptReadOnceOutboundDrainsAsync()
    {
        try
        {
            await writerTask.WaitAsync(options.GracefulCloseTimeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            orderlyCloseRequested.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
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
                    ReportAbnormalEnd(PublicWebSocketConnectionEndReason.InvalidHandshake);
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
            ReportAbnormalEnd(PublicWebSocketConnectionEndReason.HandshakeTimeout);
            return null;
        }

        if (!PublicWebSocketHandshake.TryParseUpgradeRequest(requestBuffer.AsSpan(0, length), out string acceptKey))
        {
            ReportAbnormalEnd(PublicWebSocketConnectionEndReason.InvalidHandshake);
            return null;
        }

        byte[] response = PublicWebSocketHandshake.BuildSwitchingProtocolsResponse(acceptKey);
        try
        {
            await stream.WriteAsync(response, handshakeDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (handshakeDeadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            ReportAbnormalEnd(PublicWebSocketConnectionEndReason.HandshakeTimeout);
            return null;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return null;
        }

        return WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
        {
            IsServer = true,
            KeepAliveInterval = options.KeepAliveInterval,
            KeepAliveTimeout = options.KeepAlivePongTimeout,
        });
    }

    /// <summary>
    /// Serves inbound WebSocket messages after a successful upgrade until the connection ends.
    /// Accumulates fragments into <paramref name="messageBuffer"/> up to
    /// <see cref="PublicWebSocketTransportOptions.MaxMessageBytes"/>, never allocating past that
    /// bound; rejects binary input as an unsupported message type; and enforces the inbound rate
    /// limit once a message completes.
    /// </summary>
    /// <param name="webSocket">The upgraded connection to read from.</param>
    /// <param name="messageBuffer">
    /// The buffer one inbound message is accumulated into, bounded to
    /// <see cref="PublicWebSocketTransportOptions.MaxMessageBytes"/>. Owned by the caller as a local
    /// scoped to this connection's post-upgrade lifetime rather than a connection-lifetime field, so
    /// a connection that never completes the WebSocket upgrade never allocates it.
    /// </param>
    /// <param name="receiveChunk">
    /// The chunk buffer one <see cref="WebSocket.ReceiveAsync(Memory{byte}, CancellationToken)"/>
    /// call reads into. Owned by the caller for the same post-upgrade-only reason as
    /// <paramref name="messageBuffer"/>.
    /// </param>
    /// <param name="cancellationToken">The token used to stop the loop.</param>
    private async Task ReadLoopAsync(WebSocket webSocket, byte[] messageBuffer, byte[] receiveChunk, CancellationToken cancellationToken)
    {
        int messageLength = 0;

        // Bounds how long one incomplete fragmented message may stay open, independent of the
        // completed-message rate limit below (which never sees a message until it is fully
        // assembled) and of established WebSocket-level liveness (Ping/Pong only proves the socket
        // is alive, not that an in-progress message is making progress). Created at most once per
        // message, on its first non-final fragment, and never recreated or extended by a later
        // fragment of that same message -- anchoring it to the first fragment is what closes the
        // trickle gap; resetting it per fragment would just move the gap instead of closing it.
        // Disposed the moment the message completes (before dispatch) or, for every other exit from
        // this loop, in the finally below -- never left to survive this connection's own lifetime.
        CancellationTokenSource? fragmentAssemblyDeadline = null;
        try
        {
            while (true)
            {
                WebSocketReceiveResult result;

                // A linked wrapper combining the connection's own cancellation with the fragment
                // deadline, recreated each receive only because WebSocket.ReceiveAsync accepts one
                // token; disposing this wrapper never touches fragmentAssemblyDeadline's own timer,
                // so it carries none of the two-independent-timers disposal race NotifyDisconnectedAsync
                // had to be fixed for elsewhere in this file.
                using CancellationTokenSource? receiveLink = fragmentAssemblyDeadline is null
                    ? null
                    : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, fragmentAssemblyDeadline.Token);
                CancellationToken receiveToken = receiveLink?.Token ?? cancellationToken;

                try
                {
                    result = await webSocket.ReceiveAsync(receiveChunk, receiveToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (fragmentAssemblyDeadline is not null && fragmentAssemblyDeadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // The fragment-assembly deadline elapsed before this message reached EndOfMessage.
                    forceCloseRequested = true;
                    ReportAbnormalEnd(PublicWebSocketConnectionEndReason.FragmentAssemblyTimeout);
                    return;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // This also catches an ordinary RequestClose() unblocking this read: orderlyCloseRequested
                    // is linked into the same readCancellation token this catch reacts to, so a caller-requested
                    // orderly close and a genuine missed-pong keep-alive abort both land here. Only the latter
                    // is a reportable abnormal reason -- orderlyCloseInProgress (set synchronously by
                    // RequestClose() before it ever cancels anything) distinguishes the two.
                    forceCloseRequested = true;
                    if (!Volatile.Read(ref orderlyCloseInProgress))
                    {
                        ReportAbnormalEnd(PublicWebSocketConnectionEndReason.KeepAliveTimeout);
                    }

                    return;
                }
                catch (WebSocketException)
                {
                    forceCloseRequested = true;
                    ReportAbnormalEnd(PublicWebSocketConnectionEndReason.InvalidFraming);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    forceCloseRequested = true;
                    ReportAbnormalEnd(PublicWebSocketConnectionEndReason.UnsupportedBinaryMessage);
                    return;
                }

                if (messageLength + result.Count > messageBuffer.Length)
                {
                    forceCloseRequested = true;
                    ReportAbnormalEnd(PublicWebSocketConnectionEndReason.MessageTooLarge);
                    return;
                }

                Array.Copy(receiveChunk, 0, messageBuffer, messageLength, result.Count);
                messageLength += result.Count;

                if (!result.EndOfMessage)
                {
                    fragmentAssemblyDeadline ??= new CancellationTokenSource(options.FragmentAssemblyTimeout);
                    continue;
                }

                // The message completed -- clear the deadline before any further processing so a
                // deadline that was about to fire (or just fired) can never be mistaken for a reason
                // to close a message that has, in fact, already been fully and successfully received.
                fragmentAssemblyDeadline?.Dispose();
                fragmentAssemblyDeadline = null;

                if (!TryAcceptInboundMessage())
                {
                    forceCloseRequested = true;
                    ReportAbnormalEnd(PublicWebSocketConnectionEndReason.InboundRateLimitExceeded);
                    return;
                }

                // Bounded by cancellationToken rather than a bare await: once HandleMessageAsync has
                // returned its Task, a handler whose Task ignores that token and never completes must
                // not be able to block this loop -- and so RunAsync and the listener's admission slot --
                // indefinitely. The abandoned Task keeps running in the background -- unobserved task
                // faults are not process-fatal on this runtime -- but it can no longer hold teardown
                // hostage once this token fires. This bounds only a returned-but-hanging Task; it cannot
                // bound HandleMessageAsync itself blocking the calling thread synchronously before ever
                // returning one, which is why that is a documented contract requirement on the interface
                // instead of something this loop could otherwise guard against.
                Task handleTask = messageHandler.HandleMessageAsync(connectionContext, messageBuffer.AsMemory(0, messageLength), cancellationToken);
                await handleTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                messageLength = 0;
            }
        }
        finally
        {
            fragmentAssemblyDeadline?.Dispose();
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
    /// queue is completed. Tolerates transport faults by cancelling <paramref name="writerCancellation"/>
    /// and ending the loop rather than throwing, so a broken connection cannot leave this task running
    /// or crash the caller awaiting it. Each send carries its own <see
    /// cref="PublicWebSocketTransportOptions.GracefulCloseTimeout"/> deadline -- reused here rather
    /// than adding a second timeout value, since a peer that cannot drain one send within a
    /// close-handshake-sized window is not one a close handshake could complete with either -- so a
    /// peer that stops reading cannot block this loop indefinitely even while the connection is
    /// otherwise healthy and <paramref name="writerCancellation"/> is not itself cancelled.
    /// </summary>
    /// <param name="webSocket">The upgraded connection to write to.</param>
    /// <param name="writerCancellation">
    /// Cancelled by this loop when a write fails, so the reader stops too; deliberately never linked
    /// to an orderly close request (see <see cref="orderlyCloseRequested"/>), so this loop keeps
    /// draining whatever the outbound queue already holds when only an orderly close is in progress.
    /// </param>
    private async Task WriterLoopAsync(WebSocket webSocket, CancellationTokenSource writerCancellation)
    {
        try
        {
            await foreach (byte[] frame in outbound.Reader.ReadAllAsync(writerCancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(writerCancellation.Token);
                    writeDeadline.CancelAfter(options.GracefulCloseTimeout);
                    await webSocket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, writeDeadline.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    writerFaulted = true;
                    ReportAbnormalEnd(PublicWebSocketConnectionEndReason.WriteFailure);
                    writerCancellation.Cancel();
                    return;
                }
                finally
                {
                    Interlocked.Add(ref outboundQueuedBytes, -frame.Length);
                    Interlocked.Decrement(ref outboundOutstandingMessages);
                }
            }
        }
        catch (OperationCanceledException) when (writerCancellation.IsCancellationRequested)
        {
        }
    }
}
