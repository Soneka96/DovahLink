using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="StateAuthorityLifecycle"/>.</summary>
public class StateAuthorityLifecycleTests
{
    /// <summary>Verifies that construction mints a startup value without requiring any adapter activity.</summary>
    [Fact]
    public void Constructor_MintsStartupValueEagerly()
    {
        var tracker = new FakeAdapterAvailabilityTracker();

        var lifecycle = new StateAuthorityLifecycle(tracker);

        Assert.NotEqual(default, lifecycle.Current.Value);
        Assert.False(lifecycle.IsFaulted);
    }

    /// <summary>Verifies that a startup mint failure propagates out of the constructor uncaught -- the same fail-closed behavior as every other startup precondition.</summary>
    [Fact]
    public void Constructor_IdFactoryThrows_PropagatesUncaught()
    {
        var tracker = new FakeAdapterAvailabilityTracker();

        Assert.Throws<InvalidOperationException>(() => new StateAuthorityLifecycle(tracker, () => throw new InvalidOperationException("mint failed")));
    }

    /// <summary>Verifies that an adapter connecting, with no preceding loss, never rotates the value.</summary>
    [Fact]
    public void AdapterConnects_DoesNotRotate()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        StateAuthorityId startupValue = lifecycle.Current;

        PublishConnected(tracker, AdapterInstanceId.NewId(), 1);

        Assert.Equal(startupValue, lifecycle.Current);
    }

    /// <summary>Verifies that the first detected continuity loss rotates the value exactly once, at detection.</summary>
    [Fact]
    public void FirstContinuityLossDetected_RotatesOnce()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        StateAuthorityId startupValue = lifecycle.Current;
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        PublishDisconnected(tracker, instanceId, 1);

        Assert.NotEqual(startupValue, lifecycle.Current);
    }

    /// <summary>
    /// Verifies the repeated-loss lock-down: any number of further failed reconnect/resync attempts
    /// before a fresh baseline is ever established must not rotate the value again.
    /// </summary>
    [Fact]
    public void RepeatedLossBeforeBaseline_DoesNotRotateAgain()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, firstInstanceId, 1);
        PublishDisconnected(tracker, firstInstanceId, 1);
        StateAuthorityId rotatedValue = lifecycle.Current;

        // A further reconnect/resync attempt fails again before any resynchronization ever completes.
        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, secondInstanceId, 2);
        PublishDisconnected(tracker, secondInstanceId, 2);

        Assert.Equal(rotatedValue, lifecycle.Current);
    }

    /// <summary>Verifies that the first detected continuity loss raises <see cref="IStateAuthorityLifecycle.Rotated"/> exactly once, carrying the newly minted value.</summary>
    [Fact]
    public void FirstContinuityLossDetected_RaisesRotatedExactlyOnceWithNewValue()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        var rotatedValues = new List<StateAuthorityId>();
        lifecycle.Rotated += rotatedValues.Add;
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        PublishDisconnected(tracker, instanceId, 1);

        Assert.Equal([lifecycle.Current], rotatedValues);
    }

    /// <summary>
    /// Verifies the repeated-loss lock-down also covers <see cref="IStateAuthorityLifecycle.Rotated"/>:
    /// a further failed reconnect/resync attempt before a fresh baseline is ever established must not
    /// raise it again.
    /// </summary>
    [Fact]
    public void RepeatedLossBeforeBaseline_DoesNotRaiseRotatedAgain()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, firstInstanceId, 1);
        PublishDisconnected(tracker, firstInstanceId, 1);
        int rotatedRaisedCount = 0;
        lifecycle.Rotated += _ => rotatedRaisedCount++;

        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, secondInstanceId, 2);
        PublishDisconnected(tracker, secondInstanceId, 2);

        Assert.Equal(0, rotatedRaisedCount);
    }

    /// <summary>
    /// Verifies that once a fresh authoritative baseline is established, a later continuity loss
    /// starts a new epoch and rotates again.
    /// </summary>
    [Fact]
    public void LossAfterFreshBaseline_RotatesAgain()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, firstInstanceId, 1);
        PublishDisconnected(tracker, firstInstanceId, 1);
        StateAuthorityId rotatedValue = lifecycle.Current;

        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, secondInstanceId, 2);
        Resynchronize(tracker, secondInstanceId, 2);
        PublishDisconnected(tracker, secondInstanceId, 2);

        Assert.NotEqual(rotatedValue, lifecycle.Current);
    }

    /// <summary>Verifies that a second rotation, after a fresh baseline resolved the first break, also raises <see cref="IStateAuthorityLifecycle.Rotated"/> -- the event is not a one-time startup artifact.</summary>
    [Fact]
    public void LossAfterFreshBaseline_RaisesRotatedAgainWithNewValue()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, firstInstanceId, 1);
        PublishDisconnected(tracker, firstInstanceId, 1);
        var rotatedValues = new List<StateAuthorityId>();
        lifecycle.Rotated += rotatedValues.Add;

        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, secondInstanceId, 2);
        Resynchronize(tracker, secondInstanceId, 2);
        PublishDisconnected(tracker, secondInstanceId, 2);

        Assert.Equal([lifecycle.Current], rotatedValues);
    }

    /// <summary>
    /// Verifies that a resynchronization signal with no continuity break in progress is a harmless
    /// no-op: it neither rotates the value nor prevents the next real loss from rotating it, and
    /// raises <see cref="IStateAuthorityLifecycle.Rotated"/> only for that next real loss, not for
    /// the harmless resynchronization itself.
    /// </summary>
    [Fact]
    public void ResynchronizedWithNoPriorLoss_IsHarmlessNoOp()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        StateAuthorityId startupValue = lifecycle.Current;
        var rotatedValues = new List<StateAuthorityId>();
        lifecycle.Rotated += rotatedValues.Add;
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        Resynchronize(tracker, instanceId, 1);
        Assert.Equal(startupValue, lifecycle.Current);
        Assert.Empty(rotatedValues);

        PublishDisconnected(tracker, instanceId, 1);
        Assert.NotEqual(startupValue, lifecycle.Current);
        Assert.Equal([lifecycle.Current], rotatedValues);
    }

    /// <summary>
    /// Verifies Section C's runtime-mint-failure policy: a failed rotation after a detected break is
    /// fatal, raises <see cref="IStateAuthorityLifecycle.FatalFailureOccurred"/> exactly once, and
    /// leaves no value safe to read afterward.
    /// </summary>
    [Fact]
    public void RuntimeMintFailureOnRotation_FaultsAndRaisesFatalFailureOnce()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        int mintCount = 0;
        Guid FailAfterFirstMint()
        {
            mintCount++;
            return mintCount == 1 ? Guid.NewGuid() : throw new InvalidOperationException("mint failed");
        }

        var lifecycle = new StateAuthorityLifecycle(tracker, FailAfterFirstMint);
        int fatalFailureRaisedCount = 0;
        lifecycle.FatalFailureOccurred += () => fatalFailureRaisedCount++;
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        PublishDisconnected(tracker, instanceId, 1);

        Assert.True(lifecycle.IsFaulted);
        Assert.Equal(1, fatalFailureRaisedCount);
        Assert.Throws<InvalidOperationException>(() => lifecycle.Current);
    }

    /// <summary>Verifies that a failed runtime mint raises <see cref="IStateAuthorityLifecycle.FatalFailureOccurred"/> only, never <see cref="IStateAuthorityLifecycle.Rotated"/> -- there is no new value to announce.</summary>
    [Fact]
    public void RuntimeMintFailureOnRotation_DoesNotRaiseRotated()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        int mintCount = 0;
        Guid FailAfterFirstMint()
        {
            mintCount++;
            return mintCount == 1 ? Guid.NewGuid() : throw new InvalidOperationException("mint failed");
        }

        var lifecycle = new StateAuthorityLifecycle(tracker, FailAfterFirstMint);
        int rotatedRaisedCount = 0;
        lifecycle.Rotated += _ => rotatedRaisedCount++;
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);

        PublishDisconnected(tracker, instanceId, 1);

        Assert.True(lifecycle.IsFaulted);
        Assert.Equal(0, rotatedRaisedCount);
    }

    /// <summary>
    /// Verifies that once faulted, a further detected loss neither attempts another rotation nor
    /// raises <see cref="IStateAuthorityLifecycle.FatalFailureOccurred"/> a second time -- the fault
    /// is terminal, not a per-attempt condition.
    /// </summary>
    [Fact]
    public void ContinuityLossWhileFaulted_DoesNotRotateOrRaiseAgain()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        int mintCount = 0;
        Guid FailFromSecondMintOnward()
        {
            mintCount++;
            return mintCount == 1 ? Guid.NewGuid() : throw new InvalidOperationException("mint failed");
        }

        var lifecycle = new StateAuthorityLifecycle(tracker, FailFromSecondMintOnward);
        int fatalFailureRaisedCount = 0;
        lifecycle.FatalFailureOccurred += () => fatalFailureRaisedCount++;
        int rotatedRaisedCount = 0;
        lifecycle.Rotated += _ => rotatedRaisedCount++;
        AdapterInstanceId firstInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, firstInstanceId, 1);
        PublishDisconnected(tracker, firstInstanceId, 1);
        Assert.True(lifecycle.IsFaulted);

        AdapterInstanceId secondInstanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, secondInstanceId, 2);
        PublishDisconnected(tracker, secondInstanceId, 2);

        Assert.True(lifecycle.IsFaulted);
        Assert.Equal(1, fatalFailureRaisedCount);
        Assert.Equal(0, rotatedRaisedCount);

        // One successful startup mint, one failed rotation attempt -- the second loss must not
        // trigger a third call at all, since the faulted check short-circuits before ever reaching
        // idFactory again.
        Assert.Equal(2, mintCount);
    }

    /// <summary>
    /// Verifies the lock serializes concurrent loss detections into exactly one rotation: many
    /// threads observing the same continuity loss must never each mint their own value, and
    /// <see cref="IStateAuthorityLifecycle.Rotated"/> must not fire once per thread either.
    /// </summary>
    [Fact]
    public async Task ConcurrentContinuityLossDetections_RotateExactlyOnce()
    {
        var tracker = new FakeAdapterAvailabilityTracker();
        var lifecycle = new StateAuthorityLifecycle(tracker);
        StateAuthorityId startupValue = lifecycle.Current;
        var rotatedValues = new List<StateAuthorityId>();
        lifecycle.Rotated += value =>
        {
            lock (rotatedValues)
            {
                rotatedValues.Add(value);
            }
        };
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        PublishConnected(tracker, instanceId, 1);
        var transition = new AdapterAvailabilityTransition(AdapterAvailability.Available, AdapterAvailability.Unavailable, instanceId, 1);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => tracker.PublishTransition(transition))));

        Assert.NotEqual(startupValue, lifecycle.Current);
        Assert.Equal([lifecycle.Current], rotatedValues);
        Assert.False(lifecycle.IsFaulted);
    }

    /// <summary>Commits and publishes a connected transition in one call, matching the tracker's own two-step commit/publish split.</summary>
    private static void PublishConnected(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long generation)
    {
        AdapterAvailabilityTransition? transition = tracker.CommitConnected(instanceId, generation);
        if (transition is not null)
        {
            tracker.PublishTransition(transition);
        }
    }

    /// <summary>Commits and publishes a disconnected transition in one call, matching the tracker's own two-step commit/publish split.</summary>
    private static void PublishDisconnected(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long connectionGeneration)
    {
        AdapterAvailabilityTransition? transition = tracker.CommitDisconnected(instanceId, connectionGeneration);
        if (transition is not null)
        {
            tracker.PublishTransition(transition);
        }
    }

    /// <summary>Claims the current connection's resynchronization token and reports it resynchronized in one call.</summary>
    private static void Resynchronize(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long connectionGeneration)
    {
        IAdapterResynchronizationToken? token = tracker.TryClaimResynchronizationToken();
        if (token is not null)
        {
            tracker.NotifyResynchronized(instanceId, connectionGeneration, token);
        }
    }
}
