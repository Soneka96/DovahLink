using System.Buffers.Binary;
using System.Security.Cryptography;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Process;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The per-connection private IPC protocol decisions for one adapter connection attempt: handshake
/// acceptance, resynchronization request/response correlation, host-directed capture-intent
/// preparation, and disconnect notification. Holds no transport state of its own; a new instance is
/// created for each accepted connection and consumed by that connection's <see cref="IAdapterIpcConnection"/>.
/// </summary>
public interface IAdapterIpcSession
{
    /// <summary>The connection generation assigned once the accepted handshake is committed, or <see langword="null"/> before then.</summary>
    long? ConnectionGeneration { get; }

    /// <summary>Evaluates a connecting adapter's Hello and decides whether to accept the connection without publishing availability.</summary>
    /// <param name="hello">The received Hello message.</param>
    AdapterHandshakeResult Handshake(IpcHelloMessage hello);

    /// <summary>
    /// Commits an accepted handshake after the acknowledgement has been queued ahead of all normal
    /// host-side availability work.
    /// </summary>
    void CommitHandshake();

    /// <summary>Builds the resynchronization request to send immediately after a successful handshake.</summary>
    IpcResynchronizeRequestMessage PrepareResynchronizeRequest();

    /// <summary>
    /// Withdraws a previously prepared resynchronization request that could not actually be sent (for
    /// example a full outbound queue), so a stray later result carrying the same correlation id is
    /// never mistaken for a reply to a request the adapter was never asked to answer. A no-op when
    /// <paramref name="correlationId"/> no longer matches the currently pending request -- for
    /// example because a later request has already replaced it.
    /// </summary>
    /// <param name="correlationId">The correlation id of the request to withdraw.</param>
    void CancelPendingResynchronize(ulong correlationId);

    /// <summary>Processes one message received after a successful handshake and decides how to respond.</summary>
    /// <param name="message">The decoded message.</param>
    AdapterIpcOutcome HandleFrame(IpcMessage message);

    /// <summary>
    /// Processes a frame the codec could not safely decode. Every decode failure closes the
    /// connection without echoing the specific reason back over the wire.
    /// </summary>
    AdapterIpcOutcome HandleDecodeFailure();

    /// <summary>
    /// Prepares a host-directed event-listening intent, or <see langword="null"/> before a
    /// successful handshake or when <paramref name="eventKey"/> is zero.
    /// </summary>
    /// <param name="eventKey">The host-owned event key.</param>
    IpcListenEventMessage? PrepareListenEvent(uint eventKey);

    /// <summary>
    /// Prepares a host-directed sample-read intent, or <see langword="null"/> before a successful
    /// handshake or when <paramref name="sampleToken"/> is zero.
    /// </summary>
    /// <param name="sampleToken">The host-owned sample token.</param>
    IpcReadSampleMessage? PrepareReadSample(uint sampleToken);

    /// <summary>Prepares a cancellation for a previously issued correlation id, or <see langword="null"/> before a successful handshake.</summary>
    /// <param name="correlationId">The nonzero correlation id of the request to cancel.</param>
    IpcCancelMessage? PrepareCancel(ulong correlationId);

    /// <summary>Records that the physical connection ended, deactivating this session's connection lease exactly once.</summary>
    void HandleDisconnected();

    /// <summary>
    /// Prepares a host-directed pairing-display request, or <see langword="null"/> before a
    /// successful handshake or when the current connection generation is no longer active.
    /// </summary>
    /// <param name="code">The code to display.</param>
    /// <param name="mode">Which display intent this request carries.</param>
    IpcPairingDisplayMessage? PreparePairingDisplay(string code, PairingDisplayMode mode);

    /// <summary>
    /// Prepares a host-directed no-code attempts-exhausted notification, or <see langword="null"/>
    /// before a successful handshake or when the current connection generation is no longer active.
    /// </summary>
    IpcPairingAttemptsExhaustedMessage? PreparePairingAttemptsExhausted();

    /// <summary>
    /// Validates a received pairing-display acknowledgement against the currently pending request and
    /// active connection generation, removing it from the pending set when it matches.
    /// </summary>
    /// <param name="ack">The received acknowledgement.</param>
    /// <returns>
    /// The acknowledgement's <see cref="IpcPairingDisplayAckMessage.Accepted"/> value when it matches a
    /// currently pending request on the still-active connection generation; otherwise
    /// <see langword="null"/>, meaning the acknowledgement must be ignored.
    /// </returns>
    bool? HandlePairingDisplayAck(IpcPairingDisplayAckMessage ack);

    /// <summary>
    /// Withdraws a previously prepared pairing-display request that could not actually be sent (for
    /// example a full outbound queue), so a later stray acknowledgement carrying the same correlation
    /// id is never mistaken for a reply to a request the adapter was never asked to display.
    /// </summary>
    /// <param name="correlationId">The correlation id of the request to withdraw.</param>
    void CancelPendingPairingDisplay(ulong correlationId);

    /// <summary>
    /// Handles one adapter-originated trust-administration request by forwarding it to the
    /// injected <see cref="IAdapterTrustAdminRequestHandler"/> and returning its formatted result.
    /// </summary>
    /// <param name="request">The received request.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence writes.</param>
    Task<string> HandleTrustAdminRequestAsync(IpcTrustAdminRequestMessage request, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAdapterIpcSession"/>
public sealed class AdapterIpcSession : IAdapterIpcSession
{
    /// <summary>
    /// The sole gateway for this session's connection-lifecycle mutations: activation,
    /// deactivation, and resynchronization completion. This session never reaches
    /// <see cref="IAdapterAvailabilityTracker"/> directly.
    /// </summary>
    private readonly IAdapterConnectionLifecycle lifecycle;

    /// <summary>The verifier this session checks a connecting adapter's peer-ownership proof against.</summary>
    private readonly IAdapterPeerProofVerifier peerProofVerifier;

    /// <summary>The reusable authority this session forwards adapter-originated trust-administration requests to.</summary>
    private readonly IAdapterTrustAdminRequestHandler trustAdminRequestHandler;

    /// <summary>The Host-lifetime tracker this session notifies of adapter-reported play-context transitions.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The domain-facing sink this session routes decoded capture results into.</summary>
    private readonly ILiveCaptureSink liveCaptureSink;

    /// <summary>The coordinator this session reports its own resynchronize request's wire-level admission result to.</summary>
    private readonly IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator;

    /// <summary>
    /// The owning Skyrim process's lifetime identity this host process was launched with. A Hello
    /// whose own <see cref="IpcHelloMessage.OwnerLifetimeId"/> does not match this value is rejected
    /// before <see cref="IpcHelloAckMessage.HostProof"/> is ever computed for it.
    /// </summary>
    private readonly OwnerLifetimeId expectedOwnerLifetimeId;

    /// <summary>The connecting adapter's instance identity, set once the handshake passes validation.</summary>
    private AdapterInstanceId? instanceId;

    /// <summary>
    /// This session's own connection lease, created once its handshake is committed. Identifies this
    /// exact connection attempt to <see cref="lifecycle"/>, independently of <see cref="instanceId"/>
    /// alone, so a superseded session can never be mistaken for a newer one that shares the same
    /// adapter instance.
    /// </summary>
    private AdapterConnectionLease? lease;

    /// <summary>Whether this session's Hello passed validation and is waiting for commitment.</summary>
    private bool handshakeAccepted;

    /// <summary>Whether the accepted handshake has already been committed by activating a connection lease.</summary>
    private bool handshakeCommitted;

    /// <summary>The correlation id of the resynchronization request currently awaiting a result, if any.</summary>
    private ulong? pendingResynchronizeCorrelationId;

    /// <summary>The most recently issued outbound correlation id.</summary>
    private long nextCorrelationId;

    /// <summary>Guards <see cref="HandleDisconnected"/> so it deactivates the lease at most once.</summary>
    private int disconnected;

    /// <summary>Guards <see cref="pendingPairingDisplayCorrelationIds"/> against concurrent access from the connection's send and receive paths.</summary>
    private readonly object pendingPairingDisplayGate = new();

    /// <summary>The correlation ids of pairing-display requests currently awaiting an acknowledgement.</summary>
    private readonly HashSet<ulong> pendingPairingDisplayCorrelationIds = [];

    /// <summary>Creates a session for one connection attempt.</summary>
    /// <param name="lifecycle">The sole gateway for this session's connection-lifecycle mutations.</param>
    /// <param name="peerProofVerifier">The verifier this session checks a connecting adapter's peer-ownership proof against.</param>
    /// <param name="trustAdminRequestHandler">The reusable authority this session forwards adapter-originated trust-administration requests to.</param>
    /// <param name="playContextTracker">The Host-lifetime tracker this session notifies of adapter-reported play-context transitions.</param>
    /// <param name="liveCaptureSink">The domain-facing sink this session routes decoded capture results into.</param>
    /// <param name="resynchronizationTransactionCoordinator">The coordinator this session reports its own resynchronize request's wire-level admission result to.</param>
    /// <param name="expectedOwnerLifetimeId">
    /// The owning Skyrim process's lifetime identity this host process was launched with, or
    /// <see langword="default"/> when the caller does not care about lifetime scoping (matching
    /// <see cref="IpcHelloMessage"/>'s own all-zero default for its <see cref="IpcHelloMessage.OwnerLifetimeId"/>).
    /// </param>
    public AdapterIpcSession(
        IAdapterConnectionLifecycle lifecycle,
        IAdapterPeerProofVerifier peerProofVerifier,
        IAdapterTrustAdminRequestHandler trustAdminRequestHandler,
        IPlayContextTracker playContextTracker,
        ILiveCaptureSink liveCaptureSink,
        IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator,
        OwnerLifetimeId expectedOwnerLifetimeId = default)
    {
        this.lifecycle = lifecycle;
        this.peerProofVerifier = peerProofVerifier;
        this.trustAdminRequestHandler = trustAdminRequestHandler;
        this.playContextTracker = playContextTracker;
        this.liveCaptureSink = liveCaptureSink;
        this.resynchronizationTransactionCoordinator = resynchronizationTransactionCoordinator;
        this.expectedOwnerLifetimeId = expectedOwnerLifetimeId;
    }

    /// <inheritdoc/>
    public long? ConnectionGeneration => lease?.Generation;

    /// <inheritdoc/>
    public AdapterHandshakeResult Handshake(IpcHelloMessage hello)
    {
        if (hello.AdapterInstanceId.Value == Guid.Empty)
        {
            return new AdapterHandshakeResult(false, new IpcHelloAckMessage(hello.CorrelationId, false, IpcHelloRejectReason.Malformed));
        }

        if (!peerProofVerifier.Matches(hello.PeerProofToken))
        {
            return new AdapterHandshakeResult(false, new IpcHelloAckMessage(hello.CorrelationId, false, IpcHelloRejectReason.InvalidProof));
        }

        if (!hello.OwnerLifetimeId.AsSpan().SequenceEqual(expectedOwnerLifetimeId.ToBytes()))
        {
            return new AdapterHandshakeResult(
                false, new IpcHelloAckMessage(hello.CorrelationId, false, IpcHelloRejectReason.LifetimeMismatch));
        }

        instanceId = hello.AdapterInstanceId;
        handshakeAccepted = true;
        byte[] hostProof = ComputeHostProof(hello);
        return new AdapterHandshakeResult(true, new IpcHelloAckMessage(hello.CorrelationId, true, IpcHelloRejectReason.None, hostProof));
    }

    /// <inheritdoc/>
    public void CommitHandshake()
    {
        if (!handshakeAccepted || handshakeCommitted || instanceId is null)
        {
            return;
        }

        lease = lifecycle.CreateLease();
        lifecycle.Activate(lease, instanceId.Value);
        handshakeCommitted = true;
    }

    /// <inheritdoc/>
    public IpcResynchronizeRequestMessage PrepareResynchronizeRequest()
    {
        ulong correlationId = NextCorrelationId();
        pendingResynchronizeCorrelationId = correlationId;
        return new IpcResynchronizeRequestMessage(correlationId);
    }

    /// <inheritdoc/>
    public void CancelPendingResynchronize(ulong correlationId)
    {
        if (pendingResynchronizeCorrelationId == correlationId)
        {
            pendingResynchronizeCorrelationId = null;
        }
    }

    /// <inheritdoc/>
    public AdapterIpcOutcome HandleFrame(IpcMessage message)
    {
        switch (message)
        {
            case IpcResynchronizeResultMessage resynchronizeResult:
                return HandleResynchronizeResult(resynchronizeResult);

            case IpcCloseMessage:
                return AdapterIpcOutcome.Close;

            case IpcRejectMessage:
                return AdapterIpcOutcome.None;

            case IpcCancelMessage:
                return AdapterIpcOutcome.None;

            case IpcCaptureResultMessage captureResult:
                if (instanceId is not null && lease is not null)
                {
                    liveCaptureSink.ApplyCaptureResult(captureResult, new AdapterCaptureSource(instanceId.Value, lease.Generation));
                }

                return AdapterIpcOutcome.None;

            case IpcListenEventResultMessage:
                //  No production caller of PrepareListenEvent exists yet; the
                //  frame is accepted and discarded so the adapter's real reply
                //  path can already be proved end to end before a scheduler
                //  needs to correlate it against a pending request.
                return AdapterIpcOutcome.None;

            case IpcPlayContextChangedMessage playContextChanged:
                playContextTracker.NotifyTransition(playContextChanged.PlayContextId);
                return AdapterIpcOutcome.None;

            case IpcPlayContextEndedMessage:
                playContextTracker.ClearCurrent();
                return AdapterIpcOutcome.None;

            default:
                return AdapterIpcOutcome.SendAndClose(new IpcRejectMessage(message.CorrelationId, IpcRejectReason.UnknownMessageKind));
        }
    }

    /// <inheritdoc/>
    public AdapterIpcOutcome HandleDecodeFailure() =>
        AdapterIpcOutcome.SendAndClose(new IpcCloseMessage(0, IpcCloseReason.Error));

    /// <inheritdoc/>
    public IpcListenEventMessage? PrepareListenEvent(uint eventKey) =>
        lease is null || eventKey == 0 || !lifecycle.IsActive(lease) ? null : new IpcListenEventMessage(NextCorrelationId(), eventKey);

    /// <inheritdoc/>
    public IpcReadSampleMessage? PrepareReadSample(uint sampleToken) =>
        lease is null || sampleToken == 0 || !lifecycle.IsActive(lease) ? null : new IpcReadSampleMessage(NextCorrelationId(), sampleToken);

    /// <inheritdoc/>
    public IpcCancelMessage? PrepareCancel(ulong correlationId) =>
        lease is null || correlationId == 0 || !lifecycle.IsActive(lease) ? null : new IpcCancelMessage(correlationId);

    /// <inheritdoc/>
    public void HandleDisconnected()
    {
        if (Interlocked.CompareExchange(ref disconnected, 1, 0) != 0)
        {
            return;
        }

        if (lease is not null)
        {
            lifecycle.Deactivate(lease);
        }
    }

    /// <inheritdoc/>
    public IpcPairingDisplayMessage? PreparePairingDisplay(string code, PairingDisplayMode mode)
    {
        if (lease is null || !lifecycle.IsActive(lease))
        {
            return null;
        }

        ulong correlationId = NextCorrelationId();
        lock (pendingPairingDisplayGate)
        {
            pendingPairingDisplayCorrelationIds.Add(correlationId);
        }

        return new IpcPairingDisplayMessage(correlationId, code, mode);
    }

    /// <inheritdoc/>
    public IpcPairingAttemptsExhaustedMessage? PreparePairingAttemptsExhausted() =>
        lease is null || !lifecycle.IsActive(lease) ? null : new IpcPairingAttemptsExhaustedMessage(0);

    /// <inheritdoc/>
    public bool? HandlePairingDisplayAck(IpcPairingDisplayAckMessage ack)
    {
        if (lease is null || !lifecycle.IsActive(lease))
        {
            return null;
        }

        lock (pendingPairingDisplayGate)
        {
            if (!pendingPairingDisplayCorrelationIds.Remove(ack.CorrelationId))
            {
                return null;
            }
        }

        return ack.Accepted;
    }

    /// <inheritdoc/>
    public void CancelPendingPairingDisplay(ulong correlationId)
    {
        lock (pendingPairingDisplayGate)
        {
            pendingPairingDisplayCorrelationIds.Remove(correlationId);
        }
    }

    /// <inheritdoc/>
    public Task<string> HandleTrustAdminRequestAsync(IpcTrustAdminRequestMessage request, CancellationToken cancellationToken = default) =>
        trustAdminRequestHandler.HandleAsync(request, cancellationToken);

    /// <summary>
    /// Computes this handshake's <c>HostProof</c>: <c>HMAC-SHA256(key = this host's own
    /// <see cref="IAdapterPeerProofVerifier.HostProofKey"/>, message = Challenge || CorrelationId ||
    /// AdapterInstanceId || OwnerLifetimeId)</c>, proving to the adapter that this host holds a
    /// secret independent of the bearer token the Hello itself carried -- so observing that Hello
    /// alone can never let an untrusted observer forge this proof. Only called once the Hello's own
    /// proof and lifetime id have already been verified.
    /// </summary>
    /// <param name="hello">The verified Hello to compute the proof for.</param>
    private byte[] ComputeHostProof(IpcHelloMessage hello)
    {
        byte[] message = BuildHostProofMessage(hello.Challenge, hello.CorrelationId, hello.AdapterInstanceId, hello.OwnerLifetimeId);
        using var hmac = new HMACSHA256(peerProofVerifier.HostProofKey);
        return hmac.ComputeHash(message);
    }

    /// <summary>
    /// Builds the fixed message a handshake's <c>HostProof</c> is computed over: <c>challenge ||
    /// correlationId || adapterInstanceId || ownerLifetimeId</c>, in exactly this field order,
    /// matching the wire's little-endian integer convention and the adapter's own construction.
    /// </summary>
    private static byte[] BuildHostProofMessage(byte[] challenge, ulong correlationId, AdapterInstanceId adapterInstanceId, byte[] ownerLifetimeId)
    {
        var message = new byte[Constants.IpcHostProofMessageBytes];
        challenge.CopyTo(message, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(Constants.IpcChallengeBytes, 8), correlationId);
        adapterInstanceId.Value.TryWriteBytes(message.AsSpan(Constants.IpcChallengeBytes + 8, 16), bigEndian: true, out _);
        ownerLifetimeId.CopyTo(message, Constants.IpcChallengeBytes + 8 + 16);
        return message;
    }

    /// <summary>
    /// Validates a resynchronization result against the pending request and current generation, then
    /// reports its wire-level admission to <see cref="resynchronizationTransactionCoordinator"/>. This
    /// alone never completes resynchronization -- the coordinator also requires every required
    /// baseline area to have been separately accepted; see its own documentation. A declined result
    /// must never leave the tracked transaction stuck forever with no retry, so it closes the
    /// connection instead: the adapter's normal reconnect then drives a fresh initial
    /// resynchronization. A result that does not match a genuinely pending, still-current request --
    /// a stray, superseded, or repeated correlation, or one arriving before this connection has a
    /// lease or an established play context -- is silently ignored instead, since it was never a
    /// real answer to a request this session is still tracking.
    /// </summary>
    /// <param name="resynchronizeResult">The received resynchronization result.</param>
    /// <returns><see cref="AdapterIpcOutcome.Close"/> for a genuinely declined result; otherwise <see cref="AdapterIpcOutcome.None"/>.</returns>
    private AdapterIpcOutcome HandleResynchronizeResult(IpcResynchronizeResultMessage resynchronizeResult)
    {
        if (pendingResynchronizeCorrelationId != resynchronizeResult.CorrelationId)
        {
            return AdapterIpcOutcome.None;
        }

        pendingResynchronizeCorrelationId = null;
        if (lease is null || instanceId is null)
        {
            return AdapterIpcOutcome.None;
        }

        PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
        if (contextSnapshot.Current is not PlayContextId currentContext)
        {
            return AdapterIpcOutcome.None;
        }

        resynchronizationTransactionCoordinator.RecordAdapterPlanAccepted(
            resynchronizeResult.Accepted, instanceId.Value, lease.Generation, currentContext, contextSnapshot.TransitionGeneration);

        return resynchronizeResult.Accepted ? AdapterIpcOutcome.None : AdapterIpcOutcome.Close;
    }

    /// <summary>Issues the next monotonic outbound correlation id, starting at 1.</summary>
    private ulong NextCorrelationId() => (ulong)Interlocked.Increment(ref nextCorrelationId);
}
