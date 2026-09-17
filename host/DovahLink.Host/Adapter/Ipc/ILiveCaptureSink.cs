namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The domain-facing entry point for adapter-reported capture results. Keeps
/// <see cref="AdapterIpcSession"/> itself free of state-area decoding and application policy: the
/// session only routes a decoded <see cref="IpcCaptureResultMessage"/> here, and this sink owns
/// everything about turning it into authoritative state.
/// </summary>
public interface ILiveCaptureSink
{
    /// <summary>Applies one adapter-reported capture result.</summary>
    /// <param name="captureResult">The decoded capture result to apply.</param>
    void ApplyCaptureResult(IpcCaptureResultMessage captureResult);
}
