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

    /// <summary>The message <see cref="PrepareCancel"/> returns.</summary>
    public IpcCancelMessage? CancelResult { get; set; }

    /// <summary>An optional callback invoked synchronously at the end of <see cref="HandleDisconnected"/>, letting a test observe collaborator state exactly as it stood when the connection notified this session of disconnection.</summary>
    public Action? OnDisconnected { get; set; }

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
    public IpcCancelMessage? PrepareCancel(ulong correlationId) => CancelResult;

    /// <inheritdoc/>
    public void HandleDisconnected()
    {
        DisconnectedCalls++;
        OnDisconnected?.Invoke();
    }
}
