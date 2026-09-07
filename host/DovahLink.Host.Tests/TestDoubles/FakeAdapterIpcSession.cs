using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IAdapterIpcSession"/> that records calls and returns preset results.</summary>
public sealed class FakeAdapterIpcSession : IAdapterIpcSession
{
    /// <summary>The Hello messages passed to <see cref="Handshake"/>, in call order.</summary>
    public List<IpcHelloMessage> HandshakeCalls { get; } = [];

    /// <summary>The lifecycle operations observed by this fake, in call order.</summary>
    public List<string> LifecycleCalls { get; } = [];

    /// <summary>The frames passed to <see cref="HandleFrame"/>, in call order.</summary>
    public List<IpcMessage> HandledFrames { get; } = [];

    /// <summary>The number of times <see cref="HandleDecodeFailure"/> was called.</summary>
    public int DecodeFailureCalls { get; private set; }

    /// <summary>The number of times <see cref="HandleDisconnected"/> was called.</summary>
    public int DisconnectedCalls { get; private set; }

    /// <summary>The number of times <see cref="CommitHandshake"/> was called.</summary>
    public int CommitHandshakeCalls { get; private set; }

    /// <inheritdoc/>
    public long? ConnectionGeneration { get; set; }

    /// <summary>The result <see cref="Handshake"/> returns.</summary>
    public AdapterHandshakeResult HandshakeResult { get; set; } =
        new(true, new IpcHelloAckMessage(0, true, IpcHelloRejectReason.None));

    /// <summary>The message <see cref="PrepareResynchronizeRequest"/> returns.</summary>
    public IpcResynchronizeRequestMessage ResynchronizeRequest { get; set; } = new(1);

    /// <summary>The outcome <see cref="HandleFrame"/> returns.</summary>
    public AdapterIpcOutcome FrameOutcome { get; set; } = AdapterIpcOutcome.None;

    /// <summary>The outcome <see cref="HandleDecodeFailure"/> returns.</summary>
    public AdapterIpcOutcome DecodeFailureOutcome { get; set; } = AdapterIpcOutcome.Close;

    /// <summary>The message <see cref="PrepareListenEvent"/> returns.</summary>
    public IpcListenEventMessage? ListenEventResult { get; set; }

    /// <summary>The message <see cref="PrepareReadSample"/> returns.</summary>
    public IpcReadSampleMessage? ReadSampleResult { get; set; }

    /// <summary>
    /// Whether <see cref="PrepareCancel"/> returns <see langword="null"/> instead of a message,
    /// simulating the session declining to prepare a cancellation (for example an inactive lease).
    /// When unset, <see cref="PrepareCancel"/> returns <c>new IpcCancelMessage(correlationId)</c> for
    /// whatever correlation id it was called with, matching production
    /// <see cref="AdapterIpcSession.PrepareCancel"/>'s own contract, so a test exercising the
    /// realistic default does not have to hardcode a matching value in advance.
    /// </summary>
    public bool PrepareCancelReturnsNull { get; set; }

    /// <summary>An optional callback invoked synchronously at the end of <see cref="HandleDisconnected"/>, letting a test observe collaborator state exactly as it stood when the connection notified this session of disconnection.</summary>
    public Action? OnDisconnected { get; set; }

    /// <summary>The message <see cref="PreparePairingDisplay"/> returns.</summary>
    public IpcPairingDisplayMessage? PairingDisplayResult { get; set; }

    /// <summary>The message <see cref="PreparePairingAttemptsExhausted"/> returns.</summary>
    public IpcPairingAttemptsExhaustedMessage? PairingAttemptsExhaustedResult { get; set; }

    /// <summary>The result <see cref="HandlePairingDisplayAck"/> returns.</summary>
    public bool? PairingDisplayAckResult { get; set; }

    /// <summary>The acknowledgements passed to <see cref="HandlePairingDisplayAck"/>, in call order.</summary>
    public List<IpcPairingDisplayAckMessage> HandledPairingDisplayAcks { get; } = [];

    /// <summary>The correlation ids passed to <see cref="CancelPendingPairingDisplay"/>, in call order.</summary>
    public List<ulong> CancelledPendingPairingDisplayCorrelationIds { get; } = [];

    /// <summary>The requests passed to <see cref="HandleTrustAdminRequestAsync"/>, in call order.</summary>
    public List<IpcTrustAdminRequestMessage> HandledTrustAdminRequests { get; } = [];

    /// <summary>The result <see cref="HandleTrustAdminRequestAsync"/> returns.</summary>
    public string TrustAdminRequestResult { get; set; } = string.Empty;

    /// <summary>Whether <see cref="PrepareCancel"/> throws instead of returning its normal result.</summary>
    public bool ThrowOnPrepareCancel { get; set; }

    /// <summary>The correlation ids passed to <see cref="PrepareCancel"/>, in call order.</summary>
    public List<ulong> PreparedCancelCorrelationIds { get; } = [];

    /// <summary>
    /// Overrides <see cref="HandleTrustAdminRequestAsync"/>'s entire behavior when set, letting a
    /// test control its completion timing and observe or honor the passed cancellation token.
    /// Defaults to completing immediately with <see cref="TrustAdminRequestResult"/>.
    /// </summary>
    public Func<IpcTrustAdminRequestMessage, CancellationToken, Task<string>>? HandleTrustAdminRequestOverride { get; set; }

    /// <inheritdoc/>
    public AdapterHandshakeResult Handshake(IpcHelloMessage hello)
    {
        LifecycleCalls.Add(nameof(Handshake));
        HandshakeCalls.Add(hello);
        return HandshakeResult;
    }

    /// <inheritdoc/>
    public void CommitHandshake()
    {
        LifecycleCalls.Add(nameof(CommitHandshake));
        CommitHandshakeCalls++;
    }

    /// <inheritdoc/>
    public IpcResynchronizeRequestMessage PrepareResynchronizeRequest()
    {
        LifecycleCalls.Add(nameof(PrepareResynchronizeRequest));
        return ResynchronizeRequest;
    }

    /// <inheritdoc/>
    public AdapterIpcOutcome HandleFrame(IpcMessage message)
    {
        HandledFrames.Add(message);
        return FrameOutcome;
    }

    /// <inheritdoc/>
    public AdapterIpcOutcome HandleDecodeFailure()
    {
        DecodeFailureCalls++;
        return DecodeFailureOutcome;
    }

    /// <inheritdoc/>
    public IpcListenEventMessage? PrepareListenEvent(uint eventKey) => ListenEventResult;

    /// <inheritdoc/>
    public IpcReadSampleMessage? PrepareReadSample(uint sampleToken) => ReadSampleResult;

    /// <inheritdoc/>
    public IpcCancelMessage? PrepareCancel(ulong correlationId)
    {
        PreparedCancelCorrelationIds.Add(correlationId);
        if (ThrowOnPrepareCancel)
        {
            throw new InvalidOperationException("Test-induced PrepareCancel failure.");
        }

        return PrepareCancelReturnsNull ? null : new IpcCancelMessage(correlationId);
    }

    /// <inheritdoc/>
    public void HandleDisconnected()
    {
        DisconnectedCalls++;
        OnDisconnected?.Invoke();
    }

    /// <inheritdoc/>
    public IpcPairingDisplayMessage? PreparePairingDisplay(string code, PairingDisplayMode mode) => PairingDisplayResult;

    /// <inheritdoc/>
    public IpcPairingAttemptsExhaustedMessage? PreparePairingAttemptsExhausted() => PairingAttemptsExhaustedResult;

    /// <inheritdoc/>
    public bool? HandlePairingDisplayAck(IpcPairingDisplayAckMessage ack)
    {
        HandledPairingDisplayAcks.Add(ack);
        return PairingDisplayAckResult;
    }

    /// <inheritdoc/>
    public void CancelPendingPairingDisplay(ulong correlationId) => CancelledPendingPairingDisplayCorrelationIds.Add(correlationId);

    /// <inheritdoc/>
    public Task<string> HandleTrustAdminRequestAsync(IpcTrustAdminRequestMessage request, CancellationToken cancellationToken = default)
    {
        HandledTrustAdminRequests.Add(request);
        return HandleTrustAdminRequestOverride is not null
            ? HandleTrustAdminRequestOverride(request, cancellationToken)
            : Task.FromResult(TrustAdminRequestResult);
    }
}
