using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Time;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The domain-facing entry point for adapter-reported captures. It validates provenance, catalog
/// recognition, and play context, then dispatches through the registered handler for that identity.
/// </summary>
public interface ILiveCaptureSink
{
    /// <summary>
    /// Raised at the start of every <see cref="ApplyCaptureResult"/> call, before any of its own
    /// recognition, provenance, or handler checks -- carrying the raw result and the connection
    /// generation of the <c>source</c> passed to that call, not rediscovered from mutable
    /// global availability state. Lets an interested collaborator (for example
    /// <c>LiveStateScheduler</c>, releasing its own per-sample outstanding-request tracking) observe
    /// that a reply arrived at all, independently of whether this sink goes on to actually apply it.
    /// This is the sink's own event specifically so a listener like the scheduler never needs a
    /// direct dependency on <see cref="AdapterIpcSession"/> or the adapter-IPC listener it would
    /// otherwise have to reach through -- avoiding a composition-root dependency cycle back through
    /// the connection factory that builds every session.
    /// </summary>
    event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <summary>Applies one adapter-reported capture result.</summary>
    /// <param name="captureResult">The decoded capture result to apply.</param>
    /// <param name="source">The exact adapter connection that received <paramref name="captureResult"/>.</param>
    void ApplyCaptureResult(IpcCaptureResultMessage captureResult, AdapterCaptureSource source);
}

/// <inheritdoc cref="ILiveCaptureSink"/>
/// <remarks>
/// Rejects stale or unrecognized captures before dispatching a validated
/// <see cref="LiveCaptureContext"/> to its owning <see cref="ILiveCaptureHandler"/>. Catalog-recognized
/// identities without a registered handler fail closed.
/// </remarks>
public sealed class LiveCaptureSink : ILiveCaptureSink
{
    /// <summary>The catalog that defines recognizable capture units.</summary>
    private readonly LiveStateCatalog catalog;

    /// <summary>Maps each explicitly claimed source and key identity to its handler.</summary>
    private readonly IReadOnlyDictionary<(CaptureSourceKind Source, uint CaptureKey), ILiveCaptureHandler> handlersByCapture;

    /// <summary>Consulted for source validation and resynchronization gating.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>Consulted to reject a capture whose stamped play context has already gone stale.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Stamps accepted capture dispatches.</summary>
    private readonly IClock clock;

    /// <summary>Creates a live capture sink.</summary>
    /// <param name="catalog">The catalog that defines recognizable capture units.</param>
    /// <param name="handlers">The explicit set of handlers and the capture identities each owns.</param>
    /// <param name="adapterAvailabilityTracker">Consulted for source validation and resynchronization gating.</param>
    /// <param name="playContextTracker">Consulted to reject a capture whose stamped play context has already gone stale.</param>
    /// <param name="clock">Stamps accepted capture dispatches.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handlers"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Two handlers claim the same source and capture key.</exception>
    public LiveCaptureSink(
        LiveStateCatalog catalog,
        IEnumerable<ILiveCaptureHandler> handlers,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        this.catalog = catalog;
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;
        this.clock = clock;

        var handlerMap = new Dictionary<(CaptureSourceKind Source, uint CaptureKey), ILiveCaptureHandler>();
        foreach (ILiveCaptureHandler handler in handlers)
        {
            foreach ((CaptureSourceKind source, uint captureKey) in handler.SupportedCaptures)
            {
                if (!handlerMap.TryAdd((source, captureKey), handler))
                {
                    throw new InvalidOperationException(
                        $"Multiple live capture handlers claim source {source} and key {captureKey}.");
                }
            }
        }

        handlersByCapture = handlerMap;
    }

    /// <inheritdoc/>
    public event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <inheritdoc/>
    public void ApplyCaptureResult(IpcCaptureResultMessage captureResult, AdapterCaptureSource source)
    {
        CaptureResultApplied?.Invoke(captureResult, source.ConnectionGeneration);

        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        if (adapterSnapshot.Current != AdapterAvailability.Available
            || adapterSnapshot.CurrentInstanceId != source.InstanceId
            || adapterSnapshot.ConnectionGeneration != source.ConnectionGeneration)
        {
            return;
        }

        CaptureUnitDefinition? unit = catalog.CaptureUnits.FirstOrDefault(
            candidate => candidate.Source == captureResult.Source && candidate.CaptureKey == captureResult.CaptureKey);
        if (unit is null)
        {
            return;
        }

        if (!handlersByCapture.TryGetValue((captureResult.Source, captureResult.CaptureKey), out ILiveCaptureHandler? handler))
        {
            return;
        }

        PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
        if (contextSnapshot.Current is not PlayContextId currentContext || currentContext != captureResult.PlayContextId)
        {
            // Queued or delayed across a transition; applying it here would misattribute an old
            // context's value to the new one. See ai/context/host/architecture.md's provenance rule.
            return;
        }

        DateTimeOffset occurredAt = clock.UtcNow;
        handler.Handle(new LiveCaptureContext(
            captureResult,
            source,
            unit,
            adapterSnapshot,
            currentContext,
            contextSnapshot.TransitionGeneration,
            occurredAt));
    }
}
