using System.Diagnostics.CodeAnalysis;
using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.State;

/// <summary>
/// The host's single per-state-area ordering point: applies a captured value, detects whether it
/// actually changed, and assigns the resulting revision, per
/// <c>ai/context/host/migration-audit.md</c>'s "Single per-state-area ordering point". Generic
/// over the captured value's type because Stage 2 adds no concrete Skyrim domain; a later stage
/// supplies the first real <typeparamref name="TState"/>.
/// </summary>
/// <typeparam name="TState">The captured value type for one family of state areas.</typeparam>
public interface IStatePublisher<TState>
{
    /// <summary>
    /// Tries to read a state area's current value. Reports unavailable -- rather than a stale
    /// value -- when no play context is established or the adapter is unavailable or not yet
    /// resynchronized, per <c>ai/context/host/architecture.md</c>'s "Adapter loss".
    /// </summary>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="value">The area's current value, if available.</param>
    /// <returns><see langword="true"/> if a current value is available.</returns>
    bool TryGetCurrentValue(StateAreaId areaId, [MaybeNullWhen(false)] out TState value);

    /// <summary>Reads a state area's current revision within the current play context.</summary>
    /// <param name="areaId">The state area to read.</param>
    /// <returns>The area's current revision, or <see cref="RevisionNumber.Initial"/> if no play context is established yet.</returns>
    RevisionNumber CurrentRevision(StateAreaId areaId);

    /// <summary>Applies a newly captured value, advancing the area's revision only if it actually changed.</summary>
    /// <param name="areaId">The state area the value belongs to.</param>
    /// <param name="value">The newly captured value.</param>
    /// <exception cref="InvalidOperationException">No play context has been established yet.</exception>
    void Apply(StateAreaId areaId, TState value);
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
    private readonly Dictionary<StateAreaId, TState> valuesByArea = new();

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
    }

    /// <inheritdoc/>
    public bool TryGetCurrentValue(StateAreaId areaId, [MaybeNullWhen(false)] out TState value)
    {
        if (playContextTracker.Current is null)
        {
            value = default;
            return false;
        }

        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        if (adapterSnapshot.Current == AdapterAvailability.Unavailable || adapterSnapshot.NeedsResynchronization)
        {
            value = default;
            return false;
        }

        lock (gate)
        {
            return valuesByArea.TryGetValue(areaId, out value);
        }
    }

    /// <inheritdoc/>
    public RevisionNumber CurrentRevision(StateAreaId areaId)
    {
        PlayContextId? currentContext = playContextTracker.Current;
        return currentContext is null ? RevisionNumber.Initial : revisionTracker.Current(currentContext.Value, areaId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the current play context inside the same lock that a concurrent transition's
    /// <see cref="OnPlayContextTransitioned"/> clears <see cref="valuesByArea"/> under, per
    /// <c>ai/context/common.md</c>'s "Concurrent decisions require coherent snapshots" -- otherwise
    /// a capture read just before a transition could still be written into the dictionary after
    /// that transition's clear, misattributing it to the new context.
    /// </remarks>
    public void Apply(StateAreaId areaId, TState value)
    {
        lock (gate)
        {
            PlayContextId currentContext = playContextTracker.Current
                ?? throw new InvalidOperationException("Cannot apply captured state before a play context has been established.");

            bool changed = !valuesByArea.TryGetValue(areaId, out TState? existing)
                || !EqualityComparer<TState>.Default.Equals(existing, value);

            valuesByArea[areaId] = value;

            if (changed)
            {
                revisionTracker.AdvanceOnChange(currentContext, areaId);
            }
        }
    }

    /// <summary>
    /// Clears every stored value on a play-context transition -- a value captured under the
    /// previous context is never valid under the new one -- and prunes the previous context's
    /// revisions so they do not accumulate for the rest of the host process's lifetime.
    /// </summary>
    /// <param name="transition">The transition that just occurred.</param>
    private void OnPlayContextTransitioned(PlayContextTransition transition)
    {
        lock (gate)
        {
            valuesByArea.Clear();
        }

        if (transition.PreviousPlayContextId is { } previousContext)
        {
            revisionTracker.InvalidateContext(previousContext);
        }
    }
}
