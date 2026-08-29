using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.Tests.PlayContext;

/// <summary>Tests for <see cref="PlayContextTracker"/>.</summary>
public class PlayContextTrackerTests
{
    /// <summary>Verifies that a freshly constructed tracker has no current play context.</summary>
    [Fact]
    public void NewTracker_HasNoCurrentPlayContext()
    {
        var tracker = new PlayContextTracker();

        Assert.Null(tracker.Current);
    }

    /// <summary>Verifies that the first transition establishes the current play context.</summary>
    [Fact]
    public void NotifyTransition_FirstTransition_EstablishesCurrent()
    {
        var tracker = new PlayContextTracker();
        PlayContextId newContext = PlayContextId.NewId();

        tracker.NotifyTransition(newContext);

        Assert.Equal(newContext, tracker.Current);
    }

    /// <summary>Verifies that a subsequent transition updates the current play context.</summary>
    [Fact]
    public void NotifyTransition_SubsequentTransition_UpdatesCurrent()
    {
        var tracker = new PlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId());
        PlayContextId newContext = PlayContextId.NewId();

        tracker.NotifyTransition(newContext);

        Assert.Equal(newContext, tracker.Current);
    }

    /// <summary>Verifies that the first transition fires the event with a null previous play context.</summary>
    [Fact]
    public void NotifyTransition_FirstTransition_FiresEventWithNullPrevious()
    {
        var tracker = new PlayContextTracker();
        PlayContextTransition? observed = null;
        tracker.Transitioned += transition => observed = transition;
        PlayContextId newContext = PlayContextId.NewId();

        tracker.NotifyTransition(newContext);

        Assert.NotNull(observed);
        Assert.Null(observed!.PreviousPlayContextId);
        Assert.Equal(newContext, observed.NewPlayContextId);
    }

    /// <summary>Verifies that a subsequent transition fires the event with both the old and new play context.</summary>
    [Fact]
    public void NotifyTransition_SubsequentTransition_FiresEventWithOldAndNewContext()
    {
        var tracker = new PlayContextTracker();
        PlayContextId firstContext = PlayContextId.NewId();
        tracker.NotifyTransition(firstContext);
        PlayContextTransition? observed = null;
        tracker.Transitioned += transition => observed = transition;
        PlayContextId secondContext = PlayContextId.NewId();

        tracker.NotifyTransition(secondContext);

        Assert.NotNull(observed);
        Assert.Equal(firstContext, observed!.PreviousPlayContextId);
        Assert.Equal(secondContext, observed.NewPlayContextId);
    }

    /// <summary>Verifies that a transition with no subscribers does not throw.</summary>
    [Fact]
    public void NotifyTransition_NoSubscribers_DoesNotThrow()
    {
        var tracker = new PlayContextTracker();

        tracker.NotifyTransition(PlayContextId.NewId());
    }

    /// <summary>Verifies that play-context state does not survive a host restart: a fresh tracker starts with no current context.</summary>
    [Fact]
    public void NewTracker_AfterPriorTrackerTransitioned_StartsWithNoCurrentContext()
    {
        var priorTracker = new PlayContextTracker();
        priorTracker.NotifyTransition(PlayContextId.NewId());

        var restartedTracker = new PlayContextTracker();

        Assert.Null(restartedTracker.Current);
    }

    /// <summary>Verifies that every subscriber on Transitioned is invoked, not just the first.</summary>
    [Fact]
    public void NotifyTransition_MultipleSubscribers_InvokesAllOfThem()
    {
        var tracker = new PlayContextTracker();
        int firstSubscriberCallCount = 0;
        int secondSubscriberCallCount = 0;
        tracker.Transitioned += _ => firstSubscriberCallCount++;
        tracker.Transitioned += _ => secondSubscriberCallCount++;

        tracker.NotifyTransition(PlayContextId.NewId());

        Assert.Equal(1, firstSubscriberCallCount);
        Assert.Equal(1, secondSubscriberCallCount);
    }

    /// <summary>Verifies that concurrent transitions never lose or tear state: Current ends up as one of the notified values, and every transition still fires its event.</summary>
    [Fact]
    public async Task NotifyTransition_ConcurrentTransitions_NoLostOrTornUpdates()
    {
        var tracker = new PlayContextTracker();
        PlayContextId[] contexts = Enumerable.Range(0, 20).Select(_ => PlayContextId.NewId()).ToArray();
        int eventCount = 0;
        tracker.Transitioned += _ => Interlocked.Increment(ref eventCount);

        await Task.WhenAll(contexts.Select(context => Task.Run(() => tracker.NotifyTransition(context))));

        Assert.Equal(20, eventCount);
        Assert.Contains(tracker.Current!.Value, contexts);
    }
}
