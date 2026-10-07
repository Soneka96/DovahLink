using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.State;

/// <summary>
/// The single mutable Host owner of every live state area's authoritative current state: its typed
/// value, revision, capture provenance, and the serialized Snapshot a public client is replayed.
/// There is exactly one record per area, so a value, its revision, and its replay Snapshot can never
/// disagree -- <see cref="TryGetSnapshot"/> reporting <see langword="false"/> means the Host does not
/// currently consider the area authoritative and replayable. The public publication layer is a view
/// over this owner, not a second current-state store.
/// </summary>
/// <remarks>
/// Applying a value and committing its replay Snapshot are one step under one lock, so a caller that
/// observes an accepted <see cref="StateApplyResult"/> already has a replayable area.
/// </remarks>
public interface IAuthoritativeStateStore
{
    /// <summary>
    /// Raised, under the store's ordering lock, when a registered area's value changes through an
    /// Event-mode application, carrying the complete post-change state. Each subscriber's failure is
    /// contained individually.
    /// </summary>
    event Action<StateEventPublication>? EventOccurred;

    /// <summary>
    /// Raised, under the store's ordering lock, when a registered area's value changes through a
    /// Snapshot-mode application, carrying the complete current state. Each subscriber's failure is
    /// contained individually.
    /// </summary>
    event Action<StateSnapshotPublication>? SnapshotChanged;

    /// <summary>
    /// Raised when adapter resynchronization completes and stored Snapshots may now be readable. Only an
    /// availability hint: a consumer must still call <see cref="TryGetSnapshot"/>.
    /// </summary>
    event Action? SnapshotAvailabilityChanged;

    /// <summary>
    /// Tries to read an area's current state as a complete Snapshot, regardless of its update mode.
    /// Reports unavailable while the area is unregistered, the adapter is unavailable or needs
    /// resynchronization, or the stored state's adapter, play-context, or state-authority provenance is
    /// no longer current.
    /// </summary>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="snapshot">The area's current Snapshot, if available.</param>
    /// <returns><see langword="true"/> if the area is authoritative and replayable.</returns>
    bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot);

    /// <summary>
    /// Creates the generic revision-zero unavailable baseline for an accepted area at a committed
    /// play-context boundary, without storing it or advancing any revision.
    /// </summary>
    /// <param name="areaId">The registered state area the baseline belongs to.</param>
    /// <param name="playContext">The newly committed play-context identity and generation.</param>
    /// <param name="occurredAt">The time the Host established the boundary baseline.</param>
    /// <returns>A generic unavailable Snapshot owned by the supplied identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="areaId"/> is not registered.</exception>
    /// <exception cref="InvalidOperationException">The state-authority lifecycle is faulted.</exception>
    StateSnapshotPublication CreateUnavailableBoundaryBaseline(StateAreaId areaId, PlayContextSnapshot playContext, DateTimeOffset occurredAt);

    /// <summary>
    /// Tries to read an area's current typed value under the same currentness rules as
    /// <see cref="TryGetSnapshot"/>.
    /// </summary>
    /// <typeparam name="TState">The value type the area was first written with.</typeparam>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="value">The area's current value, if available.</param>
    /// <returns><see langword="true"/> if a current value is available.</returns>
    /// <exception cref="InvalidOperationException">The area was first written with a different value type.</exception>
    bool TryGetCurrentValue<TState>(StateAreaId areaId, [MaybeNullWhen(false)] out TState value);

    /// <summary>Reads an area's revision within the current play context, without regard to whether it is currently replayable.</summary>
    /// <param name="areaId">The state area to read.</param>
    /// <returns>The area's revision, or <see cref="RevisionNumber.Initial"/> if it has no state in the current play context.</returns>
    RevisionNumber CurrentRevision(StateAreaId areaId);

    /// <summary>
    /// Applies an ordinary Snapshot-mode capture from the current adapter, advancing the area's
    /// revision only if its value changed. Rejected outright while the adapter needs resynchronization
    /// or when the supplied identity is no longer current.
    /// </summary>
    /// <typeparam name="TState">The captured value type; an area's type is fixed by its first write.</typeparam>
    /// <param name="sourceInstanceId">The adapter instance that produced the value.</param>
    /// <param name="sourceConnectionGeneration">The adapter connection generation that produced the value.</param>
    /// <param name="capturedPlayContextId">The play context that was current when the value was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation current when the value was captured.</param>
    /// <param name="occurredAt">When the value was captured, for display and diagnostics only.</param>
    /// <param name="areaId">The state area the value belongs to.</param>
    /// <param name="value">The captured value.</param>
    /// <returns>The atomic outcome; when accepted, the area is already replayable.</returns>
    /// <exception cref="InvalidOperationException">No play context is established, or the area was first written with a different value type.</exception>
    StateApplyResult Apply<TState>(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt,
        StateAreaId areaId,
        TState value);

    /// <summary>
    /// Applies an identified resynchronization baseline while the adapter is gated. A value that equals
    /// the stored one restores currentness under the new provenance without a revision change or a
    /// change notification; a different value is a normal change.
    /// </summary>
    /// <typeparam name="TState">The captured value type; an area's type is fixed by its first write.</typeparam>
    /// <param name="mode">Whether a changed baseline is announced as a Snapshot or an Event.</param>
    /// <param name="resynchronizationToken">The current resynchronization authorization.</param>
    /// <param name="capturedPlayContextId">The play context that was current when the baseline was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation current when the baseline was captured.</param>
    /// <param name="occurredAt">When the baseline was captured, for display and diagnostics only.</param>
    /// <param name="areaId">The state area the baseline belongs to.</param>
    /// <param name="value">The baseline value.</param>
    /// <param name="onCommitted">
    /// Optional step run exactly once, under the store's ordering lock, after the baseline is committed
    /// as the area's replayable current state and before any change notification is raised; never run
    /// when the baseline is rejected. It lets the caller count the baseline toward a resynchronization
    /// transaction so completion becomes observable before the change notification, never before the
    /// area can be replayed. Used only on this path, because only a baseline completes a transaction.
    /// </param>
    /// <returns>The atomic outcome; when accepted, the area is already replayable.</returns>
    /// <exception cref="InvalidOperationException">No play context is established, or the area was first written with a different value type.</exception>
    StateApplyResult ApplyResynchronizationBaseline<TState>(
        UpdateMode mode,
        IAdapterResynchronizationToken resynchronizationToken,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt,
        StateAreaId areaId,
        TState value,
        Action? onCommitted = null);

    /// <summary>
    /// Applies a reliable Event from the current adapter. The store decides under its lock whether the
    /// adapter still needs resynchronization: ordinary source authority is used when it does not, and
    /// the supplied token is required when it does.
    /// </summary>
    /// <typeparam name="TState">The captured value type; an area's type is fixed by its first write.</typeparam>
    /// <param name="sourceInstanceId">The adapter instance that produced the Event.</param>
    /// <param name="sourceConnectionGeneration">The adapter connection generation that produced the Event.</param>
    /// <param name="capturedPlayContextId">The play context that was current when the Event was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation current when the Event was captured.</param>
    /// <param name="resynchronizationToken">The current resynchronization authorization when the adapter is gated, or <see langword="null"/> otherwise.</param>
    /// <param name="occurredAt">When the Event was captured, for display and diagnostics only.</param>
    /// <param name="areaId">The state area the Event belongs to.</param>
    /// <param name="value">The Event's resulting value.</param>
    /// <returns>The atomic outcome; when accepted, the area is already replayable.</returns>
    /// <exception cref="InvalidOperationException">No play context is established, or the area was first written with a different value type.</exception>
    StateApplyResult ApplyEvent<TState>(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        IAdapterResynchronizationToken? resynchronizationToken,
        DateTimeOffset occurredAt,
        StateAreaId areaId,
        TState value);
}

/// <inheritdoc cref="IAuthoritativeStateStore"/>
/// <remarks>
/// Constructed once for the Host process's lifetime. It subscribes to the play-context and
/// adapter-availability trackers for that entire lifetime and never unsubscribes, matching every
/// other tracker-composing type in this area.
/// </remarks>
public sealed class AuthoritativeStateStore : IAuthoritativeStateStore
{
    /// <summary>The adapter availability tracker consulted for every apply and read.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>The play-context tracker whose current context scopes every record and revision.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The registered-area policy outside which nothing is stored or published.</summary>
    private readonly IRegisteredStateAreaPolicy registeredStateAreaPolicy;

    /// <summary>The Host continuity epoch stamped on every committed record.</summary>
    private readonly IStateAuthorityLifecycle stateAuthorityLifecycle;

    /// <summary>Guards both dictionaries and serializes every check-commit-raise step.</summary>
    private readonly object gate = new();

    /// <summary>The value type each area was first written with; kept across play-context transitions.</summary>
    private readonly Dictionary<StateAreaId, Type> valueTypesByArea = new();

    /// <summary>Each area's one authoritative record under the play context that produced it.</summary>
    private readonly Dictionary<StateAreaId, AreaRecord> recordsByArea = new();

    /// <summary>Creates the store, subscribed to the adapter-availability and play-context lifecycles for the Host process's lifetime.</summary>
    /// <param name="adapterAvailabilityTracker">The adapter availability tracker the store validates against.</param>
    /// <param name="playContextTracker">The play-context tracker the store scopes records to.</param>
    /// <param name="registeredStateAreaPolicy">The registered-area policy the store never commits outside of.</param>
    /// <param name="stateAuthorityLifecycle">The Host continuity epoch stamped onto each committed record.</param>
    public AuthoritativeStateStore(
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker,
        IRegisteredStateAreaPolicy registeredStateAreaPolicy,
        IStateAuthorityLifecycle stateAuthorityLifecycle)
    {
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;
        this.registeredStateAreaPolicy = registeredStateAreaPolicy;
        this.stateAuthorityLifecycle = stateAuthorityLifecycle;

        playContextTracker.Transitioned += OnPlayContextTransitioned;
        adapterAvailabilityTracker.AvailabilityChanged += OnAdapterAvailabilityChanged;
        adapterAvailabilityTracker.Resynchronized += (_, _) => RaiseContained(SnapshotAvailabilityChanged);
    }

    /// <inheritdoc/>
    public event Action<StateEventPublication>? EventOccurred;

    /// <inheritdoc/>
    public event Action<StateSnapshotPublication>? SnapshotChanged;

    /// <inheritdoc/>
    public event Action? SnapshotAvailabilityChanged;

    /// <inheritdoc/>
    public bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot)
    {
        lock (gate)
        {
            if (!TryGetCurrentRecordLocked(areaId, out AreaRecord? record))
            {
                snapshot = null;
                return false;
            }

            snapshot = ToSnapshot(areaId, record);
            return true;
        }
    }

    /// <inheritdoc/>
    public StateSnapshotPublication CreateUnavailableBoundaryBaseline(StateAreaId areaId, PlayContextSnapshot playContext, DateTimeOffset occurredAt)
    {
        if (!registeredStateAreaPolicy.IsRegistered(areaId))
        {
            throw new ArgumentException("The state area is not registered.", nameof(areaId));
        }

        JsonElement unavailableData = JsonSerializer.SerializeToElement(new { value = (object?)null });
        return new StateSnapshotPublication(
            areaId,
            stateAuthorityLifecycle.Current,
            RevisionNumber.Initial,
            occurredAt,
            unavailableData,
            playContext.Current,
            playContext.TransitionGeneration);
    }

    /// <inheritdoc/>
    public bool TryGetCurrentValue<TState>(StateAreaId areaId, [MaybeNullWhen(false)] out TState value)
    {
        lock (gate)
        {
            ThrowIfBoundToDifferentType<TState>(areaId);
            if (!TryGetCurrentRecordLocked(areaId, out AreaRecord? record))
            {
                value = default;
                return false;
            }

            value = (TState)record.Value!;
            return true;
        }
    }

    /// <inheritdoc/>
    public RevisionNumber CurrentRevision(StateAreaId areaId)
    {
        lock (gate)
        {
            PlayContextId? currentContext = playContextTracker.GetSnapshot().Current;
            return currentContext is not null
                && recordsByArea.TryGetValue(areaId, out AreaRecord? record)
                && record.PlayContextId == currentContext.Value
                    ? record.Revision
                    : RevisionNumber.Initial;
        }
    }

    /// <inheritdoc/>
    public StateApplyResult Apply<TState>(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt,
        StateAreaId areaId,
        TState value)
    {
        return Commit(
            ApplyAuthority.Ordinary, UpdateMode.Snapshot, sourceInstanceId, sourceConnectionGeneration,
            capturedPlayContextId, capturedPlayContextGeneration, null, occurredAt, areaId, value);
    }

    /// <inheritdoc/>
    public StateApplyResult ApplyResynchronizationBaseline<TState>(
        UpdateMode mode,
        IAdapterResynchronizationToken resynchronizationToken,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt,
        StateAreaId areaId,
        TState value,
        Action? onCommitted = null)
    {
        return Commit(
            ApplyAuthority.ResynchronizationBaseline, mode, null, null,
            capturedPlayContextId, capturedPlayContextGeneration, resynchronizationToken, occurredAt, areaId, value, onCommitted);
    }

    /// <inheritdoc/>
    public StateApplyResult ApplyEvent<TState>(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        IAdapterResynchronizationToken? resynchronizationToken,
        DateTimeOffset occurredAt,
        StateAreaId areaId,
        TState value)
    {
        return Commit(
            ApplyAuthority.Event, UpdateMode.Event, sourceInstanceId, sourceConnectionGeneration,
            capturedPlayContextId, capturedPlayContextGeneration, resynchronizationToken, occurredAt, areaId, value);
    }

    /// <summary>
    /// Shared implementation behind every apply method: validates the caller's authority and captured
    /// provenance against current Host state, replaces the area's one record, and raises the resulting
    /// publication -- all under <see cref="gate"/>, so the record, its revision, and its replay Snapshot
    /// are committed together before any subscriber or caller can observe them.
    /// </summary>
    /// <typeparam name="TState">The captured value type.</typeparam>
    /// <param name="authority">The capture authority policy to validate.</param>
    /// <param name="mode">Whether a changed value is announced as a Snapshot or an Event.</param>
    /// <param name="sourceInstanceId">The producing adapter instance; <see langword="null"/> for a resynchronization baseline.</param>
    /// <param name="sourceConnectionGeneration">The producing adapter connection generation; <see langword="null"/> for a resynchronization baseline.</param>
    /// <param name="capturedPlayContextId">The play context current when the value was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context generation current when the value was captured.</param>
    /// <param name="resynchronizationToken">The claimed resynchronization authorization for a baseline or gated Event.</param>
    /// <param name="occurredAt">When the value was captured.</param>
    /// <param name="areaId">The state area the value belongs to.</param>
    /// <param name="value">The value to apply.</param>
    /// <param name="onCommitted">Optional step run under <see cref="gate"/> after the record is committed and before any notification; see <see cref="ApplyResynchronizationBaseline{TState}"/>.</param>
    /// <returns>The atomic outcome of this call.</returns>
    /// <exception cref="InvalidOperationException">No play context is established, or the area was first written with a different value type.</exception>
    private StateApplyResult Commit<TState>(
        ApplyAuthority authority,
        UpdateMode mode,
        AdapterInstanceId? sourceInstanceId,
        long? sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        IAdapterResynchronizationToken? resynchronizationToken,
        DateTimeOffset occurredAt,
        StateAreaId areaId,
        TState value,
        Action? onCommitted = null)
    {
        lock (gate)
        {
            ThrowIfBoundToDifferentType<TState>(areaId);

            AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
            if (adapterSnapshot.Current != AdapterAvailability.Available)
            {
                return StateApplyResult.Rejected;
            }

            if (authority == ApplyAuthority.Ordinary && adapterSnapshot.NeedsResynchronization)
            {
                return StateApplyResult.Rejected;
            }

            if (authority == ApplyAuthority.ResynchronizationBaseline && !adapterSnapshot.NeedsResynchronization)
            {
                return StateApplyResult.Rejected;
            }

            if (authority != ApplyAuthority.ResynchronizationBaseline
                && (adapterSnapshot.CurrentInstanceId != sourceInstanceId
                    || adapterSnapshot.ConnectionGeneration != sourceConnectionGeneration))
            {
                return StateApplyResult.Rejected;
            }

            if ((authority == ApplyAuthority.ResynchronizationBaseline
                    || (authority == ApplyAuthority.Event && adapterSnapshot.NeedsResynchronization))
                && (resynchronizationToken is null
                    || !adapterAvailabilityTracker.IsCurrentResynchronizationToken(resynchronizationToken)))
            {
                return StateApplyResult.Rejected;
            }

            PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
            PlayContextId currentContext = contextSnapshot.Current
                ?? throw new InvalidOperationException("Cannot apply captured state before a play context has been established.");

            if (capturedPlayContextId != currentContext || capturedPlayContextGeneration != contextSnapshot.TransitionGeneration)
            {
                // A capture stamped with a play context or generation other than the one currently
                // applying state was queued or delayed across a transition; applying it here would
                // silently misattribute an old context's value to the new one.
                return StateApplyResult.Rejected;
            }

            if (playContextTracker.GetSnapshot().TransitionGeneration != contextSnapshot.TransitionGeneration
                || !registeredStateAreaPolicy.IsRegistered(areaId)
                || !TryReadAuthority(out StateAuthorityId authorityId))
            {
                return StateApplyResult.Rejected;
            }

            AreaRecord? existing = recordsByArea.GetValueOrDefault(areaId);
            if (existing is not null && existing.PlayContextId != currentContext)
            {
                existing = null;
            }

            bool wasCurrent = existing is not null && IsCurrentLocked(existing, adapterSnapshot, contextSnapshot, authorityId);
            RevisionNumber baseRevision = existing?.Revision ?? RevisionNumber.Initial;
            bool changed = existing is null || !EqualityComparer<TState>.Default.Equals((TState)existing.Value!, value);
            RevisionNumber revision = changed ? baseRevision.Next() : baseRevision;

            // An unchanged value keeps its serialized data and, while it was already current, its capture
            // time; a value that was not current (a same-value resynchronization baseline) takes the new
            // capture time, because that capture is what makes it current again.
            JsonElement data = changed ? JsonSerializer.SerializeToElement(new { value }) : existing!.Data;
            DateTimeOffset recordedAt = changed || !wasCurrent ? occurredAt : existing!.OccurredAt;

            if (!TryReadAuthority(out StateAuthorityId authorityAtCommit) || authorityAtCommit != authorityId)
            {
                // The Host continuity epoch rotated while this value was being prepared; committing it
                // would stamp state onto an authority it was not captured under.
                return StateApplyResult.Rejected;
            }

            var record = new AreaRecord(
                value,
                revision,
                currentContext,
                contextSnapshot.TransitionGeneration,
                adapterSnapshot.CurrentInstanceId!.Value,
                adapterSnapshot.ConnectionGeneration,
                authorityId,
                recordedAt,
                data,
                Replayable: true);
            recordsByArea[areaId] = record;
            valueTypesByArea[areaId] = typeof(TState);
            onCommitted?.Invoke();

            if (changed)
            {
                if (mode == UpdateMode.Event)
                {
                    RaiseContained(EventOccurred, new StateEventPublication(
                        areaId, authorityId, baseRevision, revision, recordedAt, data, currentContext, contextSnapshot.TransitionGeneration));
                }
                else
                {
                    RaiseContained(SnapshotChanged, ToSnapshot(areaId, record));
                }
            }

            return new StateApplyResult(Accepted: true, changed, baseRevision, revision);
        }
    }

    /// <summary>Removes the prior context's records, so the new context's revisions restart at the initial revision.</summary>
    /// <param name="transition">The committed play-context transition.</param>
    private void OnPlayContextTransitioned(PlayContextTransition transition)
    {
        if (transition.PreviousPlayContextId is not PlayContextId previousContext)
        {
            return;
        }

        lock (gate)
        {
            foreach (StateAreaId areaId in recordsByArea
                .Where(pair => pair.Value.PlayContextId == previousContext)
                .Select(pair => pair.Key)
                .ToList())
            {
                recordsByArea.Remove(areaId);
            }
        }
    }

    /// <summary>
    /// Makes every record non-replayable and advances the revision of each record in the current play
    /// context. The typed value is retained, so a same-value resynchronization baseline can restore
    /// currentness without a value change.
    /// </summary>
    /// <param name="transition">The committed adapter availability transition. Unused: every transition invalidates unconditionally.</param>
    private void OnAdapterAvailabilityChanged(AdapterAvailabilityTransition transition)
    {
        lock (gate)
        {
            PlayContextId? currentContext = playContextTracker.GetSnapshot().Current;
            foreach (StateAreaId areaId in recordsByArea.Keys.ToList())
            {
                AreaRecord record = recordsByArea[areaId];
                bool inCurrentContext = currentContext is not null && record.PlayContextId == currentContext.Value;
                recordsByArea[areaId] = record with
                {
                    Replayable = false,
                    Revision = inCurrentContext ? record.Revision.Next() : record.Revision,
                };
            }
        }
    }

    /// <summary>Fails loudly when <paramref name="areaId"/> was first written with a value type other than <typeparamref name="TState"/>. Must be called with <see cref="gate"/> held.</summary>
    /// <typeparam name="TState">The value type the caller is using.</typeparam>
    /// <param name="areaId">The state area being used.</param>
    /// <exception cref="InvalidOperationException">The area is bound to a different value type.</exception>
    private void ThrowIfBoundToDifferentType<TState>(StateAreaId areaId)
    {
        if (valueTypesByArea.TryGetValue(areaId, out Type? boundType) && boundType != typeof(TState))
        {
            throw new InvalidOperationException(
                $"State area '{areaId}' holds {boundType} values and cannot be used with {typeof(TState)}.");
        }
    }

    /// <summary>Reads the current state authority, treating a faulted lifecycle as unavailable.</summary>
    /// <param name="authorityId">The current authority, if readable.</param>
    /// <returns><see langword="true"/> when the lifecycle produced an authority.</returns>
    private bool TryReadAuthority(out StateAuthorityId authorityId)
    {
        try
        {
            authorityId = stateAuthorityLifecycle.Current;
            return true;
        }
        catch (InvalidOperationException)
        {
            authorityId = default;
            return false;
        }
    }

    /// <summary>Finds an area's record only if it is registered and currently authoritative and replayable. Must be called with <see cref="gate"/> held.</summary>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="record">The area's current record, if any.</param>
    /// <returns><see langword="true"/> when the area is currently replayable.</returns>
    private bool TryGetCurrentRecordLocked(StateAreaId areaId, [MaybeNullWhen(false)] out AreaRecord record)
    {
        if (registeredStateAreaPolicy.IsRegistered(areaId)
            && recordsByArea.TryGetValue(areaId, out AreaRecord? candidate)
            && TryReadAuthority(out StateAuthorityId authorityId)
            && IsCurrentLocked(candidate, adapterAvailabilityTracker.GetSnapshot(), playContextTracker.GetSnapshot(), authorityId))
        {
            record = candidate;
            return true;
        }

        record = null;
        return false;
    }

    /// <summary>
    /// Decides whether a record may be handed out as current: the adapter is available and not awaiting
    /// resynchronization, and the record's adapter instance, connection generation, play context,
    /// generation, and state authority all still match.
    /// </summary>
    /// <param name="record">The stored record to check.</param>
    /// <param name="adapterSnapshot">The adapter availability read for this decision.</param>
    /// <param name="contextSnapshot">The play context read for this decision.</param>
    /// <param name="authorityId">The current state authority.</param>
    /// <returns><see langword="true"/> when the record is current.</returns>
    private static bool IsCurrentLocked(
        AreaRecord record,
        AdapterAvailabilitySnapshot adapterSnapshot,
        PlayContextSnapshot contextSnapshot,
        StateAuthorityId authorityId) =>
        record.Replayable
        && adapterSnapshot.Current == AdapterAvailability.Available
        && !adapterSnapshot.NeedsResynchronization
        && record.AdapterInstanceId == adapterSnapshot.CurrentInstanceId
        && record.ConnectionGeneration == adapterSnapshot.ConnectionGeneration
        && contextSnapshot.Current == record.PlayContextId
        && contextSnapshot.TransitionGeneration == record.PlayContextGeneration
        && record.StateAuthorityId == authorityId;

    /// <summary>Projects a record to the Snapshot a public client is replayed.</summary>
    /// <param name="areaId">The record's state area.</param>
    /// <param name="record">The record to project.</param>
    /// <returns>The record's complete current state.</returns>
    private static StateSnapshotPublication ToSnapshot(StateAreaId areaId, AreaRecord record) =>
        new(areaId, record.StateAuthorityId, record.Revision, record.OccurredAt, record.Data, record.PlayContextId, record.PlayContextGeneration);

    /// <summary>
    /// Invokes every subscriber, containing each one's exception individually, so one failing subscriber
    /// can neither suppress delivery to the rest nor escape into the capture path that produced the change.
    /// </summary>
    /// <typeparam name="T">The publication type.</typeparam>
    /// <param name="handlers">The event's current handlers.</param>
    /// <param name="publication">The publication to deliver.</param>
    private static void RaiseContained<T>(Action<T>? handlers, T publication)
    {
        foreach (Delegate subscriber in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<T>)subscriber).Invoke(publication);
            }
            catch (Exception)
            {
                // A subscriber's own failure must never prevent another subscriber from receiving the publication.
            }
        }
    }

    /// <summary>Invokes every parameterless subscriber, containing each one's exception individually.</summary>
    /// <param name="handlers">The event's current handlers.</param>
    private static void RaiseContained(Action? handlers)
    {
        foreach (Delegate subscriber in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)subscriber).Invoke();
            }
            catch (Exception)
            {
                // A failed availability hint must not prevent other consumers from retrying.
            }
        }
    }

    /// <summary>
    /// One area's complete authoritative state, replaced whole on every commit. Value, revision,
    /// provenance, and the serialized replay data live here together, which is what makes drift between
    /// them impossible.
    /// </summary>
    /// <param name="Value">The typed value, boxed; its type is the area's bound value type.</param>
    /// <param name="Revision">The revision within <paramref name="PlayContextId"/>.</param>
    /// <param name="PlayContextId">The play context the value was committed under.</param>
    /// <param name="PlayContextGeneration">The play-context transition generation it was committed under.</param>
    /// <param name="AdapterInstanceId">The adapter instance that produced the value.</param>
    /// <param name="ConnectionGeneration">The adapter connection generation that produced the value.</param>
    /// <param name="StateAuthorityId">The Host continuity epoch it was committed under.</param>
    /// <param name="OccurredAt">When the replayed state was captured.</param>
    /// <param name="Data">The complete serialized state a Snapshot or Event carries.</param>
    /// <param name="Replayable">Cleared by an adapter availability transition until a fresh capture re-establishes currentness.</param>
    private sealed record AreaRecord(
        object? Value,
        RevisionNumber Revision,
        PlayContextId PlayContextId,
        long PlayContextGeneration,
        AdapterInstanceId AdapterInstanceId,
        long ConnectionGeneration,
        StateAuthorityId StateAuthorityId,
        DateTimeOffset OccurredAt,
        JsonElement Data,
        bool Replayable);

    /// <summary>Identifies the authority policy one apply operation must validate.</summary>
    private enum ApplyAuthority
    {
        /// <summary>Requires ordinary current-adapter authority outside resynchronization.</summary>
        Ordinary,

        /// <summary>Requires the claimed token for an explicitly identified baseline.</summary>
        ResynchronizationBaseline,

        /// <summary>Uses current ordinary authority or current resynchronization Event authority.</summary>
        Event,
    }
}
