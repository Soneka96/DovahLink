using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="ILiveCaptureSink"/> that records every applied capture result.</summary>
public sealed class FakeLiveCaptureSink : ILiveCaptureSink
{
    /// <summary>Every capture result passed to <see cref="ApplyCaptureResult"/>, in call order.</summary>
    public List<IpcCaptureResultMessage> Applied { get; } = [];

    /// <summary>The connection generation <see cref="ApplyCaptureResult"/> reports through <see cref="CaptureResultApplied"/>.</summary>
    public long ConnectionGeneration { get; set; }

    /// <inheritdoc/>
    public event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <inheritdoc/>
    public void ApplyCaptureResult(IpcCaptureResultMessage captureResult)
    {
        CaptureResultApplied?.Invoke(captureResult, ConnectionGeneration);
        Applied.Add(captureResult);
    }
}
