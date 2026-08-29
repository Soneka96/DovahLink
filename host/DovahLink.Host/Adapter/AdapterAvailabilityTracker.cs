using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter;

/// <summary>
/// The host's view of the native adapter's connection identity and availability -- the host-owned
/// successor to the old single-process <c>bridgeInstanceId</c>, per
/// <c>ai/context/host/migration-audit.md</c>'s "Identity model". A restarted host starts
/// unavailable with no known adapter instance, and every connection or reconnection requires an
/// explicit resynchronization before adapter-sourced state can be published as current, per
/// <c>ai/context/host/architecture.md</c>'s "Host-to-adapter IPC contract".
/// </summary>
public interface IAdapterAvailabilityTracker
{
    /// <summary>Whether an adapter is currently connected.</summary>
    AdapterAvailability Current { get; }

    /// <summary>The most recently connected adapter's instance identity, or <see langword="null"/> if none has ever connected.</summary>
    AdapterInstanceId? CurrentInstanceId { get; }

    /// <summary>Whether a resynchronization handshake must complete before adapter-sourced state can be published as current.</summary>
    bool NeedsResynchronization { get; }

    /// <summary>Records that an adapter instance has connected.</summary>
    /// <param name="instanceId">The connecting adapter's instance identity.</param>
    void NotifyConnected(AdapterInstanceId instanceId);

    /// <summary>Records that the connected adapter has disconnected.</summary>
    void NotifyDisconnected();

    /// <summary>Records that the resynchronization handshake has completed.</summary>
    void NotifyResynchronized();

    /// <summary>
    /// Reads <see cref="Current"/>, <see cref="CurrentInstanceId"/>, and
    /// <see cref="NeedsResynchronization"/> together as one internally consistent snapshot. Use
    /// this instead of reading two or more of those properties separately when a decision needs a
    /// coherent combined view.
    /// </summary>
    AdapterAvailabilitySnapshot GetSnapshot();
}

/// <inheritdoc cref="IAdapterAvailabilityTracker"/>
public sealed class AdapterAvailabilityTracker : IAdapterAvailabilityTracker
{
    /// <summary>Guards every field below against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Whether an adapter is currently connected.</summary>
    private AdapterAvailability current = AdapterAvailability.Unavailable;

    /// <summary>The most recently connected adapter's instance identity, or <see langword="null"/> if none has ever connected.</summary>
    private AdapterInstanceId? currentInstanceId;

    /// <summary>Whether a resynchronization handshake must complete before adapter-sourced state can be published as current.</summary>
    private bool needsResynchronization;

    /// <inheritdoc/>
    public AdapterAvailability Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    /// <inheritdoc/>
    public AdapterInstanceId? CurrentInstanceId
    {
        get
        {
            lock (gate)
            {
                return currentInstanceId;
            }
        }
    }

    /// <inheritdoc/>
    public bool NeedsResynchronization
    {
        get
        {
            lock (gate)
            {
                return needsResynchronization;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The prior instance identity is not cleared -- it remains the last known adapter identity
    /// until a real reconnection replaces it here, matching Current separately reporting
    /// unavailability in the meantime.
    /// </remarks>
    public void NotifyConnected(AdapterInstanceId instanceId)
    {
        lock (gate)
        {
            current = AdapterAvailability.Available;
            currentInstanceId = instanceId;
            needsResynchronization = true;
        }
    }

    /// <inheritdoc/>
    public void NotifyDisconnected()
    {
        lock (gate)
        {
            current = AdapterAvailability.Unavailable;
            needsResynchronization = true;
        }
    }

    /// <inheritdoc/>
    public void NotifyResynchronized()
    {
        lock (gate)
        {
            needsResynchronization = false;
        }
    }

    /// <inheritdoc/>
    public AdapterAvailabilitySnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new AdapterAvailabilitySnapshot(current, currentInstanceId, needsResynchronization);
        }
    }
}
