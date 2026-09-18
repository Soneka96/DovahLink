namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The domain-facing entry point for adapter-reported capture results. Keeps
/// <see cref="AdapterIpcSession"/> itself free of state-area decoding and application policy: the
/// session only routes a decoded <see cref="IpcCaptureResultMessage"/> here, and this sink owns
/// everything about turning it into authoritative state.
/// </summary>
public interface ILiveCaptureSink
{
    /// <summary>
    /// Raised at the start of every <see cref="ApplyCaptureResult"/> call, before any of its own
    /// recognition, provenance, or decoding checks -- carrying the raw result and the adapter
    /// connection generation it arrived under, per <see cref="IAdapterAvailabilityTracker"/>'s own
    /// canonical numbering. Lets an interested collaborator (for example <c>LiveStateScheduler</c>,
    /// releasing its own per-sample outstanding-request tracking) observe that a reply arrived at
    /// all, independently of whether this sink goes on to actually apply it. This is the sink's own
    /// event specifically so a listener like the scheduler never needs a direct dependency on
    /// <see cref="AdapterIpcSession"/> or the adapter-IPC listener it would otherwise have to reach
    /// through -- avoiding a composition-root dependency cycle back through the connection factory
    /// that builds every session.
    /// </summary>
    event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <summary>Applies one adapter-reported capture result.</summary>
    /// <param name="captureResult">The decoded capture result to apply.</param>
    void ApplyCaptureResult(IpcCaptureResultMessage captureResult);
}
