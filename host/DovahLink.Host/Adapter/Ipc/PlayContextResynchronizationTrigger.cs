using System.Net;
using System.Net.Sockets;
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
    /// a fresh baseline on the currently active adapter connection, if any. A no-op when
    /// <see cref="PlayContextTransition.NewPlayContextId"/> is <see langword="null"/>: no play
    /// context exists to resynchronize. Otherwise unconditional: every real transition (already
    /// deduplicated for a repeated context by <see cref="IPlayContextTracker.NotifyTransition"/>
    /// itself) re-arms and re-requests, superseding whatever transaction the previous request may
    /// still be in flight for. When an adapter is currently connected, this also immediately
    /// supersedes whatever transaction <see cref="IResynchronizationTransactionCoordinator"/> is still
    /// tracking for the previous play context, via <see cref="IResynchronizationTransactionCoordinator.BeginTransaction"/>
    /// -- so that transaction's own watchdog can never expire and recover the connection this new
    /// transition now owns, even before its first baseline or result ever arrives. An essential
    /// resynchronize request must never silently disappear: when the send itself fails (for example a
    /// full outbound queue), this forces the connection closed instead of leaving the re-armed
    /// requirement with no request ever having gone out -- the adapter's normal reconnect then drives
    /// a fresh initial resynchronization.
    /// </summary>
    /// <param name="transition">The transition that just committed.</param>
    void HandleTransition(PlayContextTransition transition);
}

/// <inheritdoc cref="IPlayContextResynchronizationTrigger"/>
public sealed class PlayContextResynchronizationTrigger : IPlayContextResynchronizationTrigger
{
    /// <summary>The tracker this trigger reads the committed transition's own generation from.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The tracker this trigger re-arms for every play-context transition.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>The listener whose currently active connection this trigger sends the fresh request through.</summary>
    private readonly IAdapterIpcListener listener;

    /// <summary>The coordinator this trigger immediately supersedes the previous transaction on.</summary>
    private readonly IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator;

    /// <summary>Creates a trigger subscribed to <paramref name="playContextTracker"/> for the host process's own lifetime.</summary>
    /// <param name="playContextTracker">The tracker this trigger subscribes to and reads each transition's own generation from.</param>
    /// <param name="adapterAvailabilityTracker">The tracker this trigger re-arms for every play-context transition.</param>
    /// <param name="listener">The listener whose currently active connection this trigger sends the fresh request through.</param>
    /// <param name="resynchronizationTransactionCoordinator">The coordinator this trigger immediately supersedes the previous transaction on.</param>
    public PlayContextResynchronizationTrigger(
        IPlayContextTracker playContextTracker,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IAdapterIpcListener listener,
        IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator)
    {
        this.playContextTracker = playContextTracker;
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.listener = listener;
        this.resynchronizationTransactionCoordinator = resynchronizationTransactionCoordinator;

        playContextTracker.Transitioned += HandleTransition;
    }

    /// <inheritdoc/>
    public void HandleTransition(PlayContextTransition transition)
    {
        if (transition.NewPlayContextId is null)
        {
            //  No play context exists to resynchronize; a baseline is requested only once a later
            //  transition establishes a real one.
            return;
        }

        adapterAvailabilityTracker.RearmResynchronizationForPlayContextTransition();

        AdapterAvailabilitySnapshot availability = adapterAvailabilityTracker.GetSnapshot();
        if (availability.Current == AdapterAvailability.Available && availability.CurrentInstanceId is not null)
        {
            resynchronizationTransactionCoordinator.BeginTransaction(
                availability.CurrentInstanceId.Value, availability.ConnectionGeneration,
                transition.NewPlayContextId.Value, playContextTracker.GetSnapshot().TransitionGeneration);
        }

        IAdapterIpcConnection? connection = listener.CurrentConnection;
        if (connection is not null && !connection.TrySendResynchronizeRequest())
        {
            connection.RequestClose();
        }
    }
}
