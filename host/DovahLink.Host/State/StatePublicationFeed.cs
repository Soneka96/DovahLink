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

    /// <summary>Creates a publication feed, subscribed to <paramref name="adapterAvailabilityTracker"/> and <paramref name="playContextTracker"/> for the host process's own lifetime; never unsubscribed.</summary>
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

        adapterAvailabilityTracker.AvailabilityChanged += HandleAdapterAvailabilityChanged;
        playContextTracker.Transitioned += HandlePlayContextTransitioned;
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
            if (!latestByArea.TryGetValue(areaId, out StateSnapshotPublication? candidate) || !IsCurrentLocked(candidate))
            {
                snapshot = null;
                return false;
            }

            snapshot = candidate;
            return true;
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

    /// <inheritdoc/>
    public void EstablishBaseline(
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

            latestByArea[areaId] = new StateSnapshotPublication(areaId, revision, occurredAt, data, capturedPlayContextId, capturedPlayContextGeneration);
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
    /// <remarks>
    /// Deliberately does not require <see cref="AdapterAvailabilitySnapshot.NeedsResynchronization"/>
    /// to be <see langword="false"/>: a resynchronization baseline is only ever accepted by
    /// <see cref="IStatePublisher{TState}.ApplyResynchronizationBaseline"/> while that flag is
    /// <see langword="true"/>, so requiring it clear here would make a legitimate baseline publish
    /// impossible to ever satisfy. The caller's own <c>Apply</c>/<c>ApplyResynchronizationBaseline</c>
    /// result already proves the operation matched the adapter's state at that moment; this re-check
    /// exists only to catch the adapter dropping again, or the play context moving on, in the gap
    /// between that call and this one.
    /// </remarks>
    private bool IsStillFreshLocked(StateAreaId areaId, PlayContextId capturedPlayContextId, long capturedPlayContextGeneration)
    {
        if (!registeredStateAreaPolicy.IsRegistered(areaId))
        {
            return false;
        }

        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        if (adapterSnapshot.Current != AdapterAvailability.Available)
        {
            return false;
        }

        PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
        return contextSnapshot.Current == capturedPlayContextId && contextSnapshot.TransitionGeneration == capturedPlayContextGeneration;
    }

    /// <summary>
    /// Checks whether a previously stored snapshot is still trustworthy to hand out as current: its
    /// area is still registered, the adapter is available and does not need resynchronization, and
    /// the play context it was captured under still matches. Stricter than
    /// <see cref="IsStillFreshLocked"/>'s own publish-time check, which deliberately allows
    /// <see cref="AdapterAvailabilitySnapshot.NeedsResynchronization"/> to be <see langword="true"/>
    /// so a legitimate baseline publish can still land -- a pull read must never hand out state while
    /// a resynchronization is outstanding, since the value it would return could already be
    /// superseded by whatever that resynchronization is about to establish. Defense in depth on top
    /// of <see cref="HandleAdapterAvailabilityChanged"/> and <see cref="HandlePlayContextTransitioned"/>
    /// proactively clearing <see cref="latestByArea"/> on the same triggers, not a substitute for
    /// them. Must be called with <see cref="gate"/> already held.
    /// </summary>
    /// <param name="publication">The previously stored snapshot to check.</param>
    private bool IsCurrentLocked(StateSnapshotPublication publication)
    {
        if (!registeredStateAreaPolicy.IsRegistered(publication.StateArea))
        {
            return false;
        }

        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        if (adapterSnapshot.Current != AdapterAvailability.Available || adapterSnapshot.NeedsResynchronization)
        {
            return false;
        }

        PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
        return contextSnapshot.Current == publication.PlayContextId && contextSnapshot.TransitionGeneration == publication.PlayContextGeneration;
    }

    /// <summary>
    /// Clears every stored snapshot on any adapter availability transition -- both a continuity loss
    /// and a fresh reconnect, since a reconnect also requires a new resynchronization before any
    /// cached value can be trusted again -- so a later <see cref="TryGetSnapshot"/> can never resurface
    /// pre-loss data even before a fresh publish overwrites it. Proactive defense in depth alongside
    /// <see cref="IsCurrentLocked"/>'s own re-check, not a substitute for it.
    /// </summary>
    /// <param name="transition">The committed availability transition. Unused: every transition clears unconditionally.</param>
    private void HandleAdapterAvailabilityChanged(AdapterAvailabilityTransition transition)
    {
        lock (gate)
        {
            latestByArea.Clear();
        }
    }

    /// <summary>Clears every stored snapshot on a play-context transition, for the same reason <see cref="HandleAdapterAvailabilityChanged"/> does.</summary>
    /// <param name="transition">The committed play-context transition. Unused: every transition clears unconditionally.</param>
    private void HandlePlayContextTransitioned(PlayContextTransition transition)
    {
        lock (gate)
        {
            latestByArea.Clear();
        }
    }
}
