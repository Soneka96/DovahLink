using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="ILiveCaptureSink"/> that records every applied capture result.</summary>
public sealed class FakeLiveCaptureSink : ILiveCaptureSink
{
    /// <summary>Every capture result passed to <see cref="ApplyCaptureResult"/>, in call order.</summary>
    public List<IpcCaptureResultMessage> Applied { get; } = [];

    /// <summary>Every source passed to <see cref="ApplyCaptureResult"/>, in call order, parallel to <see cref="Applied"/>.</summary>
    public List<AdapterCaptureSource> Sources { get; } = [];

    /// <inheritdoc/>
    public event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <inheritdoc/>
    public void ApplyCaptureResult(IpcCaptureResultMessage captureResult, AdapterCaptureSource source)
    {
        CaptureResultApplied?.Invoke(captureResult, source.ConnectionGeneration);
        Applied.Add(captureResult);
        Sources.Add(source);
    }
}
