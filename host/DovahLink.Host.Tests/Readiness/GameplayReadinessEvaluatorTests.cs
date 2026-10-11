using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Readiness;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Readiness;

/// <summary>Tests for <see cref="GameplayReadinessEvaluator"/>, driven by the real Adapter availability and play-context trackers.</summary>
public class GameplayReadinessEvaluatorTests
{
    /// <summary>Verifies that a freshly started Host with nothing connected and no play context is not ready.</summary>
    [Fact]
    public void NewHost_IsNotReady()
    {
        var rig = new Rig();

        Assert.False(rig.Evaluator.IsReady);
        Assert.Empty(rig.Changes);
    }

    /// <summary>Verifies the main-menu case: a connected, resynchronized Adapter with no play context is not ready.</summary>
    [Fact]
    public void MainMenu_ConnectedAndResynchronizedWithoutPlayContext_IsNotReady()
    {
        var rig = new Rig();

        rig.Connect();
        rig.Resynchronize();

        Assert.False(rig.Evaluator.IsReady);
        Assert.Empty(rig.Changes);
    }

    /// <summary>Verifies that a new play context is not ready until resynchronization completes, then becomes ready exactly once.</summary>
    [Fact]
    public void NewPlayContext_IsReadyOnlyAfterResynchronizationCompletes()
    {
        var rig = new Rig();
        rig.Connect();
        rig.Resynchronize();

        rig.NewContext();
        Assert.False(rig.Evaluator.IsReady);

        rig.Resynchronize();

        Assert.True(rig.Evaluator.IsReady);
        Assert.Equal([true], rig.Changes);
    }

    /// <summary>Verifies that the main-menu return (context ended) makes a ready Host not ready and publishes the change.</summary>
    [Fact]
    public void ContextEnded_WhileReady_BecomesNotReady()
    {
        var rig = new Rig();
        rig.BecomeReady();

        rig.PlayContext.ClearCurrent();

        Assert.False(rig.Evaluator.IsReady);
        Assert.Equal([true, false], rig.Changes);
    }

    /// <summary>Verifies that an Adapter disconnect is not ready immediately -- even before the transition is published and while a non-null play context is still retained.</summary>
    [Fact]
    public void AdapterDisconnect_WithRetainedContext_IsNotReadyImmediately()
    {
        var rig = new Rig();
        rig.BecomeReady();
        Assert.NotNull(rig.PlayContext.Current);

        AdapterAvailabilityTransition? committed = rig.Availability.CommitDisconnected(rig.InstanceId, rig.Generation);

        Assert.NotNull(committed);
        Assert.False(rig.Evaluator.IsReady);
        Assert.NotNull(rig.PlayContext.Current);
        Assert.Equal([true], rig.Changes);

        rig.Availability.PublishTransition(committed);

        Assert.Equal([true, false], rig.Changes);
    }

    /// <summary>Verifies that reconnecting the Adapter on the unchanged context stays not ready until the connection resynchronizes.</summary>
    [Fact]
    public void AdapterReconnect_OnRetainedContext_IsNotReadyUntilResynchronized()
    {
        var rig = new Rig();
        rig.BecomeReady();
        rig.Disconnect();

        rig.Connect();
        Assert.False(rig.Evaluator.IsReady);

        rig.Resynchronize();

        Assert.True(rig.Evaluator.IsReady);
        Assert.Equal([true, false, true], rig.Changes);
    }

    /// <summary>Verifies that a replacement play context makes a ready Host not ready until the new context is resynchronized.</summary>
    [Fact]
    public void ContextReplacement_WhileReady_IsNotReadyUntilResynchronized()
    {
        var rig = new Rig();
        rig.BecomeReady();

        rig.NewContext();
        Assert.False(rig.Evaluator.IsReady);
        Assert.Equal([true, false], rig.Changes);

        rig.Resynchronize();

        Assert.True(rig.Evaluator.IsReady);
        Assert.Equal([true, false, true], rig.Changes);
    }

    /// <summary>
    /// Verifies the commit-before-re-arm window: a context is committed, the previous transaction's
    /// still-valid token completes before the re-arm runs, and the Host must not read as ready for
    /// the new, un-armed context at any point -- including once the re-arm and a fresh
    /// resynchronization finally follow.
    /// </summary>
    [Fact]
    public void StaleCompletion_BetweenContextCommitAndRearm_NeverReportsReady()
    {
        var rig = new Rig();
        rig.Connect();
        rig.NewContext();
        IAdapterResynchronizationToken staleToken = rig.Availability.TryClaimResynchronizationToken()!;

        rig.PlayContext.NotifyTransition(PlayContextId.NewId());
        rig.Availability.NotifyResynchronized(rig.InstanceId, rig.Generation, staleToken);

        Assert.False(rig.Availability.NeedsResynchronization);
        Assert.False(rig.Evaluator.IsReady);
        Assert.Empty(rig.Changes);

        rig.Availability.RearmResynchronizationForPlayContextTransition(rig.PlayContext.GetSnapshot().TransitionGeneration);
        Assert.False(rig.Evaluator.IsReady);

        rig.Resynchronize();

        Assert.True(rig.Evaluator.IsReady);
        Assert.Equal([true], rig.Changes);
    }

    /// <summary>Verifies that a completion claimed for a superseded context cannot restore readiness after a newer context re-armed.</summary>
    [Fact]
    public void StaleToken_AfterRearmForNewerContext_CannotRestoreReadiness()
    {
        var rig = new Rig();
        rig.Connect();
        rig.NewContext();
        IAdapterResynchronizationToken staleToken = rig.Availability.TryClaimResynchronizationToken()!;
        rig.NewContext();

        rig.Availability.NotifyResynchronized(rig.InstanceId, rig.Generation, staleToken);

        Assert.True(rig.Availability.NeedsResynchronization);
        Assert.False(rig.Evaluator.IsReady);
        Assert.Empty(rig.Changes);
    }

    /// <summary>Verifies that a delayed completion from a previous connection generation cannot restore readiness after a reconnect.</summary>
    [Fact]
    public void StaleCompletion_FromSupersededConnection_CannotRestoreReadiness()
    {
        var rig = new Rig();
        rig.Connect();
        rig.NewContext();
        IAdapterResynchronizationToken oldToken = rig.Availability.TryClaimResynchronizationToken()!;
        long oldGeneration = rig.Generation;
        AdapterInstanceId oldInstance = rig.InstanceId;
        rig.Disconnect();
        rig.Connect();

        rig.Availability.NotifyResynchronized(oldInstance, oldGeneration, oldToken);

        Assert.False(rig.Evaluator.IsReady);
        Assert.Empty(rig.Changes);
    }

    /// <summary>Verifies that repeated equivalent notifications cause neither a wrong value nor a duplicate publication.</summary>
    [Fact]
    public void RepeatedEquivalentNotifications_DoNotRepublish()
    {
        var rig = new Rig();
        rig.BecomeReady();
        rig.Changes.Clear();
        PlayContextId current = rig.PlayContext.Current!.Value;

        rig.PlayContext.NotifyTransition(current);
        rig.Resynchronize();
        rig.Availability.PublishTransition(new AdapterAvailabilityTransition(
            AdapterAvailability.Unavailable, AdapterAvailability.Available, rig.InstanceId, rig.Generation));

        Assert.True(rig.Evaluator.IsReady);
        Assert.Empty(rig.Changes);

        rig.PlayContext.ClearCurrent();
        rig.PlayContext.ClearCurrent();

        Assert.Equal([false], rig.Changes);
    }

    /// <summary>Verifies that a subscriber that throws does not suppress later subscribers or surface into the tracker callback.</summary>
    [Fact]
    public void ThrowingSubscriber_DoesNotSuppressLaterSubscribers()
    {
        var rig = new Rig();
        var seen = new List<bool>();
        rig.Evaluator.ReadinessChanged += _ => throw new InvalidOperationException("subscriber failure");
        rig.Evaluator.ReadinessChanged += seen.Add;

        Exception? exception = Record.Exception(rig.BecomeReady);

        Assert.Null(exception);
        Assert.Equal([true], seen);
    }

    /// <summary>Verifies that a subscriber reading <see cref="IGameplayReadinessEvaluator.IsReady"/> from inside the change callback does not deadlock and sees the committed value.</summary>
    [Fact]
    public void SubscriberReadingCurrentValue_FromChangeCallback_SeesCommittedValue()
    {
        var rig = new Rig();
        var observed = new List<bool>();
        rig.Evaluator.ReadinessChanged += _ => observed.Add(rig.Evaluator.IsReady);

        rig.BecomeReady();
        rig.PlayContext.ClearCurrent();

        Assert.Equal([true, false], observed);
    }

    /// <summary>Verifies that an evaluator built after readiness was already established starts from the live value, so it reports no spurious change.</summary>
    [Fact]
    public void LateConstruction_StartsFromLiveValue()
    {
        var rig = new Rig();
        rig.BecomeReady();
        var late = new GameplayReadinessEvaluator(rig.Availability, rig.PlayContext);
        var changes = new List<bool>();
        late.ReadinessChanged += changes.Add;

        rig.Resynchronize();
        rig.PlayContext.NotifyTransition(rig.PlayContext.Current!.Value);

        Assert.True(late.IsReady);
        Assert.Empty(changes);
    }

    /// <summary>Verifies that the Adapter's own unavailability alone, with every other conjunct satisfied, is not ready.</summary>
    [Fact]
    public void UnavailableAdapter_WithEveryOtherConditionSatisfied_IsNotReady()
    {
        var playContext = new PlayContextTracker();
        playContext.NotifyTransition(PlayContextId.NewId());
        var availability = new FakeAdapterAvailabilityTracker
        {
            Current = AdapterAvailability.Unavailable,
            NeedsResynchronization = false,
            ArmedPlayContextTransitionGeneration = playContext.GetSnapshot().TransitionGeneration,
        };
        var evaluator = new GameplayReadinessEvaluator(availability, playContext);

        Assert.False(evaluator.IsReady);
    }

    /// <summary>Verifies that a play-context change landing between the two validating reads is retried, and the answer comes from the stable read.</summary>
    [Fact]
    public void ContextChangingDuringTheRead_IsRetriedAndAnswersFromTheStableRead()
    {
        PlayContextId context = PlayContextId.NewId();
        int reads = 0;
        var playContext = new ScriptedPlayContextTracker(() =>
        {
            reads++;
            // The first validating pair straddles a transition (generation 1 then 2); later reads are stable at 2.
            return reads == 1 ? new PlayContextSnapshot(null, 1) : new PlayContextSnapshot(context, 2);
        });
        var availability = new FakeAdapterAvailabilityTracker
        {
            Current = AdapterAvailability.Available,
            NeedsResynchronization = false,
            ArmedPlayContextTransitionGeneration = 2,
        };

        var evaluator = new GameplayReadinessEvaluator(availability, playContext);

        Assert.True(evaluator.IsReady);
    }

    /// <summary>Verifies that a play context that never stabilizes yields not ready rather than a guess.</summary>
    [Fact]
    public void ContextThatNeverStabilizes_IsNotReady()
    {
        long generation = 0;
        PlayContextId context = PlayContextId.NewId();
        var playContext = new ScriptedPlayContextTracker(() => new PlayContextSnapshot(context, ++generation));
        var availability = new FakeAdapterAvailabilityTracker
        {
            Current = AdapterAvailability.Available,
            NeedsResynchronization = false,
        };
        var evaluator = new GameplayReadinessEvaluator(availability, playContext);
        availability.ArmedPlayContextTransitionGeneration = generation;

        Assert.False(evaluator.IsReady);
    }

    /// <summary>Verifies the constructor rejects missing collaborators.</summary>
    [Fact]
    public void Constructor_NullCollaborators_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new GameplayReadinessEvaluator(null!, new PlayContextTracker()));
        Assert.Throws<ArgumentNullException>(() => new GameplayReadinessEvaluator(new AdapterAvailabilityTracker(), null!));
    }

    /// <summary>
    /// Real trackers plus the evaluator. The play-context re-arm the production trigger performs is
    /// issued explicitly by <see cref="NewContext"/>, so individual tests can also run the steps apart.
    /// </summary>
    private sealed class Rig
    {
        private long generation;

        public Rig()
        {
            Availability = new AdapterAvailabilityTracker();
            PlayContext = new PlayContextTracker();
            Evaluator = new GameplayReadinessEvaluator(Availability, PlayContext);
            Evaluator.ReadinessChanged += Changes.Add;
        }

        public AdapterAvailabilityTracker Availability { get; }

        public PlayContextTracker PlayContext { get; }

        public GameplayReadinessEvaluator Evaluator { get; }

        public List<bool> Changes { get; } = [];

        public AdapterInstanceId InstanceId { get; private set; } = AdapterInstanceId.NewId();

        public long Generation => generation;

        public void Connect()
        {
            generation++;
            InstanceId = AdapterInstanceId.NewId();
            AdapterAvailabilityTransition? transition = Availability.CommitConnected(InstanceId, generation);
            if (transition is not null)
            {
                Availability.PublishTransition(transition);
            }
        }

        public void Disconnect()
        {
            AdapterAvailabilityTransition? transition = Availability.CommitDisconnected(InstanceId, generation);
            if (transition is not null)
            {
                Availability.PublishTransition(transition);
            }
        }

        /// <summary>Commits a new play context and performs the re-arm the production trigger would.</summary>
        public void NewContext()
        {
            PlayContext.NotifyTransition(PlayContextId.NewId());
            Availability.RearmResynchronizationForPlayContextTransition(PlayContext.GetSnapshot().TransitionGeneration);
        }

        public void Resynchronize()
        {
            IAdapterResynchronizationToken? token = Availability.TryClaimResynchronizationToken();
            if (token is not null)
            {
                Availability.NotifyResynchronized(InstanceId, generation, token);
            }
        }

        public void BecomeReady()
        {
            Connect();
            NewContext();
            Resynchronize();
        }
    }

    /// <summary>A play-context tracker whose snapshot is produced by a script, to force changes between validating reads.</summary>
    private sealed class ScriptedPlayContextTracker : IPlayContextTracker
    {
        private readonly Func<PlayContextSnapshot> snapshot;

        public ScriptedPlayContextTracker(Func<PlayContextSnapshot> snapshot)
        {
            this.snapshot = snapshot;
        }

        public PlayContextId? Current => snapshot().Current;

        public event Action<PlayContextTransition>? Transitioned
        {
            add { }
            remove { }
        }

        public PlayContextSnapshot GetSnapshot() => snapshot();

        public void NotifyTransition(PlayContextId newPlayContextId) => throw new NotSupportedException();

        public void ClearCurrent() => throw new NotSupportedException();
    }
}
