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

    /// <summary>Verifies that each connected adapter channel receives a new monotonic generation.</summary>
    [Fact]
    public void NotifyConnected_IncrementsAndReturnsConnectionGeneration()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        long first = tracker.NotifyConnected(instanceId);
        long second = tracker.NotifyConnected(instanceId);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(2, tracker.CurrentConnectionGeneration);
    }

    /// <summary>Verifies that NotifyResynchronized clears the resynchronization requirement.</summary>
    [Fact]
    public void NotifyResynchronized_ClearsNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        long connectionGeneration = tracker.NotifyConnected(instanceId);

        tracker.NotifyResynchronized(instanceId, connectionGeneration);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting reports unavailable and requires resynchronization on the next connection.</summary>
    [Fact]
    public void NotifyDisconnected_ReportsUnavailableAndNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        long connectionGeneration = tracker.NotifyConnected(instanceId);
        tracker.NotifyResynchronized(instanceId, connectionGeneration);

        tracker.NotifyDisconnected(instanceId, connectionGeneration);

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting does not erase the last known adapter instance identity.</summary>
    [Fact]
    public void NotifyDisconnected_RetainsLastKnownInstanceId()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        long connectionGeneration = tracker.NotifyConnected(instanceId);

        tracker.NotifyDisconnected(instanceId, connectionGeneration);

        Assert.Equal(instanceId, tracker.CurrentInstanceId);
    }

    /// <summary>Verifies that a reconnection with a new instance identity replaces the previous one.</summary>
    [Fact]
    public void NotifyConnected_AfterDisconnect_ReplacesInstanceIdAndRequiresResyncAgain()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId oldInstanceId = AdapterInstanceId.NewId();
        long oldConnectionGeneration = tracker.NotifyConnected(oldInstanceId);
        tracker.NotifyResynchronized(oldInstanceId, oldConnectionGeneration);
        tracker.NotifyDisconnected(oldInstanceId, oldConnectionGeneration);
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
        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        long firstConnectionGeneration = tracker.NotifyConnected(firstInstanceId);
        tracker.NotifyResynchronized(firstInstanceId, firstConnectionGeneration);
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
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        long connectionGeneration = tracker.NotifyConnected(instanceId);

        tracker.NotifyDisconnected(instanceId, connectionGeneration);
        tracker.NotifyDisconnected(instanceId, connectionGeneration);

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that resynchronizing on a fresh tracker, with no connection ever made, is a harmless no-op.</summary>
    [Fact]
    public void NotifyResynchronized_OnFreshTracker_DoesNotThrow()
    {
        var tracker = new AdapterAvailabilityTracker();

        tracker.NotifyResynchronized(AdapterInstanceId.NewId(), 1);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting before ever resynchronizing leaves resynchronization still required, rather than resetting it.</summary>
    [Fact]
    public void NotifyDisconnected_BeforeEverResynchronizing_StillNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        long connectionGeneration = tracker.NotifyConnected(instanceId);

        tracker.NotifyDisconnected(instanceId, connectionGeneration);

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

    /// <summary>Verifies that stale notifications cannot change the current adapter generation.</summary>
    [Fact]
    public void StaleNotifications_DoNotChangeCurrentInstanceState()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId oldInstanceId = AdapterInstanceId.NewId();
        AdapterInstanceId currentInstanceId = AdapterInstanceId.NewId();
        long oldConnectionGeneration = tracker.NotifyConnected(oldInstanceId);
        long currentConnectionGeneration = tracker.NotifyConnected(currentInstanceId);

        tracker.NotifyDisconnected(oldInstanceId, oldConnectionGeneration);
        tracker.NotifyResynchronized(oldInstanceId, oldConnectionGeneration);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(currentInstanceId, snapshot.CurrentInstanceId);
        Assert.Equal(currentConnectionGeneration, snapshot.ConnectionGeneration);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>Verifies that old notifications for the same adapter instance cannot affect a new channel generation.</summary>
    [Fact]
    public void SameInstanceStaleNotifications_DoNotChangeCurrentConnection()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        long oldGeneration = tracker.NotifyConnected(instanceId);
        long currentGeneration = tracker.NotifyConnected(instanceId);

        tracker.NotifyDisconnected(instanceId, oldGeneration);
        tracker.NotifyResynchronized(instanceId, oldGeneration);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(currentGeneration, snapshot.ConnectionGeneration);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>Verifies that a late resynchronization from a disconnected connection is ignored.</summary>
    [Fact]
    public void NotifyResynchronized_AfterDisconnectWithSameGeneration_IsIgnored()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        long connectionGeneration = tracker.NotifyConnected(instanceId);
        tracker.NotifyDisconnected(instanceId, connectionGeneration);

        tracker.NotifyResynchronized(instanceId, connectionGeneration);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Unavailable, snapshot.Current);
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

    /// <summary>Verifies that concurrent stale notifications cannot corrupt the active connection state.</summary>
    [Fact]
    public async Task ConcurrentStaleNotifications_DoNotChangeCurrentConnection()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId oldInstanceId = AdapterInstanceId.NewId();
        AdapterInstanceId currentInstanceId = AdapterInstanceId.NewId();
        long oldGeneration = tracker.NotifyConnected(oldInstanceId);
        long currentGeneration = tracker.NotifyConnected(currentInstanceId);

        Task[] staleNotifications = Enumerable.Range(0, 32)
            .Select(index => Task.Run(() =>
            {
                tracker.NotifyDisconnected(oldInstanceId, oldGeneration);
                tracker.NotifyResynchronized(oldInstanceId, oldGeneration);
            }))
            .ToArray();

        await Task.WhenAll(staleNotifications);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(currentInstanceId, snapshot.CurrentInstanceId);
        Assert.Equal(currentGeneration, snapshot.ConnectionGeneration);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>Verifies that a delayed availability callback cannot be overtaken by a newer connection transition.</summary>
    [Fact]
    public async Task AvailabilityChanged_DelayedCallback_BlocksLaterTransitionPublication()
    {
        var tracker = new AdapterAvailabilityTracker();
        using var firstCallbackEntered = new ManualResetEventSlim();
        using var releaseFirstCallback = new ManualResetEventSlim();
        int callbackCount = 0;
        tracker.AvailabilityChanged += _ =>
        {
            if (Interlocked.Increment(ref callbackCount) == 1)
            {
                firstCallbackEntered.Set();
                releaseFirstCallback.Wait();
            }
        };

        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        Task firstConnection = Task.Run(() => tracker.NotifyConnected(firstInstanceId));
        Assert.True(firstCallbackEntered.Wait(TimeSpan.FromSeconds(5)));
        using var secondConnectionStarted = new ManualResetEventSlim();
        Task secondConnection = Task.Run(() =>
        {
            secondConnectionStarted.Set();
            tracker.NotifyConnected(secondInstanceId);
        });
        Assert.True(secondConnectionStarted.Wait(TimeSpan.FromSeconds(5)));

        Task completed = await Task.WhenAny(secondConnection, Task.Delay(100));
        Assert.NotSame(secondConnection, completed);
        releaseFirstCallback.Set();
        await Task.WhenAll(firstConnection, secondConnection);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(secondInstanceId, snapshot.CurrentInstanceId);
        Assert.Equal(2, snapshot.ConnectionGeneration);
        Assert.Equal(1, callbackCount);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>Verifies that concurrent claimers receive only one resynchronization authorization.</summary>
    [Fact]
    public async Task TryClaimResynchronizationToken_ConcurrentClaims_AreExclusive()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        long generation = tracker.NotifyConnected(instanceId);

        IAdapterResynchronizationToken?[] claims = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => Task.Run(tracker.TryClaimResynchronizationToken)));

        IAdapterResynchronizationToken token = Assert.Single(claims, claim => claim is not null)!;
        Assert.Null(tracker.TryClaimResynchronizationToken());
        Assert.True(tracker.IsCurrentResynchronizationToken(token));

        tracker.NotifyResynchronized(instanceId, generation);

        Assert.False(tracker.IsCurrentResynchronizationToken(token));
        Assert.Null(tracker.TryClaimResynchronizationToken());
    }

    /// <summary>Verifies that mixed concurrent connection notifications preserve one coherent current connection.</summary>
    [Fact]
    public async Task MixedConcurrentConnectionNotifications_PreserveCoherentState()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId initialInstanceId = AdapterInstanceId.NewId();
        long initialGeneration = tracker.NotifyConnected(initialInstanceId);
        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        AdapterInstanceId thirdInstanceId = AdapterInstanceId.NewId();

        Task[] operations =
        [
            Task.Run(() => tracker.NotifyConnected(secondInstanceId)),
            Task.Run(() => tracker.NotifyDisconnected(initialInstanceId, initialGeneration)),
            Task.Run(() => tracker.NotifyResynchronized(initialInstanceId, initialGeneration)),
            Task.Run(() => tracker.NotifyConnected(thirdInstanceId)),
        ];

        await Task.WhenAll(operations);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.NotNull(snapshot.CurrentInstanceId);
        Assert.Contains(snapshot.CurrentInstanceId.Value, new[] { secondInstanceId, thirdInstanceId });
        Assert.True(snapshot.ConnectionGeneration is 2 or 3);
        Assert.True(snapshot.NeedsResynchronization);
    }
}
