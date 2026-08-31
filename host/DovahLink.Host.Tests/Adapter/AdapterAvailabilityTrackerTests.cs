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
    public void PublishConnected_ReportsAvailableAndNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        tracker.PublishConnected(instanceId, 1);

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(instanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that the tracker commits exactly the generation it is given rather than deriving one
    /// of its own -- connection-generation numbering belongs solely to
    /// <see cref="Ipc.IAdapterConnectionLifecycle"/>, the tracker's sole intended caller.
    /// </summary>
    [Fact]
    public void PublishConnected_CommitsSuppliedGeneration()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        tracker.PublishConnected(instanceId, 5);
        Assert.Equal(5, tracker.CurrentConnectionGeneration);

        tracker.PublishConnected(instanceId, 9);
        Assert.Equal(9, tracker.CurrentConnectionGeneration);
    }

    /// <summary>Verifies that NotifyResynchronized clears the resynchronization requirement.</summary>
    [Fact]
    public void NotifyResynchronized_ClearsNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);

        tracker.NotifyResynchronized(instanceId, 1);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting reports unavailable and requires resynchronization on the next connection.</summary>
    [Fact]
    public void PublishDisconnected_ReportsUnavailableAndNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);
        tracker.NotifyResynchronized(instanceId, 1);

        tracker.PublishDisconnected(instanceId, 1);

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting does not erase the last known adapter instance identity.</summary>
    [Fact]
    public void PublishDisconnected_RetainsLastKnownInstanceId()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);

        tracker.PublishDisconnected(instanceId, 1);

        Assert.Equal(instanceId, tracker.CurrentInstanceId);
    }

    /// <summary>Verifies that a reconnection with a new instance identity replaces the previous one.</summary>
    [Fact]
    public void PublishConnected_AfterDisconnect_ReplacesInstanceIdAndRequiresResyncAgain()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId oldInstanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(oldInstanceId, 1);
        tracker.NotifyResynchronized(oldInstanceId, 1);
        tracker.PublishDisconnected(oldInstanceId, 1);
        AdapterInstanceId newInstanceId = AdapterInstanceId.NewId();

        tracker.PublishConnected(newInstanceId, 2);

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(newInstanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that adapter availability state does not survive a host restart: a fresh tracker starts unavailable with no instance, even after a prior tracker connected.</summary>
    [Fact]
    public void NewTracker_AfterPriorTrackerConnected_StartsUnavailableWithNoInstance()
    {
        var priorTracker = new AdapterAvailabilityTracker();
        priorTracker.PublishConnected(AdapterInstanceId.NewId(), 1);

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
    public void PublishConnected_WhileAlreadyConnected_ReplacesInstanceAndRequiresResync()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(firstInstanceId, 1);
        tracker.NotifyResynchronized(firstInstanceId, 1);
        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();

        tracker.PublishConnected(secondInstanceId, 2);

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(secondInstanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting twice in a row is a harmless no-op.</summary>
    [Fact]
    public void PublishDisconnected_CalledTwice_StaysUnavailable()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);

        tracker.PublishDisconnected(instanceId, 1);
        tracker.PublishDisconnected(instanceId, 1);

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
    public void PublishDisconnected_BeforeEverResynchronizing_StillNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);

        tracker.PublishDisconnected(instanceId, 1);

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that GetSnapshot reports the same combined state as the individual properties after connecting.</summary>
    [Fact]
    public void GetSnapshot_AfterConnecting_MatchesIndividualProperties()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);

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
        tracker.PublishConnected(oldInstanceId, 1);
        tracker.PublishConnected(currentInstanceId, 2);

        tracker.PublishDisconnected(oldInstanceId, 1);
        tracker.NotifyResynchronized(oldInstanceId, 1);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(currentInstanceId, snapshot.CurrentInstanceId);
        Assert.Equal(2, snapshot.ConnectionGeneration);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>Verifies that old notifications for the same adapter instance cannot affect a new channel generation.</summary>
    [Fact]
    public void SameInstanceStaleNotifications_DoNotChangeCurrentConnection()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);
        tracker.PublishConnected(instanceId, 2);

        tracker.PublishDisconnected(instanceId, 1);
        tracker.NotifyResynchronized(instanceId, 1);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(2, snapshot.ConnectionGeneration);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>Verifies that a late resynchronization from a disconnected connection is ignored.</summary>
    [Fact]
    public void NotifyResynchronized_AfterDisconnectWithSameGeneration_IsIgnored()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);
        tracker.PublishDisconnected(instanceId, 1);

        tracker.NotifyResynchronized(instanceId, 1);

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
        tracker.PublishConnected(oldInstanceId, 1);
        tracker.PublishConnected(currentInstanceId, 2);

        Task[] staleNotifications = Enumerable.Range(0, 32)
            .Select(index => Task.Run(() =>
            {
                tracker.PublishDisconnected(oldInstanceId, 1);
                tracker.NotifyResynchronized(oldInstanceId, 1);
            }))
            .ToArray();

        await Task.WhenAll(staleNotifications);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(currentInstanceId, snapshot.CurrentInstanceId);
        Assert.Equal(2, snapshot.ConnectionGeneration);
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
        Task firstConnection = Task.Run(() => tracker.PublishConnected(firstInstanceId, 1));
        Assert.True(firstCallbackEntered.Wait(TimeSpan.FromSeconds(5)));
        using var secondConnectionStarted = new ManualResetEventSlim();
        Task secondConnection = Task.Run(() =>
        {
            secondConnectionStarted.Set();
            tracker.PublishConnected(secondInstanceId, 2);
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

    /// <summary>Verifies that one throwing subscriber does not suppress a later subscriber, and that the transition it observes is still fully committed.</summary>
    [Fact]
    public void PublishConnected_OneSubscriberThrows_LaterSubscriberStillRunsAndStateIsCommitted()
    {
        var tracker = new AdapterAvailabilityTracker();
        int laterSubscriberCallCount = 0;
        tracker.AvailabilityChanged += _ => throw new InvalidOperationException("Simulated subscriber failure.");
        tracker.AvailabilityChanged += transition =>
        {
            laterSubscriberCallCount++;
            Assert.Equal(AdapterAvailability.Available, transition.Current);
        };
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        tracker.PublishConnected(instanceId, 1);

        Assert.Equal(1, laterSubscriberCallCount);
        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(instanceId, tracker.CurrentInstanceId);
        Assert.Equal(1, tracker.CurrentConnectionGeneration);
    }

    /// <summary>Verifies that one throwing subscriber does not suppress a later subscriber, and that the disconnect transition it observes is still fully committed.</summary>
    [Fact]
    public void PublishDisconnected_OneSubscriberThrows_LaterSubscriberStillRunsAndStateIsCommitted()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);
        int laterSubscriberCallCount = 0;
        tracker.AvailabilityChanged += _ => throw new InvalidOperationException("Simulated subscriber failure.");
        tracker.AvailabilityChanged += transition =>
        {
            laterSubscriberCallCount++;
            Assert.Equal(AdapterAvailability.Unavailable, transition.Current);
        };

        tracker.PublishDisconnected(instanceId, 1);

        Assert.Equal(1, laterSubscriberCallCount);
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that concurrent claimers receive only one resynchronization authorization.</summary>
    [Fact]
    public async Task TryClaimResynchronizationToken_ConcurrentClaims_AreExclusive()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(instanceId, 1);

        IAdapterResynchronizationToken?[] claims = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => Task.Run(tracker.TryClaimResynchronizationToken)));

        IAdapterResynchronizationToken token = Assert.Single(claims, claim => claim is not null)!;
        Assert.Null(tracker.TryClaimResynchronizationToken());
        Assert.True(tracker.IsCurrentResynchronizationToken(token));

        tracker.NotifyResynchronized(instanceId, 1);

        Assert.False(tracker.IsCurrentResynchronizationToken(token));
        Assert.Null(tracker.TryClaimResynchronizationToken());
    }

    /// <summary>Verifies that mixed concurrent connection notifications preserve one coherent current connection.</summary>
    [Fact]
    public async Task MixedConcurrentConnectionNotifications_PreserveCoherentState()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId initialInstanceId = AdapterInstanceId.NewId();
        tracker.PublishConnected(initialInstanceId, 1);
        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        AdapterInstanceId thirdInstanceId = AdapterInstanceId.NewId();

        Task[] operations =
        [
            Task.Run(() => tracker.PublishConnected(secondInstanceId, 2)),
            Task.Run(() => tracker.PublishDisconnected(initialInstanceId, 1)),
            Task.Run(() => tracker.NotifyResynchronized(initialInstanceId, 1)),
            Task.Run(() => tracker.PublishConnected(thirdInstanceId, 3)),
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
