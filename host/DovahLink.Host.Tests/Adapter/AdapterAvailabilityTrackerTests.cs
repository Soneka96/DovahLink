using DovahLink.Host;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;

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

// TODO(stage4-file-extraction): Move ResynchronizationTransactionCoordinatorTests
// to its own file in the post-Stage-4 structural cleanup PR. Temporarily
// colocated with the sibling adapter-availability test to hold this PR's
// changed-file count down; extraction only, no behavior change.
/// <summary>Tests for <see cref="ResynchronizationTransactionCoordinator"/>.</summary>
public class ResynchronizationTransactionCoordinatorTests
{
    private static readonly StateAreaId HealthArea = new(Constants.CharacterHealthStateArea);
    private static readonly StateAreaId MagickaArea = new(Constants.CharacterMagickaStateArea);
    private static readonly StateAreaId StaminaArea = new(Constants.CharacterStaminaStateArea);
    private static readonly StateAreaId XpArea = new(Constants.CharacterXpStateArea);
    private static readonly StateAreaId LevelArea = new(Constants.CharacterLevelStateArea);
    private static readonly StateAreaId[] AllFiveAreas = [HealthArea, MagickaArea, StaminaArea, XpArea, LevelArea];

    /// <summary>
    /// Verifies that a full five-area transaction completes -- notifying the tracker exactly once --
    /// only once every required area is accepted and the adapter's own plan is accepted, regardless
    /// of which half lands first.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullTransaction_BothHalvesLandInEitherOrder_CompletesExactlyOnce(bool planAcceptedFirst)
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId context = PlayContextId.NewId();
        int resynchronizedCount = 0;
        tracker.Resynchronized += (_, _) => resynchronizedCount++;

        void AcceptAllAreas()
        {
            foreach (StateAreaId area in AllFiveAreas)
            {
                Assert.NotNull(coordinator.AcquireToken(instanceId, 1, context, 1));
                coordinator.RecordAreaAccepted(area, instanceId, 1, context, 1);
            }
        }

        if (planAcceptedFirst)
        {
            coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, context, 1);
            Assert.True(tracker.NeedsResynchronization);
            AcceptAllAreas();
        }
        else
        {
            AcceptAllAreas();
            Assert.True(tracker.NeedsResynchronization);
            coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, context, 1);
        }

        Assert.False(tracker.NeedsResynchronization);
        Assert.Equal(1, resynchronizedCount);
    }

    /// <summary>Verifies that every required area being accepted never completes the transaction on its own when the adapter's own plan was reported not accepted (for example a failed event registration).</summary>
    [Fact]
    public void AllAreasAcceptedButPlanNotAccepted_NeverCompletes()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId context = PlayContextId.NewId();

        foreach (StateAreaId area in AllFiveAreas)
        {
            coordinator.AcquireToken(instanceId, 1, context, 1);
            coordinator.RecordAreaAccepted(area, instanceId, 1, context, 1);
        }

        coordinator.RecordAdapterPlanAccepted(false, instanceId, 1, context, 1);

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that the plan being accepted never completes the transaction on its own while a required area is still missing.</summary>
    [Fact]
    public void PlanAcceptedButOneAreaMissing_NeverCompletes()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId context = PlayContextId.NewId();

        foreach (StateAreaId area in AllFiveAreas.Where(area => area != LevelArea))
        {
            coordinator.AcquireToken(instanceId, 1, context, 1);
            coordinator.RecordAreaAccepted(area, instanceId, 1, context, 1);
        }

        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, context, 1);

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that a strictly newer play-context generation on the same connection replaces the
    /// tracked transaction outright, discarding its partial progress, so a later call for the
    /// superseded context can never complete the new one -- the "Context B arrives while Context A's
    /// resync is still pending" scenario.
    /// </summary>
    [Fact]
    public void NewerPlayContextGeneration_ReplacesTrackedTransactionAndDiscardsOldProgress()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId contextA = PlayContextId.NewId();
        PlayContextId contextB = PlayContextId.NewId();

        // Context A's transaction starts, and its plan is accepted, but not every area lands yet.
        coordinator.AcquireToken(instanceId, 1, contextA, 1);
        coordinator.RecordAreaAccepted(HealthArea, instanceId, 1, contextA, 1);
        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, contextA, 1);
        Assert.True(tracker.NeedsResynchronization);

        // Context B supersedes A (generation 2 > 1) before A ever finished. In production this
        // re-arm is PlayContextResynchronizationTrigger's own real call, always made before any
        // capture for the new context can reach this coordinator -- it is what actually mints the
        // fresh token AcquireToken below claims for B.
        tracker.RearmResynchronizationForPlayContextTransition();
        foreach (StateAreaId area in AllFiveAreas)
        {
            coordinator.AcquireToken(instanceId, 1, contextB, 2);
            coordinator.RecordAreaAccepted(area, instanceId, 1, contextB, 2);
        }

        // A's own plan-accepted report was discarded along with the rest of its progress: B still
        // needs its own plan-accepted report, even though every one of B's areas already landed.
        Assert.True(tracker.NeedsResynchronization);

        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, contextB, 2);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that a call for an older play-context generation than the one currently tracked is
    /// ignored outright -- never resurrecting stale progress, and never crediting the newer
    /// transaction -- matching "a stale ResynchronizeResult/CaptureResult arriving after a new context
    /// started is ignored."
    /// </summary>
    [Fact]
    public void OlderPlayContextGeneration_IsIgnoredAndCannotCompleteTheNewerTransaction()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId contextA = PlayContextId.NewId();
        PlayContextId contextB = PlayContextId.NewId();

        // B starts tracking (generation 2), then a stale message for A (generation 1) arrives late.
        coordinator.AcquireToken(instanceId, 1, contextB, 2);
        foreach (StateAreaId area in AllFiveAreas.Where(area => area != HealthArea))
        {
            coordinator.RecordAreaAccepted(area, instanceId, 1, contextB, 2);
        }

        IAdapterResynchronizationToken? staleToken = coordinator.AcquireToken(instanceId, 1, contextA, 1);
        coordinator.RecordAreaAccepted(HealthArea, instanceId, 1, contextA, 1);
        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, contextA, 1);

        Assert.Null(staleToken);
        // B's own Health area is still missing: the stale A-tagged Health accept did not count for it.
        Assert.True(tracker.NeedsResynchronization);

        coordinator.RecordAreaAccepted(HealthArea, instanceId, 1, contextB, 2);
        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, contextB, 2);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that a coherent multi-area capture unit (Vitals) is satisfied only when every one of
    /// its areas is accepted under the same tuple: one area (Health) accepted under a since-superseded
    /// context must not count toward the same area's requirement under the new context.
    /// </summary>
    [Fact]
    public void PartialVitalsAcceptUnderSupersededContext_LeavesVitalsIncompleteUnderNewContext()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId contextA = PlayContextId.NewId();
        PlayContextId contextB = PlayContextId.NewId();

        coordinator.AcquireToken(instanceId, 1, contextA, 1);
        coordinator.RecordAreaAccepted(HealthArea, instanceId, 1, contextA, 1);

        // Context transitions to B before Magicka/Stamina land; only Health, XP, and Level accept
        // under B -- Magicka and Stamina remain outstanding. The re-arm mints B's fresh token, the
        // same real call PlayContextResynchronizationTrigger makes on every play-context transition.
        tracker.RearmResynchronizationForPlayContextTransition();
        foreach (StateAreaId area in new[] { HealthArea, XpArea, LevelArea })
        {
            coordinator.AcquireToken(instanceId, 1, contextB, 2);
            coordinator.RecordAreaAccepted(area, instanceId, 1, contextB, 2);
        }

        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, contextB, 2);

        Assert.True(tracker.NeedsResynchronization);

        coordinator.RecordAreaAccepted(MagickaArea, instanceId, 1, contextB, 2);
        coordinator.RecordAreaAccepted(StaminaArea, instanceId, 1, contextB, 2);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that a token, once claimed for a tuple, is reused across every later call for the same tuple rather than being claimed again.</summary>
    [Fact]
    public void AcquireToken_CalledRepeatedlyForSameTuple_ReturnsTheSameToken()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId context = PlayContextId.NewId();

        IAdapterResynchronizationToken? first = coordinator.AcquireToken(instanceId, 1, context, 1);
        IAdapterResynchronizationToken? second = coordinator.AcquireToken(instanceId, 1, context, 1);
        IAdapterResynchronizationToken? third = coordinator.AcquireToken(instanceId, 1, context, 1);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Same(first, third);
    }

    /// <summary>Verifies that a plan reported not accepted, with no area ever accepted either, leaves the transaction incomplete -- the simplest case, mirroring the all-areas-accepted-but-plan-not-accepted case from the other direction.</summary>
    [Fact]
    public void PlanNotAcceptedAndNoAreasAccepted_NeverCompletes()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId context = PlayContextId.NewId();

        coordinator.RecordAdapterPlanAccepted(false, instanceId, 1, context, 1);

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that accepting the same area twice under the same transaction is harmless and does not complete the transaction on its own (still missing the other four areas and the plan).</summary>
    [Fact]
    public void RecordAreaAccepted_SameAreaTwice_IsIdempotentAndDoesNotOverCredit()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId context = PlayContextId.NewId();

        coordinator.RecordAreaAccepted(HealthArea, instanceId, 1, context, 1);
        coordinator.RecordAreaAccepted(HealthArea, instanceId, 1, context, 1);
        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, context, 1);

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that a stale RecordAdapterPlanAccepted call -- for a generation older than the one currently tracked -- is ignored, mirroring the same guarantee already proven for RecordAreaAccepted.</summary>
    [Fact]
    public void RecordAdapterPlanAccepted_OlderGeneration_IsIgnored()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId contextA = PlayContextId.NewId();
        PlayContextId contextB = PlayContextId.NewId();

        // B starts tracking (generation 2) and completes every area, but not its plan yet.
        foreach (StateAreaId area in AllFiveAreas)
        {
            coordinator.AcquireToken(instanceId, 1, contextB, 2);
            coordinator.RecordAreaAccepted(area, instanceId, 1, contextB, 2);
        }

        // A stale plan-accepted for the superseded context A must not complete B.
        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, contextA, 1);

        Assert.True(tracker.NeedsResynchronization);

        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, contextB, 2);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that a call carrying a different adapter instance than the one currently tracked, but the exact same connection and play-context generations, is treated as stale rather than as a newer transaction -- generation ordering alone decides "newer", not instance identity.</summary>
    [Fact]
    public void MismatchedInstanceIdAtSameGenerations_IsTreatedAsStaleNotNewer()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        AdapterInstanceId otherInstanceId = AdapterInstanceId.NewId();
        Connect(tracker, firstInstanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId context = PlayContextId.NewId();

        coordinator.AcquireToken(firstInstanceId, 1, context, 1);
        coordinator.RecordAreaAccepted(HealthArea, firstInstanceId, 1, context, 1);

        IAdapterResynchronizationToken? otherInstanceToken = coordinator.AcquireToken(otherInstanceId, 1, context, 1);

        Assert.Null(otherInstanceToken);

        // The original instance's progress must still be intact and completable.
        foreach (StateAreaId area in AllFiveAreas.Where(area => area != HealthArea))
        {
            coordinator.RecordAreaAccepted(area, firstInstanceId, 1, context, 1);
        }

        coordinator.RecordAdapterPlanAccepted(true, firstInstanceId, 1, context, 1);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that an empty required-area set (no BaselineSample units in the catalog) completes the transaction from the accepted plan alone, with no area ever needing to be recorded.</summary>
    [Fact]
    public void EmptyRequiredAreaSet_CompletesFromPlanAcceptedAlone()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(new LiveStateCatalog([], []), tracker);
        PlayContextId context = PlayContextId.NewId();

        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, context, 1);

        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that TryMarkCompletedLocked's own defensive token claim -- needed for an empty
    /// required-area set, since no area's own AcquireToken call ever claimed one -- leaves the
    /// transaction incomplete rather than throwing when the tracker has no claimable token to give,
    /// for example because something else already consumed the connection's one-time token first.
    /// </summary>
    [Fact]
    public void EmptyRequiredAreaSet_NoClaimableTokenAvailable_NeverCompletes()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        Assert.NotNull(tracker.TryClaimResynchronizationToken());
        var coordinator = new ResynchronizationTransactionCoordinator(new LiveStateCatalog([], []), tracker);
        PlayContextId context = PlayContextId.NewId();

        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, context, 1);

        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that concurrently recording every area and the plan-accepted result for one transaction completes it exactly once, never more.</summary>
    [Fact]
    public async Task ConcurrentAreaAndPlanRecording_CompletesExactlyOnce()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId context = PlayContextId.NewId();
        int resynchronizedCount = 0;
        tracker.Resynchronized += (_, _) => Interlocked.Increment(ref resynchronizedCount);

        IEnumerable<Task> recordTasks = AllFiveAreas
            .Select(area => Task.Run(() =>
            {
                coordinator.AcquireToken(instanceId, 1, context, 1);
                coordinator.RecordAreaAccepted(area, instanceId, 1, context, 1);
            }))
            .Append(Task.Run(() => coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, context, 1)));

        await Task.WhenAll(recordTasks);

        Assert.False(tracker.NeedsResynchronization);
        Assert.Equal(1, resynchronizedCount);
    }

    /// <summary>
    /// Reproduces BLOCKER 1: a transaction that reaches ready-to-complete (every required area
    /// already accepted, only the adapter's own plan-accepted report still outstanding) must not be
    /// able to complete a newer requirement that a play-context re-arm mints in the meantime, on the
    /// same adapter instance and connection generation. A play-context transition re-arms the tracker
    /// directly (<see cref="DovahLink.Host.Adapter.Ipc.PlayContextResynchronizationTrigger"/>'s real call), independently of
    /// whatever tuple this coordinator is still tracking, so the coordinator's own captured token can
    /// go stale without any newer tuple ever being tracked here at all -- the coordinator's own
    /// newer-tuple replacement (see <see cref="NewerPlayContextGeneration_ReplacesTrackedTransactionAndDiscardsOldProgress"/>)
    /// does not, by itself, protect against this: it only guards a later call for the newer tuple,
    /// not an in-flight completion still carrying the older tuple's own already-claimed token.
    /// </summary>
    [Fact]
    public void PlanAcceptedReport_ArrivesAfterPlayContextRearm_DoesNotCompleteWithStaleToken()
    {
        var tracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(tracker, instanceId, 1);
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);
        PlayContextId contextA = PlayContextId.NewId();

        // Context A's transaction reaches ready-to-complete: its token is claimed and every required
        // area is accepted, leaving only the adapter's own plan-accepted report outstanding.
        IAdapterResynchronizationToken? tokenA = coordinator.AcquireToken(instanceId, 1, contextA, 1);
        Assert.NotNull(tokenA);
        foreach (StateAreaId area in AllFiveAreas)
        {
            coordinator.RecordAreaAccepted(area, instanceId, 1, contextA, 1);
        }

        Assert.True(tracker.NeedsResynchronization);

        // Before A's plan-accepted report arrives, a play-context transition commits on the same
        // connection and re-arms the tracker directly, minting a fresh token -- A's own captured
        // token is now stale even though the coordinator has not tracked any newer tuple yet.
        tracker.RearmResynchronizationForPlayContextTransition();
        Assert.True(tracker.NeedsResynchronization);
        Assert.False(tracker.IsCurrentResynchronizationToken(tokenA!));

        // A's now-stale plan-accepted report finally arrives and completes the coordinator's
        // still-A-tracked transaction, reporting A's own originally claimed token.
        coordinator.RecordAdapterPlanAccepted(true, instanceId, 1, contextA, 1);

        // A's completion must be ignored: the newer requirement minted by the re-arm survives, and
        // A's stale token never becomes current again.
        Assert.True(tracker.NeedsResynchronization);
        Assert.False(tracker.IsCurrentResynchronizationToken(tokenA!));

        // The requirement the re-arm minted is still genuinely live and claimable, distinct from A's token.
        IAdapterResynchronizationToken? tokenAfterRearm = tracker.TryClaimResynchronizationToken();
        Assert.NotNull(tokenAfterRearm);
        Assert.NotSame(tokenA, tokenAfterRearm);
    }

    /// <summary>Verifies that AcquireToken returns null, and records nothing, when the tracker has no claimable token to give (for example no adapter connected).</summary>
    [Fact]
    public void AcquireToken_NoAdapterConnected_ReturnsNull()
    {
        var tracker = new AdapterAvailabilityTracker();
        var coordinator = new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, tracker);

        IAdapterResynchronizationToken? token = coordinator.AcquireToken(AdapterInstanceId.NewId(), 1, PlayContextId.NewId(), 1);

        Assert.Null(token);
    }

    /// <summary>Commits and publishes a connected transition in one call.</summary>
    private static void Connect(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long generation)
    {
        AdapterAvailabilityTransition? transition = tracker.CommitConnected(instanceId, generation);
        if (transition is not null)
        {
            tracker.PublishTransition(transition);
        }
    }
}
