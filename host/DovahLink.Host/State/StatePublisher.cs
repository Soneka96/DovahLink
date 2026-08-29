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

    /// <summary>Applies a newly captured value from the current adapter, advancing the area's revision only if it actually changed.</summary>
    /// <param name="sourceInstanceId">The adapter instance that produced the value.</param>
    /// <param name="sourceConnectionGeneration">The adapter connection generation that produced the value.</param>
    /// <param name="areaId">The state area the value belongs to.</param>
    /// <param name="value">The newly captured value.</param>
    /// <returns><see langword="true"/> when the value was accepted from the current adapter.</returns>
    /// <exception cref="InvalidOperationException">No play context has been established yet.</exception>
    bool Apply(AdapterInstanceId sourceInstanceId, long sourceConnectionGeneration, StateAreaId areaId, TState value);

    /// <summary>Applies an explicitly identified resynchronization baseline while the adapter is gated.</summary>
    /// <param name="resynchronizationToken">The opaque authorization issued for the current adapter connection.</param>
    /// <param name="areaId">The state area the baseline belongs to.</param>
    /// <param name="value">The baseline value.</param>
    /// <returns><see langword="true"/> when the baseline was accepted from the current adapter connection.</returns>
    /// <exception cref="InvalidOperationException">No play context has been established yet.</exception>
    bool ApplyResynchronizationBaseline(IAdapterResynchronizationToken resynchronizationToken, StateAreaId areaId, TState value);
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
    public bool Apply(
        AdapterInstanceId sourceInstanceId,
        long sourceConnectionGeneration,
        StateAreaId areaId,
        TState value)
    {
        return ApplyCore(sourceInstanceId, sourceConnectionGeneration, null, areaId, value, allowResynchronization: false);
    }

    /// <inheritdoc/>
    public bool ApplyResynchronizationBaseline(
        IAdapterResynchronizationToken resynchronizationToken,
        StateAreaId areaId,
        TState value)
    {
        return ApplyCore(null, null, resynchronizationToken, areaId, value, allowResynchronization: true);
    }

    private bool ApplyCore(
        AdapterInstanceId? sourceInstanceId,
        long? sourceConnectionGeneration,
        IAdapterResynchronizationToken? resynchronizationToken,
        StateAreaId areaId,
        TState value,
        bool allowResynchronization)
    {
        lock (gate)
        {
            AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
            if (adapterSnapshot.Current != AdapterAvailability.Available ||
                adapterSnapshot.NeedsResynchronization != allowResynchronization ||
                (allowResynchronization
                    ? resynchronizationToken is null || !adapterAvailabilityTracker.IsCurrentResynchronizationToken(resynchronizationToken)
                    : adapterSnapshot.CurrentInstanceId != sourceInstanceId ||
                      adapterSnapshot.ConnectionGeneration != sourceConnectionGeneration))
            {
                return false;
            }

            PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
            PlayContextId currentContext = contextSnapshot.Current
                ?? throw new InvalidOperationException("Cannot apply captured state before a play context has been established.");

            if (playContextTracker.GetSnapshot().TransitionGeneration != contextSnapshot.TransitionGeneration)
            {
                return false;
            }

            bool changed = !valuesByArea.TryGetValue(areaId, out StoredValue? existing)
                || !EqualityComparer<TState>.Default.Equals(existing.Value, value)
                || existing.PlayContextId != currentContext;

            valuesByArea[areaId] = new StoredValue(
                value,
                currentContext,
                adapterSnapshot.CurrentInstanceId!.Value,
                adapterSnapshot.ConnectionGeneration);

            if (changed)
            {
                revisionTracker.AdvanceOnChange(currentContext, areaId);
            }

            return true;
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
    private sealed record StoredValue(
        TState Value,
        PlayContextId PlayContextId,
        AdapterInstanceId AdapterInstanceId,
        long ConnectionGeneration);
}
