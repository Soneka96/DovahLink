using DovahLink.Host;
using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Adapter;

/// <summary>Tests for <see cref="AdapterAvailabilityTracker"/>.</summary>
public class AdapterAvailabilityTrackerTests
{
    /// <summary>Verifies that a freshly constructed tracker starts unavailable with no known adapter instance -- the unavailable-adapter path a restarted host starts in.</summary>
    [Fact]
    public void NewTracker_StartsUnavailableWithNoInstance()
    {
        var tracker = new AdapterAvailabilityTracker();

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.Null(tracker.CurrentInstanceId);
    }

    /// <summary>Verifies that connecting reports available, records the instance, and requires resynchronization.</summary>
    [Fact]
    public void NotifyConnected_ReportsAvailableAndNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        tracker.NotifyConnected(instanceId);

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(instanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that NotifyResynchronized clears the resynchronization requirement.</summary>
    [Fact]
    public void NotifyResynchronized_ClearsNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        tracker.NotifyConnected(AdapterInstanceId.NewId());

        tracker.NotifyResynchronized();

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting reports unavailable and requires resynchronization on the next connection.</summary>
    [Fact]
    public void NotifyDisconnected_ReportsUnavailableAndNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        tracker.NotifyConnected(AdapterInstanceId.NewId());
        tracker.NotifyResynchronized();

        tracker.NotifyDisconnected();

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting does not erase the last known adapter instance identity.</summary>
    [Fact]
    public void NotifyDisconnected_RetainsLastKnownInstanceId()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.NotifyConnected(instanceId);

        tracker.NotifyDisconnected();

        Assert.Equal(instanceId, tracker.CurrentInstanceId);
    }

    /// <summary>Verifies that a reconnection with a new instance identity replaces the previous one.</summary>
    [Fact]
    public void NotifyConnected_AfterDisconnect_ReplacesInstanceIdAndRequiresResyncAgain()
    {
        var tracker = new AdapterAvailabilityTracker();
        tracker.NotifyConnected(AdapterInstanceId.NewId());
        tracker.NotifyResynchronized();
        tracker.NotifyDisconnected();
        AdapterInstanceId newInstanceId = AdapterInstanceId.NewId();

        tracker.NotifyConnected(newInstanceId);

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(newInstanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that adapter availability state does not survive a host restart: a fresh tracker starts unavailable with no instance, even after a prior tracker connected.</summary>
    [Fact]
    public void NewTracker_AfterPriorTrackerConnected_StartsUnavailableWithNoInstance()
    {
        var priorTracker = new AdapterAvailabilityTracker();
        priorTracker.NotifyConnected(AdapterInstanceId.NewId());

        var restartedTracker = new AdapterAvailabilityTracker();

        Assert.Equal(AdapterAvailability.Unavailable, restartedTracker.Current);
        Assert.Null(restartedTracker.CurrentInstanceId);
    }

    /// <summary>
    /// Verifies that a fresh tracker does not report needing resynchronization: with no adapter
    /// ever connected, there is no prior synchronization to recover, so the flag starts false
    /// rather than true.
    /// </summary>
    [Fact]
    public void NewTracker_DoesNotNeedResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that connecting again while already connected (a duplicate connect notification) still replaces the instance and requires resync.</summary>
    [Fact]
    public void NotifyConnected_WhileAlreadyConnected_ReplacesInstanceAndRequiresResync()
    {
        var tracker = new AdapterAvailabilityTracker();
        tracker.NotifyConnected(AdapterInstanceId.NewId());
        tracker.NotifyResynchronized();
        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();

        tracker.NotifyConnected(secondInstanceId);

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(secondInstanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting twice in a row is a harmless no-op.</summary>
    [Fact]
    public void NotifyDisconnected_CalledTwice_StaysUnavailable()
    {
        var tracker = new AdapterAvailabilityTracker();
        tracker.NotifyConnected(AdapterInstanceId.NewId());

        tracker.NotifyDisconnected();
        tracker.NotifyDisconnected();

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that resynchronizing on a fresh tracker, with no connection ever made, is a harmless no-op.</summary>
    [Fact]
    public void NotifyResynchronized_OnFreshTracker_DoesNotThrow()
    {
        var tracker = new AdapterAvailabilityTracker();

        tracker.NotifyResynchronized();

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting before ever resynchronizing leaves resynchronization still required, rather than resetting it.</summary>
    [Fact]
    public void NotifyDisconnected_BeforeEverResynchronizing_StillNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        tracker.NotifyConnected(AdapterInstanceId.NewId());

        tracker.NotifyDisconnected();

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that GetSnapshot reports the same combined state as the individual properties after connecting.</summary>
    [Fact]
    public void GetSnapshot_AfterConnecting_MatchesIndividualProperties()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.NotifyConnected(instanceId);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();

        Assert.Equal(tracker.Current, snapshot.Current);
        Assert.Equal(tracker.CurrentInstanceId, snapshot.CurrentInstanceId);
        Assert.Equal(tracker.NeedsResynchronization, snapshot.NeedsResynchronization);
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(instanceId, snapshot.CurrentInstanceId);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>Verifies that GetSnapshot on a fresh tracker reports unavailable, no instance, and no resynchronization needed.</summary>
    [Fact]
    public void GetSnapshot_OnFreshTracker_ReportsUnavailableWithNoResyncNeeded()
    {
        var tracker = new AdapterAvailabilityTracker();

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();

        Assert.Equal(AdapterAvailability.Unavailable, snapshot.Current);
        Assert.Null(snapshot.CurrentInstanceId);
        Assert.False(snapshot.NeedsResynchronization);
    }
}
