using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Requests a fresh baseline whenever the play context transitions while the adapter connection
/// stays up -- a save load with no intervening reconnect -- so <c>character_level</c> and every
/// other baseline-required area is not left stale until the player happens to level up or the
/// connection happens to drop. Subscribes to <see cref="IPlayContextTracker.Transitioned"/> for the
/// host process's own lifetime at construction, matching <see cref="State.StatePublisher{TState}"/>'s
/// identical subscription discipline for the same event; never unsubscribed.
/// </summary>
public interface IPlayContextResynchronizationTrigger
{
    /// <summary>
    /// Reacts to one committed play-context transition by re-arming resynchronization and requesting
    /// a fresh baseline on the currently active adapter connection, if any. Unconditional: every real
    /// transition (already deduplicated for a repeated context by
    /// <see cref="IPlayContextTracker.NotifyTransition"/> itself) re-arms and re-requests, superseding
    /// whatever transaction the previous request may still be in flight for.
    /// </summary>
    /// <param name="transition">The transition that just committed.</param>
    void HandleTransition(PlayContextTransition transition);
}

/// <inheritdoc cref="IPlayContextResynchronizationTrigger"/>
public sealed class PlayContextResynchronizationTrigger : IPlayContextResynchronizationTrigger
{
    /// <summary>The tracker this trigger re-arms for every play-context transition.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>The listener whose currently active connection this trigger sends the fresh request through.</summary>
    private readonly IAdapterIpcListener listener;

    /// <summary>Creates a trigger subscribed to <paramref name="playContextTracker"/> for the host process's own lifetime.</summary>
    /// <param name="playContextTracker">The tracker this trigger subscribes to.</param>
    /// <param name="adapterAvailabilityTracker">The tracker this trigger re-arms for every play-context transition.</param>
    /// <param name="listener">The listener whose currently active connection this trigger sends the fresh request through.</param>
    public PlayContextResynchronizationTrigger(
        IPlayContextTracker playContextTracker,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IAdapterIpcListener listener)
    {
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.listener = listener;

        playContextTracker.Transitioned += HandleTransition;
    }

    /// <inheritdoc/>
    public void HandleTransition(PlayContextTransition transition)
    {
        adapterAvailabilityTracker.RearmResynchronizationForPlayContextTransition();
        listener.CurrentConnection?.TrySendResynchronizeRequest();
    }
}
