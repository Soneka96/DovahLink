using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="ILiveCaptureSink"/> that records every applied capture result.</summary>
public sealed class FakeLiveCaptureSink : ILiveCaptureSink
{
    /// <summary>Every capture result passed to <see cref="ApplyCaptureResult"/>, in call order.</summary>
    public List<IpcCaptureResultMessage> Applied { get; } = [];

    /// <inheritdoc/>
    public void ApplyCaptureResult(IpcCaptureResultMessage captureResult) => Applied.Add(captureResult);
}
