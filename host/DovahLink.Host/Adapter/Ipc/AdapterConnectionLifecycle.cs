using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The sole owner of the logical Host-side Adapter connection-generation lifecycle: allocates each
/// connection's generation, activates and deactivates the exact <see cref="AdapterConnectionLease"/>
/// that identifies it, and coordinates that activation with <see cref="IAdapterAvailabilityTracker"/>
/// so no caller can ever observe the tracker and the lease disagreeing about which connection is
/// current. <see cref="AdapterIpcSession"/> is the only intended consumer; every connection-lifecycle
/// mutation is meant to flow through this type, never directly through the tracker.
/// </summary>
/// <remarks>
/// <see cref="Activate"/> and <see cref="Deactivate"/> hold their lease-eligibility lock across
/// their entire tracker publication -- lease mutation through subscriber notification -- so
/// <see cref="IsActive"/> can never observe a lease as eligible, or ineligible, while the tracker's
/// committed availability still disagrees: any concurrent reader sees the whole transition or none
/// of it, never a seam in between. A subscriber to the tracker's availability event must never call
/// back into <see cref="Activate"/>, <see cref="Deactivate"/>, or
/// <see cref="TryCompleteResynchronization"/> from inside its own handler: C# locks are reentrant
/// per thread, so such a call would not be blocked -- it would instead re-enter this type's
/// serialization mid-transition, which this type does not defend against. Subscribers are meant to
/// observe, not to drive further lifecycle mutation.
/// </remarks>
public interface IAdapterConnectionLifecycle
{
    /// <summary>Creates a fresh, inactive lease for one connection attempt. Never reused across a reconnect.</summary>
    AdapterConnectionLease CreateLease();

    /// <summary>
    /// Allocates a new connection generation, activates <paramref name="lease"/> for it, and only
    /// then commits and publishes the tracker's available transition -- so a subscriber reacting to
    /// that transition synchronously already sees <paramref name="lease"/> as active.
    /// </summary>
    /// <param name="lease">
    /// The lease to activate. Must not already be active; callers own preventing a duplicate
    /// activation of the same lease (<see cref="AdapterIpcSession"/> does so with its own
    /// handshake-committed guard).
    /// </param>
    /// <param name="instanceId">The connecting adapter's instance identity.</param>
    void Activate(AdapterConnectionLease lease, AdapterInstanceId instanceId);

    /// <summary>
    /// Deactivates <paramref name="lease"/> if it is still the active connection, then commits and
    /// publishes the tracker's unavailable transition -- so a subscriber reacting to that transition
    /// synchronously already sees <paramref name="lease"/> as inactive. A no-op if
    /// <paramref name="lease"/> has already been superseded or deactivated.
    /// </summary>
    /// <param name="lease">The lease to deactivate.</param>
    void Deactivate(AdapterConnectionLease lease);

    /// <summary>Whether <paramref name="lease"/> is currently this lifecycle's active connection.</summary>
    /// <param name="lease">The lease to check.</param>
    bool IsActive(AdapterConnectionLease lease);

    /// <summary>
    /// Completes resynchronization for <paramref name="lease"/> if it is still active, atomically
    /// with respect to a concurrent <see cref="Deactivate"/>.
    /// </summary>
    /// <param name="lease">The lease whose resynchronization to complete.</param>
    /// <returns><see langword="true"/> if the lease was still active and resynchronization was completed; otherwise <see langword="false"/>.</returns>
    bool TryCompleteResynchronization(AdapterConnectionLease lease);
}

/// <inheritdoc cref="IAdapterConnectionLifecycle"/>
public sealed class AdapterConnectionLifecycle : IAdapterConnectionLifecycle
{
    /// <summary>The tracker this lifecycle commits and publishes availability transitions through.</summary>
    private readonly IAdapterAvailabilityTracker tracker;

    /// <summary>
    /// Serializes one complete Activate/Deactivate/TryCompleteResynchronization operation, including
    /// its tracker publication, so one logical transition can never interleave with another and a
    /// generation can never be committed out of allocation order.
    /// </summary>
    private readonly object transitionGate = new();

    /// <summary>
    /// Guards <see cref="currentLease"/> for the fast read <see cref="IsActive"/> needs, and, in
    /// <see cref="Activate"/>/<see cref="Deactivate"/>, is held across those methods' entire tracker
    /// publication -- including subscriber notification -- so a lease's eligibility and the
    /// tracker's committed availability always commit as one atomic transition: <see
    /// cref="IsActive"/> blocks until an in-flight transition fully lands rather than observing it
    /// mid-flight. Deliberately a separate lock from <see cref="transitionGate"/> so
    /// <see cref="TryCompleteResynchronization"/>, which does not change lease eligibility, does not
    /// also block <see cref="IsActive"/> for the duration of its own tracker call.
    /// </summary>
    private readonly object stateGate = new();

    /// <summary>The most recently allocated connection generation.</summary>
    private long nextGeneration;

    /// <summary>The lease currently active, or <see langword="null"/> when no connection is active.</summary>
    private AdapterConnectionLease? currentLease;

    /// <summary>Creates a lifecycle over the tracker it commits and publishes availability transitions through.</summary>
    /// <param name="tracker">The tracker this lifecycle commits and publishes availability transitions through.</param>
    public AdapterConnectionLifecycle(IAdapterAvailabilityTracker tracker)
    {
        this.tracker = tracker;
    }

    /// <inheritdoc/>
    public AdapterConnectionLease CreateLease() => new();

    /// <inheritdoc/>
    public void Activate(AdapterConnectionLease lease, AdapterInstanceId instanceId)
    {
        lock (transitionGate)
        {
            long generation = ++nextGeneration;
            lock (stateGate)
            {
                lease.InstanceId = instanceId;
                lease.Generation = generation;
                currentLease = lease;

                tracker.PublishConnected(instanceId, generation);
            }
        }
    }

    /// <inheritdoc/>
    public void Deactivate(AdapterConnectionLease lease)
    {
        lock (transitionGate)
        {
            lock (stateGate)
            {
                if (!ReferenceEquals(currentLease, lease))
                {
                    return;
                }

                currentLease = null;
                tracker.PublishDisconnected(lease.InstanceId, lease.Generation);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsActive(AdapterConnectionLease lease)
    {
        lock (stateGate)
        {
            return ReferenceEquals(currentLease, lease);
        }
    }

    /// <inheritdoc/>
    public bool TryCompleteResynchronization(AdapterConnectionLease lease)
    {
        lock (transitionGate)
        {
            AdapterInstanceId instanceId;
            long generation;
            lock (stateGate)
            {
                if (!ReferenceEquals(currentLease, lease))
                {
                    return false;
                }

                instanceId = lease.InstanceId;
                generation = lease.Generation;
            }

            tracker.NotifyResynchronized(instanceId, generation);
            return true;
        }
    }
}
