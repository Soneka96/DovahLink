using DovahLink.Host;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

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

        PublishConnected(tracker, instanceId, 1);

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(instanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that the tracker commits exactly the generation it is given rather than deriving one
    /// of its own -- connection-generation numbering belongs solely to
    /// <see cref="IAdapterConnectionLifecycle"/>, the tracker's sole intended caller.
    /// </summary>
    [Fact]
    public void PublishConnected_CommitsSuppliedGeneration()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        PublishConnected(tracker, instanceId, 5);
        Assert.Equal(5, tracker.CurrentConnectionGeneration);

        PublishConnected(tracker, instanceId, 9);
        Assert.Equal(9, tracker.CurrentConnectionGeneration);
    }

    /// <summary>Verifies that NotifyResynchronized clears the resynchronization requirement when given the current claimed token.</summary>
    [Fact]
    public void NotifyResynchronized_ClearsNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        Resynchronize(tracker, instanceId, 1);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that ordinary queue admission runs only for the current, available, resynchronized connection generation.</summary>
    [Fact]
    public void TryExecuteWhileOrdinarySamplingAllowed_RequiresAvailableCurrentResynchronizedGeneration()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 4);
        Resynchronize(tracker, instanceId, 4);
        int admissions = 0;

        Assert.True(tracker.TryExecuteWhileOrdinarySamplingAllowed(4, () =>
        {
            admissions++;
            return true;
        }));
        Assert.False(tracker.TryExecuteWhileOrdinarySamplingAllowed(3, () =>
        {
            admissions++;
            return true;
        }));

        tracker.RearmResynchronizationForPlayContextTransition();
        Assert.False(tracker.TryExecuteWhileOrdinarySamplingAllowed(4, () =>
        {
            admissions++;
            return true;
        }));

        Assert.Equal(1, admissions);
    }

    /// <summary>Verifies that calling NotifyResynchronized a second time with the same, already-consumed token is a harmless no-op rather than a double-clear or a thrown exception.</summary>
    [Fact]
    public void NotifyResynchronized_CalledTwiceWithSameToken_SecondCallIsNoOp()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);
        IAdapterResynchronizationToken token = tracker.TryClaimResynchronizationToken()!;
        tracker.NotifyResynchronized(instanceId, 1, token);
        Assert.False(tracker.NeedsResynchronization);

        Exception? exception = Record.Exception(() => tracker.NotifyResynchronized(instanceId, 1, token));

        Assert.Null(exception);
        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that a resynchronization notification carrying a stale token -- one claimed for the
    /// same instance and connection generation, but superseded by a later play-context re-arm --
    /// cannot clear the newer requirement, even though the instance and generation both still match.
    /// This is the exact token check that stops an old, already-superseded transaction from
    /// completing a newer one; see <see cref="ResynchronizationTransactionCoordinatorTests"/> for the
    /// same race reproduced at the coordinator level.
    /// </summary>
    [Fact]
    public void NotifyResynchronized_StaleTokenSameInstanceAndGeneration_DoesNotClearResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);
        IAdapterResynchronizationToken staleToken = tracker.TryClaimResynchronizationToken()!;
        tracker.RearmResynchronizationForPlayContextTransition();

        tracker.NotifyResynchronized(instanceId, 1, staleToken);

        Assert.True(tracker.NeedsResynchronization);
        Assert.False(tracker.IsCurrentResynchronizationToken(staleToken));
    }

    /// <summary>Verifies that disconnecting reports unavailable and requires resynchronization on the next connection.</summary>
    [Fact]
    public void PublishDisconnected_ReportsUnavailableAndNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);
        Resynchronize(tracker, instanceId, 1);

        PublishDisconnected(tracker, instanceId, 1);

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting does not erase the last known adapter instance identity.</summary>
    [Fact]
    public void PublishDisconnected_RetainsLastKnownInstanceId()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        PublishDisconnected(tracker, instanceId, 1);

        Assert.Equal(instanceId, tracker.CurrentInstanceId);
    }

    /// <summary>Verifies that a reconnection with a new instance identity replaces the previous one.</summary>
    [Fact]
    public void PublishConnected_AfterDisconnect_ReplacesInstanceIdAndRequiresResyncAgain()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId oldInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, oldInstanceId, 1);
        Resynchronize(tracker, oldInstanceId, 1);
        PublishDisconnected(tracker, oldInstanceId, 1);
        AdapterInstanceId newInstanceId = AdapterInstanceId.NewId();

        PublishConnected(tracker, newInstanceId, 2);

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(newInstanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that adapter availability state does not survive a host restart: a fresh tracker starts unavailable with no instance, even after a prior tracker connected.</summary>
    [Fact]
    public void NewTracker_AfterPriorTrackerConnected_StartsUnavailableWithNoInstance()
    {
        var priorTracker = new AdapterAvailabilityTracker();
        PublishConnected(priorTracker, AdapterInstanceId.NewId(), 1);

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
        PublishConnected(tracker, firstInstanceId, 1);
        Resynchronize(tracker, firstInstanceId, 1);
        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();

        PublishConnected(tracker, secondInstanceId, 2);

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
        PublishConnected(tracker, instanceId, 1);

        PublishDisconnected(tracker, instanceId, 1);
        PublishDisconnected(tracker, instanceId, 1);

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that resynchronizing on a fresh tracker, with no connection ever made, is a harmless no-op.</summary>
    [Fact]
    public void NotifyResynchronized_OnFreshTracker_DoesNotThrow()
    {
        var tracker = new AdapterAvailabilityTracker();

        tracker.NotifyResynchronized(AdapterInstanceId.NewId(), 1, new UnrelatedResynchronizationToken());

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that disconnecting before ever resynchronizing leaves resynchronization still required, rather than resetting it.</summary>
    [Fact]
    public void PublishDisconnected_BeforeEverResynchronizing_StillNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        PublishDisconnected(tracker, instanceId, 1);

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that GetSnapshot reports the same combined state as the individual properties after connecting.</summary>
    [Fact]
    public void GetSnapshot_AfterConnecting_MatchesIndividualProperties()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

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
        PublishConnected(tracker, oldInstanceId, 1);
        PublishConnected(tracker, currentInstanceId, 2);

        PublishDisconnected(tracker, oldInstanceId, 1);
        tracker.NotifyResynchronized(oldInstanceId, 1, new UnrelatedResynchronizationToken());

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
        PublishConnected(tracker, instanceId, 1);
        PublishConnected(tracker, instanceId, 2);

        PublishDisconnected(tracker, instanceId, 1);
        tracker.NotifyResynchronized(instanceId, 1, new UnrelatedResynchronizationToken());

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
        PublishConnected(tracker, instanceId, 1);
        PublishDisconnected(tracker, instanceId, 1);

        tracker.NotifyResynchronized(instanceId, 1, new UnrelatedResynchronizationToken());

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
        PublishConnected(tracker, oldInstanceId, 1);
        PublishConnected(tracker, currentInstanceId, 2);

        Task[] staleNotifications = Enumerable.Range(0, 32)
            .Select(index => Task.Run(() =>
            {
                PublishDisconnected(tracker, oldInstanceId, 1);
                tracker.NotifyResynchronized(oldInstanceId, 1, new UnrelatedResynchronizationToken());
            }))
            .ToArray();

        await Task.WhenAll(staleNotifications);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.Equal(currentInstanceId, snapshot.CurrentInstanceId);
        Assert.Equal(2, snapshot.ConnectionGeneration);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that a delayed availability subscriber does not block a concurrent commit:
    /// <see cref="AdapterAvailabilityTracker.CommitConnected"/> only ever holds its own
    /// field-mutation lock, never the caller's publication step, so a second commit is free to
    /// proceed and land while the first transition's subscriber is still running. Serializing a
    /// complete commit-then-publish sequence relative to another is
    /// <see cref="IAdapterConnectionLifecycle"/>'s responsibility, proven by its own tests.
    /// </summary>
    [Fact]
    public async Task AvailabilityChanged_DelayedSubscriber_DoesNotBlockAConcurrentCommit()
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

        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Task firstConnection = Task.Run(() => PublishConnected(tracker, instanceId, 1));
        Assert.True(firstCallbackEntered.Wait(TimeSpan.FromSeconds(5)));

        //  The first subscriber is still blocked inside its callback here: committing (not
        //  necessarily publishing) the disconnect must not wait for it.
        AdapterAvailabilityTransition? disconnectTransition = tracker.CommitDisconnected(instanceId, 1);
        Assert.NotNull(disconnectTransition);
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);

        releaseFirstCallback.Set();
        await firstConnection;
        tracker.PublishTransition(disconnectTransition!);

        Assert.Equal(2, callbackCount);
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

        PublishConnected(tracker, instanceId, 1);

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
        PublishConnected(tracker, instanceId, 1);
        int laterSubscriberCallCount = 0;
        tracker.AvailabilityChanged += _ => throw new InvalidOperationException("Simulated subscriber failure.");
        tracker.AvailabilityChanged += transition =>
        {
            laterSubscriberCallCount++;
            Assert.Equal(AdapterAvailability.Unavailable, transition.Current);
        };

        PublishDisconnected(tracker, instanceId, 1);

        Assert.Equal(1, laterSubscriberCallCount);
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that concurrent claimers receive only one resynchronization authorization.</summary>
    [Fact]
    public async Task TryClaimResynchronizationToken_ConcurrentClaims_AreExclusive()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        IAdapterResynchronizationToken?[] claims = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => Task.Run(tracker.TryClaimResynchronizationToken)));

        IAdapterResynchronizationToken token = Assert.Single(claims, claim => claim is not null)!;
        Assert.Null(tracker.TryClaimResynchronizationToken());
        Assert.True(tracker.IsCurrentResynchronizationToken(token));

        tracker.NotifyResynchronized(instanceId, 1, token);

        Assert.False(tracker.IsCurrentResynchronizationToken(token));
        Assert.Null(tracker.TryClaimResynchronizationToken());
    }

    // ---- Play-context re-arm ----

    /// <summary>
    /// Verifies that re-arming for a play-context transition marks resynchronization needed again
    /// and mints a fresh token, without touching Current, CurrentInstanceId, or
    /// CurrentConnectionGeneration -- a context transition is a new-context baseline requirement,
    /// not an adapter connection/availability change.
    /// </summary>
    [Fact]
    public void RearmResynchronizationForPlayContextTransition_WhileConnected_RearmsWithoutChangingConnectionIdentity()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);
        IAdapterResynchronizationToken firstToken = tracker.TryClaimResynchronizationToken()!;
        tracker.NotifyResynchronized(instanceId, 1, firstToken);
        Assert.False(tracker.NeedsResynchronization);

        tracker.RearmResynchronizationForPlayContextTransition();

        Assert.Equal(AdapterAvailability.Available, tracker.Current);
        Assert.Equal(instanceId, tracker.CurrentInstanceId);
        Assert.Equal(1, tracker.CurrentConnectionGeneration);
        Assert.True(tracker.NeedsResynchronization);
        Assert.False(tracker.IsCurrentResynchronizationToken(firstToken));
        IAdapterResynchronizationToken? secondToken = tracker.TryClaimResynchronizationToken();
        Assert.NotNull(secondToken);
        Assert.NotSame(firstToken, secondToken);
    }

    /// <summary>Verifies that re-arming while no adapter is connected is a no-op: there is no live connection generation to arm a fresh baseline requirement against.</summary>
    [Fact]
    public void RearmResynchronizationForPlayContextTransition_WhileDisconnected_IsNoOp()
    {
        var tracker = new AdapterAvailabilityTracker();

        tracker.RearmResynchronizationForPlayContextTransition();

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.False(tracker.NeedsResynchronization);
        Assert.Null(tracker.TryClaimResynchronizationToken());
    }

    /// <summary>Verifies that re-arming after a genuine disconnect (not merely a play-context transition while connected) also stays a no-op, matching the disconnected case exactly.</summary>
    [Fact]
    public void RearmResynchronizationForPlayContextTransition_AfterDisconnect_IsNoOp()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);
        PublishDisconnected(tracker, instanceId, 1);

        tracker.RearmResynchronizationForPlayContextTransition();

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
        Assert.Null(tracker.TryClaimResynchronizationToken());
    }

    /// <summary>Verifies that mixed concurrent connection notifications preserve one coherent current connection.</summary>
    [Fact]
    public async Task MixedConcurrentConnectionNotifications_PreserveCoherentState()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId initialInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, initialInstanceId, 1);
        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        AdapterInstanceId thirdInstanceId = AdapterInstanceId.NewId();

        Task[] operations =
        [
            Task.Run(() => PublishConnected(tracker, secondInstanceId, 2)),
            Task.Run(() => PublishDisconnected(tracker, initialInstanceId, 1)),
            Task.Run(() => tracker.NotifyResynchronized(initialInstanceId, 1, new UnrelatedResynchronizationToken())),
            Task.Run(() => PublishConnected(tracker, thirdInstanceId, 3)),
        ];

        await Task.WhenAll(operations);

        AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
        Assert.Equal(AdapterAvailability.Available, snapshot.Current);
        Assert.NotNull(snapshot.CurrentInstanceId);
        Assert.Contains(snapshot.CurrentInstanceId.Value, new[] { secondInstanceId, thirdInstanceId });
        Assert.True(snapshot.ConnectionGeneration is 2 or 3);
        Assert.True(snapshot.NeedsResynchronization);
    }

    /// <summary>Commits and publishes a connected transition in one call, for tests that only care about the combined effect and not the two-step API split.</summary>
    private static void PublishConnected(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long generation)
    {
        AdapterAvailabilityTransition? transition = tracker.CommitConnected(instanceId, generation);
        if (transition is not null)
        {
            tracker.PublishTransition(transition);
        }
    }

    /// <summary>Commits and publishes a disconnected transition in one call, for tests that only care about the combined effect and not the two-step API split.</summary>
    private static void PublishDisconnected(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long connectionGeneration)
    {
        AdapterAvailabilityTransition? transition = tracker.CommitDisconnected(instanceId, connectionGeneration);
        if (transition is not null)
        {
            tracker.PublishTransition(transition);
        }
    }

    /// <summary>
    /// Claims the current connection's resynchronization token and reports it resynchronized in one
    /// call, for tests that only care about the combined effect and not the token hand-off. A no-op
    /// when no token could be claimed.
    /// </summary>
    private static void Resynchronize(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long connectionGeneration)
    {
        IAdapterResynchronizationToken? token = tracker.TryClaimResynchronizationToken();
        if (token is not null)
        {
            tracker.NotifyResynchronized(instanceId, connectionGeneration, token);
        }
    }

    /// <summary>An arbitrary token distinct from any tracker's real current token, for a test whose notification is expected to be rejected for a reason other than the token itself (a stale instance, generation, or connection state).</summary>
    private sealed class UnrelatedResynchronizationToken : IAdapterResynchronizationToken
    {
    }
}
