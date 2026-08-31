using DovahLink.Host.Adapter.Ipc;
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

    /// <summary>
    /// Commits an adapter instance as connected at the given generation, under the same lock
    /// <see cref="Current"/> and the other getters read, and returns the resulting transition
    /// without publishing it -- publishing is a separate step via <see cref="PublishTransition"/>,
    /// deliberately outside this method so a caller can release its own lease-eligibility lock
    /// before running arbitrary subscriber code. The generation is supplied by the caller rather
    /// than allocated here: <see cref="IAdapterConnectionLifecycle"/> is the sole intended caller
    /// and the sole authority for connection-generation numbering, so this method trusts the value
    /// it is given rather than re-deriving or validating it.
    /// </summary>
    /// <param name="instanceId">The connecting adapter's instance identity.</param>
    /// <param name="generation">The connection generation to commit as current.</param>
    /// <returns>The committed transition, or <see langword="null"/> when availability did not actually change.</returns>
    AdapterAvailabilityTransition? CommitConnected(AdapterInstanceId instanceId, long generation);

    /// <summary>
    /// Records that the specified connected adapter has disconnected and returns the resulting
    /// transition without publishing it; see <see cref="CommitConnected"/> for why commit and
    /// publish are separate steps.
    /// </summary>
    /// <param name="instanceId">The adapter instance whose connection ended.</param>
    /// <param name="connectionGeneration">The connection generation that ended.</param>
    /// <returns>The committed transition, or <see langword="null"/> when <paramref name="instanceId"/>/<paramref name="connectionGeneration"/> no longer match the current connection, or availability did not actually change.</returns>
    AdapterAvailabilityTransition? CommitDisconnected(AdapterInstanceId instanceId, long connectionGeneration);

    /// <summary>
    /// Publishes an already-committed transition to every <see cref="AvailabilityChanged"/>
    /// subscriber, containing any subscriber exception individually so one failing subscriber can
    /// never suppress a later one. <see cref="IAdapterConnectionLifecycle"/> is the sole intended
    /// caller, and calls this only after releasing its own lease-eligibility lock, so subscriber
    /// code never runs while a concurrent <c>IsActive</c> caller is blocked on that lock.
    /// </summary>
    /// <param name="transition">The transition returned by <see cref="CommitConnected"/> or <see cref="CommitDisconnected"/>.</param>
    void PublishTransition(AdapterAvailabilityTransition transition);

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
    public AdapterAvailabilityTransition? CommitConnected(AdapterInstanceId instanceId, long generation)
    {
        lock (gate)
        {
            currentConnectionGeneration = generation;
            AdapterAvailability previous = current;
            current = AdapterAvailability.Available;
            currentInstanceId = instanceId;
            needsResynchronization = true;
            currentResynchronizationToken = new AdapterResynchronizationToken();
            resynchronizationTokenClaimed = false;
            return previous != current
                ? new AdapterAvailabilityTransition(previous, current, currentInstanceId, currentConnectionGeneration)
                : null;
        }
    }

    /// <inheritdoc/>
    public AdapterAvailabilityTransition? CommitDisconnected(AdapterInstanceId instanceId, long connectionGeneration)
    {
        lock (gate)
        {
            if (currentInstanceId != instanceId || currentConnectionGeneration != connectionGeneration)
            {
                return null;
            }

            AdapterAvailability previous = current;
            current = AdapterAvailability.Unavailable;
            needsResynchronization = true;
            currentResynchronizationToken = null;
            resynchronizationTokenClaimed = false;
            return previous != current
                ? new AdapterAvailabilityTransition(previous, current, currentInstanceId, currentConnectionGeneration)
                : null;
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

    /// <inheritdoc/>
    public void PublishTransition(AdapterAvailabilityTransition transition)
    {
        Delegate[]? subscribers = AvailabilityChanged?.GetInvocationList();
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((Action<AdapterAvailabilityTransition>)subscriber).Invoke(transition);
            }
            catch (Exception)
            {
                // A subscriber's own failure is not this tracker's failure to propagate: the
                // transition above is already committed, and every other subscriber must still run.
            }
        }
    }
}
