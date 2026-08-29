using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IAdapterAvailabilityTracker"/> whose fields a test can set directly.</summary>
public sealed class FakeAdapterAvailabilityTracker : IAdapterAvailabilityTracker
{
    /// <inheritdoc/>
    public AdapterAvailability Current { get; set; } = AdapterAvailability.Unavailable;

    /// <inheritdoc/>
    public AdapterInstanceId? CurrentInstanceId { get; set; }

    /// <inheritdoc/>
    public bool NeedsResynchronization { get; set; }

    /// <inheritdoc/>
    public void NotifyConnected(AdapterInstanceId instanceId)
    {
        Current = AdapterAvailability.Available;
        CurrentInstanceId = instanceId;
        NeedsResynchronization = true;
    }

    /// <inheritdoc/>
    public void NotifyDisconnected()
    {
        Current = AdapterAvailability.Unavailable;
        NeedsResynchronization = true;
    }

    /// <inheritdoc/>
    public void NotifyResynchronized() => NeedsResynchronization = false;

    /// <inheritdoc/>
    public AdapterAvailabilitySnapshot GetSnapshot() => new(Current, CurrentInstanceId, NeedsResynchronization);
}
