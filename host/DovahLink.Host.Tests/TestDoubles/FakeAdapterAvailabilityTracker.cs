using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IAdapterAvailabilityTracker"/> whose fields a test can set directly.</summary>
public sealed class FakeAdapterAvailabilityTracker : IAdapterAvailabilityTracker
{
    /// <inheritdoc/>
    public AdapterAvailability Current { get; set; } = AdapterAvailability.Unavailable;

    /// <inheritdoc/>
    public AdapterInstanceId? CurrentInstanceId { get; set; } = AdapterInstanceId.NewId();

    /// <inheritdoc/>
    public bool NeedsResynchronization { get; set; }

    /// <inheritdoc/>
    public long CurrentConnectionGeneration { get; set; }

    private IAdapterResynchronizationToken? currentResynchronizationToken = new FakeAdapterResynchronizationToken();

    private bool resynchronizationTokenClaimed;

    /// <inheritdoc/>
    public event Action<AdapterAvailabilityTransition>? AvailabilityChanged;

    /// <inheritdoc/>
    public void PublishConnected(AdapterInstanceId instanceId, long generation)
    {
        AdapterAvailability previous = Current;
        CurrentConnectionGeneration = generation;
        Current = AdapterAvailability.Available;
        CurrentInstanceId = instanceId;
        NeedsResynchronization = true;
        currentResynchronizationToken = new FakeAdapterResynchronizationToken();
        resynchronizationTokenClaimed = false;
        if (previous != Current)
        {
            AvailabilityChanged?.Invoke(new AdapterAvailabilityTransition(
                previous, Current, CurrentInstanceId, CurrentConnectionGeneration));
        }
    }

    /// <inheritdoc/>
    public void PublishDisconnected(AdapterInstanceId instanceId, long connectionGeneration)
    {
        if (CurrentInstanceId == instanceId && CurrentConnectionGeneration == connectionGeneration)
        {
            AdapterAvailability previous = Current;
            Current = AdapterAvailability.Unavailable;
            NeedsResynchronization = true;
            currentResynchronizationToken = null;
            resynchronizationTokenClaimed = false;
            if (previous != Current)
            {
                AvailabilityChanged?.Invoke(new AdapterAvailabilityTransition(
                    previous, Current, CurrentInstanceId, CurrentConnectionGeneration));
            }
        }
    }

    /// <inheritdoc/>
    public void NotifyResynchronized(AdapterInstanceId instanceId, long connectionGeneration)
    {
        if (Current == AdapterAvailability.Available && CurrentInstanceId == instanceId && CurrentConnectionGeneration == connectionGeneration)
        {
            NeedsResynchronization = false;
            currentResynchronizationToken = null;
            resynchronizationTokenClaimed = false;
        }
    }

    /// <inheritdoc/>
    public IAdapterResynchronizationToken? TryClaimResynchronizationToken()
    {
        if (Current != AdapterAvailability.Available || !NeedsResynchronization || resynchronizationTokenClaimed)
        {
            return null;
        }

        resynchronizationTokenClaimed = true;
        return currentResynchronizationToken;
    }

    /// <inheritdoc/>
    public bool IsCurrentResynchronizationToken(IAdapterResynchronizationToken token) =>
        Current == AdapterAvailability.Available &&
        NeedsResynchronization &&
        resynchronizationTokenClaimed &&
        ReferenceEquals(currentResynchronizationToken, token);

    /// <inheritdoc/>
    public AdapterAvailabilitySnapshot GetSnapshot() => new(Current, CurrentInstanceId, NeedsResynchronization, CurrentConnectionGeneration);

    private sealed class FakeAdapterResynchronizationToken : IAdapterResynchronizationToken
    {
    }
}
