using System.Threading.Channels;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The byte-level I/O shell for one accepted adapter connection: reads and writes frames over an
/// injected <see cref="Stream"/> using <see cref="IIpcFrameCodec"/>, and delegates every protocol
/// decision to an injected <see cref="IAdapterIpcSession"/>. Owns the connection's outbound
/// backpressure and physical teardown; owns no protocol policy of its own.
/// </summary>
public interface IAdapterIpcConnection
{
    /// <summary>
    /// Runs the connection to completion: performs the handshake, requests a fresh
    /// resynchronization baseline on success, then serves inbound frames until the peer closes, the
    /// transport fails, or <paramref name="cancellationToken"/> is cancelled. Always notifies the
    /// availability tracker of disconnection and disposes the underlying stream before returning.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop the connection.</param>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>Attempts to enqueue a host-directed event-listening intent.</summary>
    /// <param name="eventKey">The host-owned event key.</param>
    /// <param name="correlationId">The intent's correlation id when enqueued; otherwise zero.</param>
    /// <returns><see langword="true"/> when the intent was accepted onto the outbound queue.</returns>
    bool TrySendListenEvent(uint eventKey, out ulong correlationId);

    /// <summary>Attempts to enqueue a host-directed sample-read intent.</summary>
    /// <param name="sampleToken">The host-owned sample token.</param>
    /// <param name="correlationId">The intent's correlation id when enqueued; otherwise zero.</param>
    /// <returns><see langword="true"/> when the intent was accepted onto the outbound queue.</returns>
    bool TrySendReadSample(uint sampleToken, out ulong correlationId);

    /// <summary>Attempts to enqueue a cancellation for a previously issued correlation id.</summary>
    /// <param name="correlationId">The nonzero correlation id of the request to cancel.</param>
    /// <returns><see langword="true"/> when the cancellation was accepted onto the outbound queue.</returns>
    bool TryCancel(ulong correlationId);
}

/// <inheritdoc cref="IAdapterIpcConnection"/>
public sealed class AdapterIpcConnection : IAdapterIpcConnection
{
    /// <summary>The underlying transport, owned by this connection for its lifetime.</summary>
    private readonly Stream stream;

    /// <summary>The codec used to encode outbound frames and decode inbound ones.</summary>
    private readonly IIpcFrameCodec codec;

    /// <summary>The protocol decisions this connection defers to.</summary>
    private readonly IAdapterIpcSession session;

    /// <summary>The bounded outbound frame queue drained by <see cref="WriterLoopAsync"/>.</summary>
    private readonly Channel<byte[]> outbound = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(Constants.MaxIpcQueuedMessages) { SingleReader = true, SingleWriter = false });

    /// <summary>Creates a connection over an already-accepted transport.</summary>
    /// <param name="stream">The underlying transport, owned by this connection for its lifetime.</param>
    /// <param name="codec">The codec used to encode outbound frames and decode inbound ones.</param>
    /// <param name="session">The protocol decisions this connection defers to.</param>
    public AdapterIpcConnection(Stream stream, IIpcFrameCodec codec, IAdapterIpcSession session)
    {
        this.stream = stream;
        this.codec = codec;
        this.session = session;
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task writerTask = WriterLoopAsync(ioCancellation);
        try
        {
            bool handshakeAccepted = await HandshakeAsync(ioCancellation.Token).ConfigureAwait(false);
            if (handshakeAccepted)
            {
                outbound.Writer.TryWrite(codec.Encode(session.PrepareResynchronizeRequest()));
                await ReadLoopAsync(ioCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ioCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A transport failure in the writer cancels the reader so both halves leave together.
        }
        finally
        {
            session.HandleDisconnected();
            outbound.Writer.TryComplete();
            bool forceClose = cancellationToken.IsCancellationRequested || ioCancellation.IsCancellationRequested;
            if (forceClose)
            {
                ioCancellation.Cancel();
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            await writerTask.ConfigureAwait(false);
            if (!forceClose)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public bool TrySendListenEvent(uint eventKey, out ulong correlationId)
    {
        IpcListenEventMessage? message = session.PrepareListenEvent(eventKey);
        if (message is null)
        {
            correlationId = 0;
            return false;
        }

        byte[] frame = codec.Encode(message);
        if (!outbound.Writer.TryWrite(frame))
        {
            correlationId = 0;
            return false;
        }

        correlationId = message.CorrelationId;
        return true;
    }

    /// <inheritdoc/>
    public bool TrySendReadSample(uint sampleToken, out ulong correlationId)
    {
        IpcReadSampleMessage? message = session.PrepareReadSample(sampleToken);
        if (message is null)
        {
            correlationId = 0;
            return false;
        }

        byte[] frame = codec.Encode(message);
        if (!outbound.Writer.TryWrite(frame))
        {
            correlationId = 0;
            return false;
        }

        correlationId = message.CorrelationId;
        return true;
    }

    /// <inheritdoc/>
    public bool TryCancel(ulong correlationId)
    {
        IpcCancelMessage? message = session.PrepareCancel(correlationId);
        return message is not null && outbound.Writer.TryWrite(codec.Encode(message));
    }

    /// <summary>Reads and evaluates the connecting adapter's first frame, which must be a Hello.</summary>
    /// <param name="cancellationToken">The token used to stop waiting for the frame.</param>
    /// <returns><see langword="true"/> when the handshake was accepted and the connection should proceed to serve frames.</returns>
    private async Task<bool> HandshakeAsync(CancellationToken cancellationToken)
    {
        IpcDecodeResult? decodeResult = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        if (decodeResult is null)
        {
            return false;
        }

        if (decodeResult.FailureReason is not null)
        {
            EnqueueOutcome(session.HandleDecodeFailure());
            return false;
        }

        if (decodeResult.Message is IpcHelloMessage hello)
        {
            AdapterHandshakeResult result = session.Handshake(hello);
            outbound.Writer.TryWrite(codec.Encode(result.AckMessage));
            return result.Accepted;
        }

        EnqueueOutcome(session.HandleFrame(decodeResult.Message!));
        return false;
    }

    /// <summary>Serves inbound frames after a successful handshake until the connection ends.</summary>
    /// <param name="cancellationToken">The token used to stop the loop.</param>
    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IpcDecodeResult? decodeResult = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (decodeResult is null)
            {
                return;
            }

            if (decodeResult.FailureReason is not null)
            {
                EnqueueOutcome(session.HandleDecodeFailure());
                return;
            }

            AdapterIpcOutcome outcome = session.HandleFrame(decodeResult.Message!);
            EnqueueOutcome(outcome);
            if (outcome.ShouldClose)
            {
                return;
            }
        }
    }

    /// <summary>Reads one frame's length prefix and payload and decodes it.</summary>
    /// <param name="cancellationToken">The token used to stop waiting for the frame.</param>
    /// <returns>The decode result, or <see langword="null"/> when the peer disconnected before a complete frame arrived.</returns>
    private async Task<IpcDecodeResult?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        byte[] lengthPrefix = new byte[sizeof(uint)];
        if (!await ReadExactAsync(lengthPrefix, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!codec.TryReadFrameLength(lengthPrefix, out int frameLength))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedFrameLength);
        }

        byte[] frame = new byte[frameLength];
        return !await ReadExactAsync(frame, cancellationToken).ConfigureAwait(false)
            ? null
            : codec.Decode(frame);
    }

    /// <summary>Fills a buffer completely from the transport, tolerating any number of partial reads.</summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="cancellationToken">The token used to stop waiting for data.</param>
    /// <returns><see langword="true"/> when the buffer was completely filled; <see langword="false"/> on a clean or faulted disconnect.</returns>
    private async Task<bool> ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer[totalRead..], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                return false;
            }

            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    /// <summary>Encodes and enqueues every message in an outcome for the writer loop to send.</summary>
    /// <param name="outcome">The outcome whose messages to enqueue.</param>
    private void EnqueueOutcome(AdapterIpcOutcome outcome)
    {
        foreach (IpcMessage message in outcome.MessagesToSend)
        {
            outbound.Writer.TryWrite(codec.Encode(message));
        }
    }

    /// <summary>
    /// Drains the outbound queue and writes each frame to the transport in order, until the queue is
    /// completed. Tolerates transport faults by ending the loop rather than throwing, so a broken
    /// peer connection cannot leave this task running or crash the caller awaiting it.
    /// </summary>
    private async Task WriterLoopAsync(CancellationTokenSource ioCancellation)
    {
        try
        {
            await foreach (byte[] frame in outbound.Reader.ReadAllAsync(ioCancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await stream.WriteAsync(frame, ioCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ioCancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    ioCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ioCancellation.IsCancellationRequested)
        {
        }
    }
}
