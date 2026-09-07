using System.Threading.Channels;
using DovahLink.Host.Time;

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

    /// <summary>Attempts to enqueue a host-directed pairing-display request.</summary>
    /// <param name="code">The code to display.</param>
    /// <param name="mode">Which display intent this request carries.</param>
    /// <param name="correlationId">The request's correlation id when enqueued; otherwise zero.</param>
    /// <returns><see langword="true"/> when the request was accepted onto the outbound queue.</returns>
    bool TrySendPairingDisplay(string code, PairingDisplayMode mode, out ulong correlationId);

    /// <summary>Attempts to enqueue a host-directed no-code attempts-exhausted notification.</summary>
    /// <returns><see langword="true"/> when the notification was accepted onto the outbound queue.</returns>
    bool TrySendPairingAttemptsExhausted();

    /// <summary>
    /// Waits for the adapter's acknowledgement to a previously enqueued pairing-display request,
    /// bounded by <paramref name="timeout"/>. A timeout, cancellation, disconnection, or an
    /// acknowledgement that does not match a currently pending request on the active connection
    /// generation are all reported as <see langword="false"/>, identically to an explicit rejection.
    /// A timeout or cancellation also withdraws <paramref name="correlationId"/> from the session's
    /// own pending set, the same as <see cref="IAdapterIpcSession.CancelPendingPairingDisplay"/>
    /// would for a queue-full rejection, so an adapter that never acknowledges cannot grow that set
    /// without bound.
    /// </summary>
    /// <param name="correlationId">The correlation id returned by <see cref="TrySendPairingDisplay"/>.</param>
    /// <param name="timeout">The maximum time to wait for the acknowledgement.</param>
    /// <param name="cancellationToken">The token used to stop waiting early.</param>
    Task<bool> AwaitPairingDisplayAckAsync(ulong correlationId, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IAdapterIpcConnection"/>
public sealed class AdapterIpcConnection : IAdapterIpcConnection
{
    /// <summary>The maximum time a graceful terminal response may wait for the outbound writer.</summary>
    private static readonly TimeSpan gracefulWriterDrainTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The underlying transport, owned by this connection for its lifetime.</summary>
    private readonly Stream stream;

    /// <summary>The codec used to encode outbound frames and decode inbound ones.</summary>
    private readonly IIpcFrameCodec codec;

    /// <summary>The protocol decisions this connection defers to.</summary>
    private readonly IAdapterIpcSession session;

    /// <summary>The clock used to enforce the per-connection inbound message rate.</summary>
    private readonly IClock clock;

    /// <summary>The timestamps of inbound messages still inside the rate-limit window.</summary>
    private readonly Queue<DateTimeOffset> inboundMessageTimes = [];

    /// <summary>Whether the connection ended because its inbound message rate was exceeded.</summary>
    private bool inboundRateLimitExceeded;

    /// <summary>Whether the peer ended or faulted the inbound transport.</summary>
    private bool peerDisconnected;

    /// <summary>The bounded outbound frame queue drained by <see cref="WriterLoopAsync"/>.</summary>
    private readonly Channel<byte[]> outbound = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(Constants.MaxIpcQueuedMessages) { SingleReader = true, SingleWriter = false });

    /// <summary>Guards <see cref="pendingPairingDisplayAcks"/> against concurrent access.</summary>
    private readonly object pendingPairingDisplayAcksGate = new();

    /// <summary>The acknowledgement waiters for pairing-display requests currently outstanding.</summary>
    private readonly Dictionary<ulong, TaskCompletionSource<bool>> pendingPairingDisplayAcks = [];

    /// <summary>Guards <see cref="pendingTrustAdminRequests"/> against concurrent access.</summary>
    private readonly object pendingTrustAdminRequestsGate = new();

    /// <summary>
    /// The cancellation source for each trust-admin request currently admitted and not yet finished
    /// dispatching, keyed by its correlation id. An entry's absence means the request was never
    /// admitted, or its own dispatch has already finished handling it -- including writing its
    /// reply, being cancelled, or being abandoned by this connection's own teardown.
    /// </summary>
    private readonly Dictionary<ulong, CancellationTokenSource> pendingTrustAdminRequests = [];

    /// <summary>Creates a connection over an already-accepted transport.</summary>
    /// <param name="stream">The underlying transport, owned by this connection for its lifetime.</param>
    /// <param name="codec">The codec used to encode outbound frames and decode inbound ones.</param>
    /// <param name="session">The protocol decisions this connection defers to.</param>
    /// <param name="clock">The time source used to enforce the inbound message rate.</param>
    public AdapterIpcConnection(Stream stream, IIpcFrameCodec codec, IAdapterIpcSession session, IClock clock)
    {
        this.stream = stream;
        this.codec = codec;
        this.session = session;
        this.clock = clock;
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
                bool resynchronizeQueued = outbound.Writer.TryWrite(codec.Encode(session.PrepareResynchronizeRequest()));
                if (!resynchronizeQueued)
                {
                    return;
                }

                session.CommitHandshake();
                await ReadLoopAsync(ioCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ioCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A transport failure in the writer cancels the reader so both halves leave together.
        }
        finally
        {
            // Completed before HandleDisconnected so the outbound channel is already closed to new
            // writes by the time a subscriber can observe Unavailable(N): Channel<T> guarantees
            // TryWrite fails once TryComplete has run, so no send authorized concurrently with
            // teardown can land in the channel after this generation's unavailability is published.
            outbound.Writer.TryComplete();
            session.HandleDisconnected();
            FailAllPendingPairingDisplayAcks();
            CancelAllPendingTrustAdminRequests();
            bool forceClose = cancellationToken.IsCancellationRequested ||
                ioCancellation.IsCancellationRequested ||
                inboundRateLimitExceeded ||
                peerDisconnected;
            bool streamDisposed = false;
            try
            {
                if (forceClose)
                {
                    ioCancellation.Cancel();
                    await stream.DisposeAsync().ConfigureAwait(false);
                    streamDisposed = true;
                }
                else
                {
                    try
                    {
                        await writerTask.WaitAsync(gracefulWriterDrainTimeout).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        ioCancellation.Cancel();
                        await stream.DisposeAsync().ConfigureAwait(false);
                        streamDisposed = true;
                    }
                }

                await writerTask.ConfigureAwait(false);
            }
            finally
            {
                if (!streamDisposed)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
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

    /// <inheritdoc/>
    public bool TrySendPairingDisplay(string code, PairingDisplayMode mode, out ulong correlationId)
    {
        IpcPairingDisplayMessage? message = session.PreparePairingDisplay(code, mode);
        if (message is null)
        {
            correlationId = 0;
            return false;
        }

        lock (pendingPairingDisplayAcksGate)
        {
            pendingPairingDisplayAcks[message.CorrelationId] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        if (!outbound.Writer.TryWrite(codec.Encode(message)))
        {
            lock (pendingPairingDisplayAcksGate)
            {
                pendingPairingDisplayAcks.Remove(message.CorrelationId);
            }

            session.CancelPendingPairingDisplay(message.CorrelationId);
            correlationId = 0;
            return false;
        }

        correlationId = message.CorrelationId;
        return true;
    }

    /// <inheritdoc/>
    public bool TrySendPairingAttemptsExhausted()
    {
        IpcPairingAttemptsExhaustedMessage? message = session.PreparePairingAttemptsExhausted();
        return message is not null && outbound.Writer.TryWrite(codec.Encode(message));
    }

    /// <inheritdoc/>
    public async Task<bool> AwaitPairingDisplayAckAsync(ulong correlationId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? tcs;
        lock (pendingPairingDisplayAcksGate)
        {
            pendingPairingDisplayAcks.TryGetValue(correlationId, out tcs);
        }

        if (tcs is null)
        {
            return false;
        }

        try
        {
            return await tcs.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            // The adapter may still be holding this request queued for its Skyrim game thread (for
            // example a stalled load), so withdrawing only the host's own local state would let a
            // display the host has already reported unavailable appear later anyway. Best-effort:
            // a failed remote cancellation still leaves this wait's own false result unchanged.
            TryCancelRemotePairingDisplay(correlationId);
            return false;
        }
        finally
        {
            lock (pendingPairingDisplayAcksGate)
            {
                pendingPairingDisplayAcks.Remove(correlationId);
            }

            // Safe to call unconditionally: a successful acknowledgement already removed this
            // correlation id from the session's own pending set via HandlePairingDisplayAck, so this
            // is a harmless no-op on that path and the only cleanup for the timeout/cancellation paths.
            session.CancelPendingPairingDisplay(correlationId);
        }
    }

    /// <summary>
    /// Best-effort enqueues a remote cancellation for a pairing-display request whose acknowledgement
    /// wait ended by timeout or caller cancellation. Never called after an explicit accepted or
    /// rejected acknowledgement, since the adapter has already executed that request by then.
    /// </summary>
    /// <param name="correlationId">The pairing-display request's correlation id.</param>
    private void TryCancelRemotePairingDisplay(ulong correlationId)
    {
        try
        {
            TryCancel(correlationId);
        }
        catch (Exception)
        {
            // Best-effort: a failed send here must not change the timeout/cancellation result already
            // decided by the caller.
        }
    }

    /// <summary>
    /// Reads and evaluates the connecting adapter's first frame within
    /// <see cref="Constants.AdapterIpcHandshakeTimeout"/>, which must be a Hello. A peer that
    /// withholds its first frame past the deadline is treated the same as one that disconnects
    /// before completing the handshake, so it cannot hold the listener's one served-connection slot
    /// indefinitely.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop waiting for the frame.</param>
    /// <returns><see langword="true"/> when the handshake was accepted and the connection should proceed to serve frames.</returns>
    private async Task<bool> HandshakeAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource handshakeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeDeadline.CancelAfter(Constants.AdapterIpcHandshakeTimeout);

        IpcDecodeResult? decodeResult;
        try
        {
            decodeResult = await ReadFrameAsync(handshakeDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (handshakeDeadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }

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
            if (!outbound.Writer.TryWrite(codec.Encode(result.AckMessage)))
            {
                return false;
            }

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

            if (decodeResult.Message is IpcPairingDisplayAckMessage pairingDisplayAck)
            {
                ResolvePairingDisplayAck(pairingDisplayAck);
                continue;
            }

            if (decodeResult.Message is IpcTrustAdminRequestMessage trustAdminRequest)
            {
                AdapterIpcOutcome trustAdminOutcome = DispatchTrustAdminRequest(trustAdminRequest);
                EnqueueOutcome(trustAdminOutcome);
                if (trustAdminOutcome.ShouldClose)
                {
                    return;
                }

                continue;
            }

            if (decodeResult.Message is IpcCancelMessage trustAdminCancel)
            {
                // Best-effort only: falls through to the generic handling below unchanged, since a
                // cancellation targeting a correlation id this connection never admitted as a
                // trust-admin request (for example an unrelated pending intent) is that generic
                // handling's own concern, not this one's.
                TryCancelPendingTrustAdminRequest(trustAdminCancel.CorrelationId);
            }

            AdapterIpcOutcome outcome = session.HandleFrame(decodeResult.Message!);
            EnqueueOutcome(outcome);
            if (outcome.ShouldClose)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Admits one received trust-admin request against the bounded, per-connection set of requests
    /// still dispatching and, once admitted, dispatches it without awaiting: the read loop continues
    /// serving other inbound frames -- including a pairing-display acknowledgement -- while this
    /// request's persistence write is still outstanding, replacing the earlier design where this
    /// connection's own read loop blocked on that write.
    /// </summary>
    /// <param name="request">The received request.</param>
    /// <returns>
    /// <see cref="AdapterIpcOutcome.None"/> once the request is admitted and dispatched;
    /// <see cref="AdapterIpcOutcome.SendAndClose"/> rejecting a duplicate, still-outstanding
    /// correlation id as a protocol violation, the same as an unrecognized message kind; or
    /// <see cref="AdapterIpcOutcome.Send"/> with a controlled result, connection left open, once
    /// <see cref="Constants.MaxPendingTrustAdminRequests"/> is already reached -- the same bound the
    /// adapter's own <c>SendTrustAdminRequest</c> enforces on itself, so a mutually authenticated but
    /// malfunctioning adapter cannot create unbounded host-side work.
    /// </returns>
    private AdapterIpcOutcome DispatchTrustAdminRequest(IpcTrustAdminRequestMessage request)
    {
        var requestCancellation = new CancellationTokenSource();
        lock (pendingTrustAdminRequestsGate)
        {
            if (pendingTrustAdminRequests.ContainsKey(request.CorrelationId))
            {
                requestCancellation.Dispose();
                return AdapterIpcOutcome.SendAndClose(
                    new IpcRejectMessage(request.CorrelationId, IpcRejectReason.DuplicateTrustAdminCorrelationId));
            }

            if (pendingTrustAdminRequests.Count >= Constants.MaxPendingTrustAdminRequests)
            {
                requestCancellation.Dispose();
                return AdapterIpcOutcome.Send(new IpcTrustAdminResultMessage(
                    request.CorrelationId, "Too many trust-administration requests in progress. Try again shortly."));
            }

            pendingTrustAdminRequests[request.CorrelationId] = requestCancellation;
        }

        _ = RunTrustAdminRequestAsync(request, requestCancellation);
        return AdapterIpcOutcome.None;
    }

    /// <summary>
    /// Awaits one admitted trust-admin request's dispatch and enqueues its formatted
    /// <see cref="IpcTrustAdminResultMessage"/> reply, then removes it from
    /// <see cref="pendingTrustAdminRequests"/> and disposes its cancellation source exactly once,
    /// regardless of outcome. A cancelled request (this connection tearing down, or an inbound
    /// <see cref="IpcCancelMessage"/> targeting its exact correlation id) is dropped silently,
    /// matching the private IPC contract's own "cancelling a request... is a harmless no-op"
    /// framing: the adapter that requested the cancellation has already stopped waiting for a
    /// reply.
    /// </summary>
    /// <param name="request">The admitted request.</param>
    /// <param name="requestCancellation">This request's own cancellation source.</param>
    private async Task RunTrustAdminRequestAsync(IpcTrustAdminRequestMessage request, CancellationTokenSource requestCancellation)
    {
        try
        {
            string resultText;
            try
            {
                resultText = await session.HandleTrustAdminRequestAsync(request, requestCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            outbound.Writer.TryWrite(codec.Encode(new IpcTrustAdminResultMessage(request.CorrelationId, resultText)));
        }
        finally
        {
            lock (pendingTrustAdminRequestsGate)
            {
                pendingTrustAdminRequests.Remove(request.CorrelationId);
            }

            requestCancellation.Dispose();
        }
    }

    /// <summary>
    /// Best-effort cancels the trust-admin request currently admitted under <paramref name="correlationId"/>,
    /// if any. A harmless no-op when no such request is currently admitted, including when it never
    /// was, or its own dispatch already finished.
    /// </summary>
    /// <param name="correlationId">The correlation id an inbound <see cref="IpcCancelMessage"/> named.</param>
    private void TryCancelPendingTrustAdminRequest(ulong correlationId)
    {
        CancellationTokenSource? requestCancellation;
        lock (pendingTrustAdminRequestsGate)
        {
            pendingTrustAdminRequests.TryGetValue(correlationId, out requestCancellation);
        }

        try
        {
            requestCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Best-effort: this request's own dispatch already finished and disposed its
            // cancellation source between the lookup above and this call; nothing left to cancel.
        }
    }

    /// <summary>
    /// Cancels every trust-admin request still admitted, so none of them keep this connection's
    /// dispatch work outstanding past its own teardown. Does not itself remove or dispose their
    /// entries: each request's own <see cref="RunTrustAdminRequestAsync"/> continuation still does
    /// that exactly once, whether it observes this cancellation or finishes some other way first.
    /// </summary>
    private void CancelAllPendingTrustAdminRequests()
    {
        List<CancellationTokenSource> requestCancellations;
        lock (pendingTrustAdminRequestsGate)
        {
            requestCancellations = [.. pendingTrustAdminRequests.Values];
        }

        foreach (CancellationTokenSource requestCancellation in requestCancellations)
        {
            try
            {
                requestCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Best-effort: see TryCancelPendingTrustAdminRequest's identical race for why.
            }
        }
    }

    /// <summary>Resolves the pending acknowledgement wait matching a received pairing-display acknowledgement, if any.</summary>
    /// <param name="ack">The received acknowledgement.</param>
    private void ResolvePairingDisplayAck(IpcPairingDisplayAckMessage ack)
    {
        bool? accepted = session.HandlePairingDisplayAck(ack);
        if (accepted is null)
        {
            return;
        }

        TaskCompletionSource<bool>? tcs;
        lock (pendingPairingDisplayAcksGate)
        {
            pendingPairingDisplayAcks.Remove(ack.CorrelationId, out tcs);
        }

        tcs?.TrySetResult(accepted.Value);
    }

    /// <summary>
    /// Resolves every still-outstanding pairing-display acknowledgement wait as not accepted, so a
    /// caller awaiting one never hangs past this connection's teardown.
    /// </summary>
    private void FailAllPendingPairingDisplayAcks()
    {
        List<TaskCompletionSource<bool>> waiters;
        lock (pendingPairingDisplayAcksGate)
        {
            waiters = [.. pendingPairingDisplayAcks.Values];
            pendingPairingDisplayAcks.Clear();
        }

        foreach (TaskCompletionSource<bool> tcs in waiters)
        {
            tcs.TrySetResult(false);
        }
    }

    /// <summary>Reads one frame's length prefix and payload and decodes it.</summary>
    /// <param name="cancellationToken">The token used to stop waiting for the frame.</param>
    /// <returns>The decode result, or <see langword="null"/> when the peer disconnected or exceeded the inbound rate limit.</returns>
    private async Task<IpcDecodeResult?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        byte[] lengthPrefix = new byte[sizeof(uint)];
        if (!await ReadExactAsync(lengthPrefix, cancellationToken).ConfigureAwait(false))
        {
            peerDisconnected = true;
            return null;
        }

        if (!codec.TryReadFrameLength(lengthPrefix, out int frameLength))
        {
            return IpcDecodeResult.Failure(IpcRejectReason.MalformedFrameLength);
        }

        byte[] frame = new byte[frameLength];
        if (!await ReadExactAsync(frame, cancellationToken).ConfigureAwait(false))
        {
            peerDisconnected = true;
            return null;
        }

        if (!TryAcceptInboundMessage())
        {
            inboundRateLimitExceeded = true;
            return null;
        }

        return codec.Decode(frame);
    }

    /// <summary>Records one inbound message if the connection remains within its bounded rate window.</summary>
    /// <returns><see langword="true"/> when the message may be decoded; otherwise the connection must close.</returns>
    private bool TryAcceptInboundMessage()
    {
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset windowStart = now - Constants.IpcMessageRateWindow;
        while (inboundMessageTimes.Count > 0 && inboundMessageTimes.Peek() < windowStart)
        {
            inboundMessageTimes.Dequeue();
        }

        if (inboundMessageTimes.Count >= Constants.MaxIpcMessagesPerSecond)
        {
            return false;
        }

        inboundMessageTimes.Enqueue(now);
        return true;
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
                catch (Exception)
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
