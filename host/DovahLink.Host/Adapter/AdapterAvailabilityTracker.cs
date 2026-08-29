using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter;

/// <summary>
/// The host's view of the native adapter's connection identity and availability -- the host-owned
/// successor to the old single-process <c>bridgeInstanceId</c>. A restarted host starts
/// unavailable with no known adapter instance, and every connection or reconnection requires an
/// explicit resynchronization before adapter-sourced state can be published as current.
/// </summary>
public interface IAdapterAvailabilityTracker
{
    /// <summary>Whether an adapter is currently connected.</summary>
    AdapterAvailability Current { get; }

    /// <summary>The most recently connected adapter's instance identity, or <see langword="null"/> if none has ever connected.</summary>
    AdapterInstanceId? CurrentInstanceId { get; }

    /// <summary>Whether a resynchronization handshake must complete before adapter-sourced state can be published as current.</summary>
    bool NeedsResynchronization { get; }

    /// <summary>The monotonically increasing generation of the current adapter connection.</summary>
    long CurrentConnectionGeneration { get; }

    /// <summary>Raised after an availability transition is committed.</summary>
    event Action<AdapterAvailabilityTransition>? AvailabilityChanged;

    /// <summary>Claims the current connection's one-time resynchronization authorization.</summary>
    IAdapterResynchronizationToken? TryClaimResynchronizationToken();

    /// <summary>Checks whether a claimed resynchronization authorization belongs to the active connection.</summary>
    bool IsCurrentResynchronizationToken(IAdapterResynchronizationToken token);

    /// <summary>Records that an adapter instance has connected.</summary>
    /// <param name="instanceId">The connecting adapter's instance identity.</param>
    long NotifyConnected(AdapterInstanceId instanceId);

    /// <summary>Records that the specified connected adapter has disconnected.</summary>
    /// <param name="instanceId">The adapter instance whose connection ended.</param>
    /// <param name="connectionGeneration">The connection generation that ended.</param>
    void NotifyDisconnected(AdapterInstanceId instanceId, long connectionGeneration);

    /// <summary>Records that the specified adapter's resynchronization handshake has completed.</summary>
    /// <param name="instanceId">The adapter instance that completed resynchronization.</param>
    /// <param name="connectionGeneration">The connection generation that resynchronized.</param>
    void NotifyResynchronized(AdapterInstanceId instanceId, long connectionGeneration);

    /// <summary>
    /// Reads all availability, identity, and generation fields together as one
    /// internally consistent snapshot. Use this instead of reading separate properties when a
    /// decision needs a coherent combined view.
    /// </summary>
    AdapterAvailabilitySnapshot GetSnapshot();
}

/// <inheritdoc cref="IAdapterAvailabilityTracker"/>
public sealed class AdapterAvailabilityTracker : IAdapterAvailabilityTracker
{
    /// <summary>Guards every field below against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Serializes availability updates with ordered transition callback publication.</summary>
    private readonly object publicationGate = new();

    /// <summary>Whether an adapter is currently connected.</summary>
    private AdapterAvailability current = AdapterAvailability.Unavailable;

    /// <summary>The most recently connected adapter's instance identity, or <see langword="null"/> if none has ever connected.</summary>
    private AdapterInstanceId? currentInstanceId;

    /// <summary>Whether a resynchronization handshake must complete before adapter-sourced state can be published as current.</summary>
    private bool needsResynchronization;

    /// <summary>The generation assigned to the most recent adapter connection.</summary>
    private long currentConnectionGeneration;

    /// <summary>The opaque baseline authorization for the current adapter connection, if still pending.</summary>
    private IAdapterResynchronizationToken? currentResynchronizationToken;

    /// <summary>Whether the current connection's baseline authorization has been claimed.</summary>
    private bool resynchronizationTokenClaimed;

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
    public long CurrentConnectionGeneration
    {
        get
        {
            lock (gate)
            {
                return currentConnectionGeneration;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The prior instance identity is not cleared -- it remains the last known adapter identity
    /// until a real reconnection replaces it here, matching Current separately reporting
    /// unavailability in the meantime.
    /// </remarks>
    public long NotifyConnected(AdapterInstanceId instanceId)
    {
        lock (publicationGate)
        {
            AdapterAvailabilityTransition? transition = null;
            long generation;
            lock (gate)
            {
                currentConnectionGeneration++;
                generation = currentConnectionGeneration;
                AdapterAvailability previous = current;
                current = AdapterAvailability.Available;
                currentInstanceId = instanceId;
                needsResynchronization = true;
                currentResynchronizationToken = new AdapterResynchronizationToken();
                resynchronizationTokenClaimed = false;
                if (previous != current)
                {
                    transition = new AdapterAvailabilityTransition(
                        previous, current, currentInstanceId, currentConnectionGeneration);
                }
            }

            if (transition is not null)
            {
                AvailabilityChanged?.Invoke(transition);
            }
            return generation;
        }
    }

    /// <inheritdoc/>
    public void NotifyDisconnected(AdapterInstanceId instanceId, long connectionGeneration)
    {
        lock (publicationGate)
        {
            AdapterAvailabilityTransition? transition = null;
            lock (gate)
            {
                if (currentInstanceId != instanceId || currentConnectionGeneration != connectionGeneration)
                {
                    return;
                }

                AdapterAvailability previous = current;
                current = AdapterAvailability.Unavailable;
                needsResynchronization = true;
                currentResynchronizationToken = null;
                resynchronizationTokenClaimed = false;
                if (previous != current)
                {
                    transition = new AdapterAvailabilityTransition(
                        previous, current, currentInstanceId, currentConnectionGeneration);
                }
            }

            if (transition is not null)
            {
                AvailabilityChanged?.Invoke(transition);
            }
        }
    }

    /// <inheritdoc/>
    public IAdapterResynchronizationToken? TryClaimResynchronizationToken()
    {
        lock (gate)
        {
            if (current != AdapterAvailability.Available || !needsResynchronization || resynchronizationTokenClaimed)
            {
                return null;
            }

            resynchronizationTokenClaimed = true;
            return currentResynchronizationToken;
        }
    }

    /// <inheritdoc/>
    public bool IsCurrentResynchronizationToken(IAdapterResynchronizationToken token)
    {
        lock (gate)
        {
            return current == AdapterAvailability.Available &&
                needsResynchronization &&
                resynchronizationTokenClaimed &&
                ReferenceEquals(currentResynchronizationToken, token);
        }
    }

    /// <inheritdoc/>
    public void NotifyResynchronized(AdapterInstanceId instanceId, long connectionGeneration)
    {
        lock (gate)
        {
            if (current != AdapterAvailability.Available || currentInstanceId != instanceId || currentConnectionGeneration != connectionGeneration)
            {
                return;
            }

            needsResynchronization = false;
            currentResynchronizationToken = null;
            resynchronizationTokenClaimed = false;
        }
    }

    /// <inheritdoc/>
    public AdapterAvailabilitySnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new AdapterAvailabilitySnapshot(current, currentInstanceId, needsResynchronization, currentConnectionGeneration);
        }
    }

    /// <inheritdoc/>
    public event Action<AdapterAvailabilityTransition>? AvailabilityChanged;
}
