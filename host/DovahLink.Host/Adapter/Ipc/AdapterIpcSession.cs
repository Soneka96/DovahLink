using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The per-connection private IPC protocol decisions for one adapter connection attempt: handshake
/// acceptance, resynchronization request/response correlation, host-directed capture-intent
/// preparation, and disconnect notification. Holds no transport state of its own; a new instance is
/// created for each accepted connection and consumed by that connection's <see cref="IAdapterIpcConnection"/>.
/// </summary>
public interface IAdapterIpcSession
{
    /// <summary>The connection generation assigned once the handshake succeeds, or <see langword="null"/> before then.</summary>
    long? ConnectionGeneration { get; }

    /// <summary>Evaluates a connecting adapter's Hello and decides whether to accept the connection.</summary>
    /// <param name="hello">The received Hello message.</param>
    AdapterHandshakeResult Handshake(IpcHelloMessage hello);

    /// <summary>Builds the resynchronization request to send immediately after a successful handshake.</summary>
    IpcResynchronizeRequestMessage PrepareResynchronizeRequest();

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

    /// <summary>Records that the physical connection ended, notifying the availability tracker exactly once.</summary>
    void HandleDisconnected();
}

/// <inheritdoc cref="IAdapterIpcSession"/>
public sealed class AdapterIpcSession : IAdapterIpcSession
{
    /// <summary>The availability tracker this session reports connection transitions to.</summary>
    private readonly IAdapterAvailabilityTracker tracker;

    /// <summary>The verifier this session checks a connecting adapter's peer-ownership proof against.</summary>
    private readonly IAdapterPeerProofVerifier peerProofVerifier;

    /// <summary>The connecting adapter's instance identity, set once the handshake succeeds.</summary>
    private AdapterInstanceId? instanceId;

    /// <summary>The connection generation assigned once the handshake succeeds.</summary>
    private long? connectionGeneration;

    /// <summary>The correlation id of the resynchronization request currently awaiting a result, if any.</summary>
    private ulong? pendingResynchronizeCorrelationId;

    /// <summary>The most recently issued outbound correlation id.</summary>
    private long nextCorrelationId;

    /// <summary>Guards <see cref="HandleDisconnected"/> so it notifies the tracker at most once.</summary>
    private int disconnected;

    /// <summary>Creates a session for one connection attempt.</summary>
    /// <param name="tracker">The availability tracker this session reports connection transitions to.</param>
    /// <param name="peerProofVerifier">The verifier this session checks a connecting adapter's peer-ownership proof against.</param>
    public AdapterIpcSession(IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier peerProofVerifier)
    {
        this.tracker = tracker;
        this.peerProofVerifier = peerProofVerifier;
    }

    /// <inheritdoc/>
    public long? ConnectionGeneration => connectionGeneration;

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

        instanceId = hello.AdapterInstanceId;
        connectionGeneration = tracker.NotifyConnected(hello.AdapterInstanceId);
        return new AdapterHandshakeResult(true, new IpcHelloAckMessage(hello.CorrelationId, true, IpcHelloRejectReason.None));
    }

    /// <inheritdoc/>
    public IpcResynchronizeRequestMessage PrepareResynchronizeRequest()
    {
        ulong correlationId = NextCorrelationId();
        pendingResynchronizeCorrelationId = correlationId;
        return new IpcResynchronizeRequestMessage(correlationId);
    }

    /// <inheritdoc/>
    public AdapterIpcOutcome HandleFrame(IpcMessage message)
    {
        switch (message)
        {
            case IpcResynchronizeResultMessage resynchronizeResult:
                HandleResynchronizeResult(resynchronizeResult);
                return AdapterIpcOutcome.None;

            case IpcCloseMessage:
                return AdapterIpcOutcome.Close;

            case IpcRejectMessage:
                return AdapterIpcOutcome.None;

            case IpcCancelMessage:
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
        connectionGeneration is null || eventKey == 0 ? null : new IpcListenEventMessage(NextCorrelationId(), eventKey);

    /// <inheritdoc/>
    public IpcReadSampleMessage? PrepareReadSample(uint sampleToken) =>
        connectionGeneration is null || sampleToken == 0 ? null : new IpcReadSampleMessage(NextCorrelationId(), sampleToken);

    /// <inheritdoc/>
    public IpcCancelMessage? PrepareCancel(ulong correlationId) =>
        connectionGeneration is null || correlationId == 0 ? null : new IpcCancelMessage(correlationId);

    /// <inheritdoc/>
    public void HandleDisconnected()
    {
        if (Interlocked.CompareExchange(ref disconnected, 1, 0) != 0)
        {
            return;
        }

        if (instanceId is not null && connectionGeneration is not null)
        {
            tracker.NotifyDisconnected(instanceId.Value, connectionGeneration.Value);
        }
    }

    /// <summary>Validates and applies a resynchronization result against the pending request and current generation.</summary>
    /// <param name="resynchronizeResult">The received resynchronization result.</param>
    private void HandleResynchronizeResult(IpcResynchronizeResultMessage resynchronizeResult)
    {
        if (pendingResynchronizeCorrelationId != resynchronizeResult.CorrelationId)
        {
            return;
        }

        pendingResynchronizeCorrelationId = null;
        if (resynchronizeResult.Accepted &&
            instanceId is not null &&
            connectionGeneration is not null &&
            tracker.CurrentInstanceId == instanceId &&
            tracker.CurrentConnectionGeneration == connectionGeneration)
        {
            tracker.NotifyResynchronized(instanceId.Value, connectionGeneration.Value);
        }
    }

    /// <summary>Issues the next monotonic outbound correlation id, starting at 1.</summary>
    private ulong NextCorrelationId() => (ulong)Interlocked.Increment(ref nextCorrelationId);
}
