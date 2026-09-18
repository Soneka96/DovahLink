using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.Adapter;

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
