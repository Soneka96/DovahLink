using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.State;

/// <inheritdoc cref="IStatePublicationFeed"/>
/// <remarks>
/// Also implements <see cref="IStatePublicationSink"/>: one instance backs both the producer- and
/// consumer-facing interfaces, registered under each in composition. Constructed once for the host
/// process's lifetime, alongside the trackers it composes, matching <see cref="StatePublisher{TState}"/>'s
/// own lifetime discipline.
/// </remarks>
public sealed class StatePublicationFeed : IStatePublicationFeed, IStatePublicationSink
{
    /// <summary>The adapter availability tracker re-checked, atomic with every publish, before ever raising.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>The play-context tracker re-checked, atomic with every publish, before ever raising.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The registered-area policy re-checked, atomic with every publish, before ever raising.</summary>
    private readonly IRegisteredStateAreaPolicy registeredStateAreaPolicy;

    /// <summary>Guards <see cref="latestByArea"/> and serializes every check-then-raise publish.</summary>
    private readonly object gate = new();

    /// <summary>
    /// Each area's current value as a baseline snapshot -- populated by both <see cref="PublishSnapshot"/>
    /// and <see cref="PublishEvent"/>, since an Event-mode area still answers <see cref="TryGetSnapshot"/>
    /// with its latest post-change state for initial synchronization and recovery.
    /// </summary>
    private readonly Dictionary<StateAreaId, StateSnapshotPublication> latestByArea = new();

    /// <summary>Creates a publication feed.</summary>
    /// <param name="adapterAvailabilityTracker">The adapter availability tracker this feed re-validates freshness against.</param>
    /// <param name="playContextTracker">The play-context tracker this feed re-validates freshness against.</param>
    /// <param name="registeredStateAreaPolicy">The registered-area policy this feed never publishes outside of.</param>
    public StatePublicationFeed(
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker,
        IRegisteredStateAreaPolicy registeredStateAreaPolicy)
    {
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;
        this.registeredStateAreaPolicy = registeredStateAreaPolicy;
    }

    /// <inheritdoc/>
    public event Action<StateEventPublication>? EventOccurred;

    /// <inheritdoc/>
    public event Action<StateSnapshotPublication>? SnapshotChanged;

    /// <inheritdoc/>
    public bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot)
    {
        lock (gate)
        {
            return latestByArea.TryGetValue(areaId, out snapshot);
        }
    }

    /// <inheritdoc/>
    public void PublishSnapshot(
        StateAreaId areaId,
        RevisionNumber revision,
        JsonElement data,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt)
    {
        lock (gate)
        {
            if (!IsStillFreshLocked(areaId, capturedPlayContextId, capturedPlayContextGeneration))
            {
                return;
            }

            var publication = new StateSnapshotPublication(areaId, revision, occurredAt, data, capturedPlayContextId, capturedPlayContextGeneration);
            latestByArea[areaId] = publication;
            RaiseSnapshotChanged(publication);
        }
    }

    /// <inheritdoc/>
    public void PublishEvent(
        StateAreaId areaId,
        RevisionNumber baseRevision,
        RevisionNumber revision,
        JsonElement data,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt)
    {
        lock (gate)
        {
            if (!IsStillFreshLocked(areaId, capturedPlayContextId, capturedPlayContextGeneration))
            {
                return;
            }

            latestByArea[areaId] = new StateSnapshotPublication(areaId, revision, occurredAt, data, capturedPlayContextId, capturedPlayContextGeneration);
            RaiseEventOccurred(new StateEventPublication(areaId, baseRevision, revision, occurredAt, data, capturedPlayContextId, capturedPlayContextGeneration));
        }
    }

    /// <summary>
    /// Invokes every <see cref="SnapshotChanged"/> subscriber, containing each one's own exception
    /// individually -- the same isolation <see cref="Identity.StateAuthorityLifecycle"/> already
    /// applies to its own multi-subscriber events -- so one failing subscriber can never suppress
    /// this publication from reaching the rest, or propagate back into the capture path that called
    /// <see cref="PublishSnapshot"/>.
    /// </summary>
    private void RaiseSnapshotChanged(StateSnapshotPublication publication)
    {
        Delegate[]? subscribers = SnapshotChanged?.GetInvocationList();
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((Action<StateSnapshotPublication>)subscriber).Invoke(publication);
            }
            catch (Exception)
            {
                // A subscriber's own failure must never prevent another subscriber from receiving
                // this publication, or escape into the capture path that produced it.
            }
        }
    }

    /// <summary>Invokes every <see cref="EventOccurred"/> subscriber, containing each one's own exception individually. See <see cref="RaiseSnapshotChanged"/> for why.</summary>
    private void RaiseEventOccurred(StateEventPublication publication)
    {
        Delegate[]? subscribers = EventOccurred?.GetInvocationList();
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((Action<StateEventPublication>)subscriber).Invoke(publication);
            }
            catch (Exception)
            {
                // A subscriber's own failure must never prevent another subscriber from receiving
                // this publication, or escape into the capture path that produced it.
            }
        }
    }

    /// <summary>
    /// Re-checks registration, adapter availability, and play-context provenance -- the same
    /// discipline <see cref="StatePublisher{TState}"/>'s own <c>ApplyCore</c> already applied a
    /// separate lock scope earlier -- so a publish can never reach a subscriber after the world has
    /// already moved on. Must be called with <see cref="gate"/> already held.
    /// </summary>
    private bool IsStillFreshLocked(StateAreaId areaId, PlayContextId capturedPlayContextId, long capturedPlayContextGeneration)
    {
        if (!registeredStateAreaPolicy.IsRegistered(areaId))
        {
            return false;
        }

        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        if (adapterSnapshot.Current != AdapterAvailability.Available || adapterSnapshot.NeedsResynchronization)
        {
            return false;
        }

        PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
        return contextSnapshot.Current == capturedPlayContextId && contextSnapshot.TransitionGeneration == capturedPlayContextGeneration;
    }
}
