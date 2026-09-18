using System.Diagnostics.CodeAnalysis;
using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.State;

/// <summary>
/// The host's single per-state-area ordering point: applies a captured value, detects whether it
/// actually changed, and assigns the resulting revision. Generic
/// over the captured value's type so the ordering and provenance rules are independent of the
/// concrete Skyrim domain.
/// </summary>
/// <typeparam name="TState">The captured value type for one family of state areas.</typeparam>
public interface IStatePublisher<TState>
{
    /// <summary>
    /// Tries to read a state area's current value. Reports unavailable -- rather than a stale
    /// value -- when no play context is established or the adapter is unavailable or not yet
    /// resynchronized. Values from a disconnected or not-yet-resynchronized adapter are never current.
    /// </summary>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="value">The area's current value, if available.</param>
    /// <returns><see langword="true"/> if a current value is available.</returns>
    bool TryGetCurrentValue(StateAreaId areaId, [MaybeNullWhen(false)] out TState value);

    /// <summary>Reads a state area's current revision within the current play context.</summary>
    /// <param name="areaId">The state area to read.</param>
    /// <returns>The area's current revision, or <see cref="RevisionNumber.Initial"/> if no play context is established yet.</returns>
    RevisionNumber CurrentRevision(StateAreaId areaId);

    /// <summary>
    /// Applies a newly captured value from the current adapter, advancing the area's revision only if
    /// it actually changed. Rejects the value outright, without applying it, when
    /// <paramref name="capturedPlayContextId"/> or <paramref name="capturedPlayContextGeneration"/> no
    /// longer matches the play context this publisher currently applies state under -- provenance is a
    /// required caller-supplied fact about when the value was captured, not something this method
    /// infers from its own current state at apply time.
    /// </summary>
    /// <param name="sourceInstanceId">The adapter instance that produced the value.</param>
    /// <param name="sourceConnectionGeneration">The adapter connection generation that produced the value.</param>
    /// <param name="capturedPlayContextId">The play context that was current at the moment this value was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation that was current at the moment this value was captured.</param>
    /// <param name="areaId">The state area the value belongs to.</param>
    /// <param name="value">The newly captured value.</param>
    /// <returns>The atomic outcome of this call. See <see cref="StateApplyResult"/>.</returns>
    /// <exception cref="InvalidOperationException">No play context has been established yet.</exception>
    StateApplyResult Apply(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        StateAreaId areaId,
        TState value);

    /// <summary>
    /// Applies an explicitly identified resynchronization baseline while the adapter is gated. Subject
    /// to the same play-context provenance check as <see cref="Apply"/>: a token remains valid across a
    /// play-context transition (the two are independent authorities -- the token proves adapter
    /// connection/resync authority, provenance proves which play context produced the value), so a
    /// baseline captured under one context and applied only after the tracker has moved on to another
    /// is rejected outright rather than being stored under the new context.
    /// </summary>
    /// <param name="resynchronizationToken">The opaque authorization issued for the current adapter connection.</param>
    /// <param name="capturedPlayContextId">The play context that was current at the moment this baseline was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation that was current at the moment this baseline was captured.</param>
    /// <param name="areaId">The state area the baseline belongs to.</param>
    /// <param name="value">The baseline value.</param>
    /// <returns>The atomic outcome of this call. See <see cref="StateApplyResult"/>.</returns>
    /// <exception cref="InvalidOperationException">No play context has been established yet.</exception>
    StateApplyResult ApplyResynchronizationBaseline(
        IAdapterResynchronizationToken resynchronizationToken,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        StateAreaId areaId,
        TState value);

    /// <summary>
    /// Applies an Event from the current adapter. The publisher decides under its ordering lock
    /// whether the current adapter still needs resynchronization: ordinary source authority is used
    /// when it does not, and the supplied token is required when it does. This keeps the final
    /// ordinary-vs-resynchronization decision current rather than relying on an older caller snapshot.
    /// </summary>
    /// <param name="sourceInstanceId">The adapter instance that produced the Event.</param>
    /// <param name="sourceConnectionGeneration">The adapter connection generation that produced the Event.</param>
    /// <param name="capturedPlayContextId">The play context that was current at the moment this Event was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation that was current at the moment this Event was captured.</param>
    /// <param name="resynchronizationToken">The current resynchronization authorization when the adapter is still gated, or <see langword="null"/> when ordinary authority is expected.</param>
    /// <param name="areaId">The state area the Event belongs to.</param>
    /// <param name="value">The Event's resulting value.</param>
    /// <returns>The atomic outcome of this call. See <see cref="StateApplyResult"/>.</returns>
    /// <exception cref="InvalidOperationException">No play context has been established yet.</exception>
    StateApplyResult ApplyEvent(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        IAdapterResynchronizationToken? resynchronizationToken,
        StateAreaId areaId,
        TState value);
}

/// <inheritdoc cref="IStatePublisher{TState}"/>
/// <remarks>
/// Constructed once for the host process's lifetime, alongside the trackers it composes; it
/// subscribes to <see cref="IPlayContextTracker.Transitioned"/> for that entire lifetime and is
/// never unsubscribed, matching every other tracker in this area.
/// </remarks>
public sealed class StatePublisher<TState> : IStatePublisher<TState>
{
    /// <summary>The revision tracker this publisher assigns revisions through.</summary>
    private readonly IRevisionTracker revisionTracker;

    /// <summary>The play-context tracker whose current context scopes every value and revision.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The adapter availability tracker consulted before ever reporting a value as current.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>Guards <see cref="valuesByArea"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Every state area's most recently applied value under the current play context.</summary>
    private readonly Dictionary<StateAreaId, StoredValue> valuesByArea = new();

    /// <summary>Creates a state publisher.</summary>
    /// <param name="revisionTracker">The revision tracker this publisher assigns revisions through.</param>
    /// <param name="playContextTracker">The play-context tracker whose current context scopes every value and revision.</param>
    /// <param name="adapterAvailabilityTracker">The adapter availability tracker consulted before ever reporting a value as current.</param>
    public StatePublisher(IRevisionTracker revisionTracker, IPlayContextTracker playContextTracker, IAdapterAvailabilityTracker adapterAvailabilityTracker)
    {
        this.revisionTracker = revisionTracker;
        this.playContextTracker = playContextTracker;
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;

        playContextTracker.Transitioned += OnPlayContextTransitioned;
        adapterAvailabilityTracker.AvailabilityChanged += OnAdapterAvailabilityChanged;
    }

    /// <inheritdoc/>
    public bool TryGetCurrentValue(StateAreaId areaId, [MaybeNullWhen(false)] out TState value)
    {
        lock (gate)
        {
            PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
            AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
            if (contextSnapshot.Current is null ||
                adapterSnapshot.Current == AdapterAvailability.Unavailable ||
                adapterSnapshot.NeedsResynchronization)
            {
                value = default;
                return false;
            }

            if (!valuesByArea.TryGetValue(areaId, out StoredValue? storedValue) ||
                storedValue.PlayContextId != contextSnapshot.Current.Value ||
                storedValue.AdapterInstanceId != adapterSnapshot.CurrentInstanceId ||
                storedValue.ConnectionGeneration != adapterSnapshot.ConnectionGeneration)
            {
                value = default;
                return false;
            }

            if (playContextTracker.GetSnapshot().TransitionGeneration != contextSnapshot.TransitionGeneration)
            {
                value = default;
                return false;
            }

            value = storedValue.Value;
            return true;
        }
    }

    /// <inheritdoc/>
    public RevisionNumber CurrentRevision(StateAreaId areaId)
    {
        lock (gate)
        {
            while (true)
            {
                PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
                if (contextSnapshot.Current is null)
                {
                    return RevisionNumber.Initial;
                }

                RevisionNumber revision = revisionTracker.Current(contextSnapshot.Current.Value, areaId);
                if (playContextTracker.GetSnapshot().TransitionGeneration == contextSnapshot.TransitionGeneration)
                {
                    return revision;
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the current play-context generation inside the same lock that a concurrent
    /// transition's <see cref="OnPlayContextTransitioned"/> uses to remove prior-context values.
    /// </remarks>
    public StateApplyResult Apply(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        StateAreaId areaId,
        TState value)
    {
        return ApplyCore(
            sourceInstanceId, sourceConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration,
            null, areaId, value, ApplyAuthority.Ordinary);
    }

    /// <inheritdoc/>
    public StateApplyResult ApplyResynchronizationBaseline(
        IAdapterResynchronizationToken resynchronizationToken,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        StateAreaId areaId,
        TState value)
    {
        return ApplyCore(
            null, null, capturedPlayContextId, capturedPlayContextGeneration,
            resynchronizationToken, areaId, value, ApplyAuthority.ResynchronizationBaseline);
    }

    /// <inheritdoc/>
    public StateApplyResult ApplyEvent(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        IAdapterResynchronizationToken? resynchronizationToken,
        StateAreaId areaId,
        TState value)
    {
        return ApplyCore(
            sourceInstanceId, sourceConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration,
            resynchronizationToken, areaId, value, ApplyAuthority.Event);
    }

    /// <summary>
    /// Shared implementation behind <see cref="Apply"/>, <see cref="ApplyResynchronizationBaseline"/>,
    /// and <see cref="ApplyEvent"/>:
    /// validates the caller's authority (an ordinary capture's source adapter instance/connection
    /// generation, or a resynchronization baseline/Event's claimed token) and captured play-context
    /// provenance, then applies the value and advances the revision if it actually changed.
    /// </summary>
    /// <param name="sourceInstanceId">The adapter instance that produced the value; <see langword="null"/> for a resynchronization baseline.</param>
    /// <param name="sourceConnectionGeneration">The adapter connection generation that produced the value; <see langword="null"/> for a resynchronization baseline.</param>
    /// <param name="capturedPlayContextId">The play context that was current at the moment the value was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation that was current at the moment the value was captured.</param>
    /// <param name="resynchronizationToken">The claimed resynchronization authorization for a baseline or Event; <see langword="null"/> for an ordinary capture.</param>
    /// <param name="areaId">The state area the value belongs to.</param>
    /// <param name="value">The value to apply.</param>
    /// <param name="authority">The capture authority and current-state policy to validate.</param>
    /// <returns>The atomic outcome of this call. See <see cref="StateApplyResult"/>.</returns>
    /// <exception cref="InvalidOperationException">No play context has been established yet.</exception>
    private StateApplyResult ApplyCore(
        AdapterInstanceId? sourceInstanceId,
        long? sourceConnectionGeneration,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        IAdapterResynchronizationToken? resynchronizationToken,
        StateAreaId areaId,
        TState value,
        ApplyAuthority authority)
    {
        lock (gate)
        {
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

            if (playContextTracker.GetSnapshot().TransitionGeneration != contextSnapshot.TransitionGeneration)
            {
                return StateApplyResult.Rejected;
            }

            RevisionNumber baseRevision = revisionTracker.Current(currentContext, areaId);
            bool changed = !valuesByArea.TryGetValue(areaId, out StoredValue? existing)
                || !EqualityComparer<TState>.Default.Equals(existing.Value, value)
                || existing.PlayContextId != currentContext;

            valuesByArea[areaId] = new StoredValue(
                value,
                currentContext,
                adapterSnapshot.CurrentInstanceId!.Value,
                adapterSnapshot.ConnectionGeneration);

            RevisionNumber revision = changed
                ? revisionTracker.AdvanceOnChange(currentContext, areaId)
                : baseRevision;

            return new StateApplyResult(Accepted: true, changed, baseRevision, revision);
        }
    }

    /// <summary>Removes values belonging to the prior context and prunes its revisions.</summary>
    /// <param name="transition">The transition that just occurred.</param>
    private void OnPlayContextTransitioned(PlayContextTransition transition)
    {
        PlayContextId? previousContext = transition.PreviousPlayContextId;
        lock (gate)
        {
            if (previousContext.HasValue)
            {
                foreach (StateAreaId areaId in valuesByArea
                    .Where(pair => pair.Value.PlayContextId == previousContext.Value)
                    .Select(pair => pair.Key)
                    .ToList())
                {
                    valuesByArea.Remove(areaId);
                }
            }
        }

        if (previousContext.HasValue)
        {
            revisionTracker.InvalidateContext(previousContext.Value);
        }
    }

    /// <summary>Advances every populated area's revision when adapter availability changes.</summary>
    /// <param name="transition">The committed adapter availability transition.</param>
    private void OnAdapterAvailabilityChanged(AdapterAvailabilityTransition transition)
    {
        lock (gate)
        {
            PlayContextId? currentContext = playContextTracker.GetSnapshot().Current;
            if (currentContext is null)
            {
                return;
            }

            foreach (StateAreaId areaId in valuesByArea
                .Where(pair => pair.Value.PlayContextId == currentContext.Value)
                .Select(pair => pair.Key)
                .ToList())
            {
                revisionTracker.AdvanceOnChange(currentContext.Value, areaId);
            }
        }
    }

    /// <summary>A captured value tagged with its originating play context and adapter instance.</summary>
    /// <param name="Value">The captured value.</param>
    /// <param name="PlayContextId">The play context this value was captured under.</param>
    /// <param name="AdapterInstanceId">The adapter instance that produced this value.</param>
    /// <param name="ConnectionGeneration">The adapter connection generation that produced this value.</param>
    private sealed record StoredValue(
        TState Value,
        PlayContextId PlayContextId,
        AdapterInstanceId AdapterInstanceId,
        long ConnectionGeneration);

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

// TODO(stage4-file-extraction): Move StateApplyResult to its own
// StateApplyResult.cs in the post-Stage-4 structural cleanup PR.
// Temporarily colocated here to hold this PR's changed-file count down;
// extraction only, no behavior change.
/// <summary>
/// The atomic outcome of one <see cref="IStatePublisher{TState}.Apply"/>,
/// <see cref="IStatePublisher{TState}.ApplyResynchronizationBaseline"/>, or
/// <see cref="IStatePublisher{TState}.ApplyEvent"/> call, computed inside the
/// same lock that decides acceptance and assigns the revision -- so a caller deciding whether to
/// push an unsolicited publication never needs to separately re-read <see cref="RevisionNumber"/>
/// after the fact, which could otherwise race a concurrent capture for the same area.
/// </summary>
/// <param name="Accepted">Whether the value was accepted from the current adapter and captured play context.</param>
/// <param name="Changed">
/// Whether the accepted value actually changed the state area's authoritative value, and therefore
/// advanced <see cref="Revision"/> past <see cref="BaseRevision"/>. Always <see langword="false"/>
/// when <see cref="Accepted"/> is <see langword="false"/>.
/// </param>
/// <param name="BaseRevision">The state area's revision immediately before this call, or <see cref="RevisionNumber.Initial"/> when rejected.</param>
/// <param name="Revision">The state area's revision immediately after this call: equal to <see cref="BaseRevision"/> unless <see cref="Changed"/> is <see langword="true"/>, or <see cref="RevisionNumber.Initial"/> when rejected.</param>
public readonly record struct StateApplyResult(bool Accepted, bool Changed, RevisionNumber BaseRevision, RevisionNumber Revision)
{
    /// <summary>The result for a call rejected before any revision could be read.</summary>
    public static StateApplyResult Rejected { get; } = new(Accepted: false, Changed: false, RevisionNumber.Initial, RevisionNumber.Initial);
}
