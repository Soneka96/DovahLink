using DovahLink.Host.Identity;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for <see cref="RevisionTracker"/>.</summary>
public class RevisionTrackerTests
{
    /// <summary>Verifies that a state area that has never changed reports the initial revision.</summary>
    [Fact]
    public void Current_NeverAdvanced_ReturnsInitial()
    {
        var tracker = new RevisionTracker();

        RevisionNumber current = tracker.Current(PlayContextId.NewId(), new StateAreaId("Character"));

        Assert.Equal(RevisionNumber.Initial, current);
    }

    /// <summary>Verifies that advancing a state area's revision moves it past the initial revision, and Current reflects it.</summary>
    [Fact]
    public void AdvanceOnChange_FirstChange_MovesPastInitialAndUpdatesCurrent()
    {
        var tracker = new RevisionTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        var areaId = new StateAreaId("Character");

        RevisionNumber advanced = tracker.AdvanceOnChange(playContextId, areaId);

        Assert.Equal(RevisionNumber.Initial.Next(), advanced);
        Assert.Equal(advanced, tracker.Current(playContextId, areaId));
    }

    /// <summary>Verifies that repeated advances increment sequentially.</summary>
    [Fact]
    public void AdvanceOnChange_CalledRepeatedly_IncrementsSequentially()
    {
        var tracker = new RevisionTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        var areaId = new StateAreaId("Character");

        tracker.AdvanceOnChange(playContextId, areaId);
        tracker.AdvanceOnChange(playContextId, areaId);
        RevisionNumber third = tracker.AdvanceOnChange(playContextId, areaId);

        Assert.Equal(3UL, third.Value);
    }

    /// <summary>Verifies that distinct state areas within the same play context are tracked independently.</summary>
    [Fact]
    public void AdvanceOnChange_DifferentAreasSameContext_TrackedIndependently()
    {
        var tracker = new RevisionTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        var characterArea = new StateAreaId("Character");
        var inventoryArea = new StateAreaId("Inventory");

        tracker.AdvanceOnChange(playContextId, characterArea);

        Assert.Equal(RevisionNumber.Initial.Next(), tracker.Current(playContextId, characterArea));
        Assert.Equal(RevisionNumber.Initial, tracker.Current(playContextId, inventoryArea));
    }

    /// <summary>Verifies that the same state area under distinct play contexts is tracked independently.</summary>
    [Fact]
    public void AdvanceOnChange_SameAreaDifferentContexts_TrackedIndependently()
    {
        var tracker = new RevisionTracker();
        var areaId = new StateAreaId("Character");
        PlayContextId firstContext = PlayContextId.NewId();
        PlayContextId secondContext = PlayContextId.NewId();

        tracker.AdvanceOnChange(firstContext, areaId);

        Assert.Equal(RevisionNumber.Initial.Next(), tracker.Current(firstContext, areaId));
        Assert.Equal(RevisionNumber.Initial, tracker.Current(secondContext, areaId));
    }

    /// <summary>Verifies that invalidating a play context resets every one of its state areas back to the initial revision.</summary>
    [Fact]
    public void InvalidateContext_ResetsAllOfThatContextsAreasToInitial()
    {
        var tracker = new RevisionTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        var characterArea = new StateAreaId("Character");
        var inventoryArea = new StateAreaId("Inventory");
        tracker.AdvanceOnChange(playContextId, characterArea);
        tracker.AdvanceOnChange(playContextId, inventoryArea);

        tracker.InvalidateContext(playContextId);

        Assert.Equal(RevisionNumber.Initial, tracker.Current(playContextId, characterArea));
        Assert.Equal(RevisionNumber.Initial, tracker.Current(playContextId, inventoryArea));
    }

    /// <summary>Verifies that invalidating one play context does not affect another's revisions.</summary>
    [Fact]
    public void InvalidateContext_DoesNotAffectOtherContexts()
    {
        var tracker = new RevisionTracker();
        var areaId = new StateAreaId("Character");
        PlayContextId invalidatedContext = PlayContextId.NewId();
        PlayContextId otherContext = PlayContextId.NewId();
        tracker.AdvanceOnChange(invalidatedContext, areaId);
        RevisionNumber otherContextRevision = tracker.AdvanceOnChange(otherContext, areaId);

        tracker.InvalidateContext(invalidatedContext);

        Assert.Equal(otherContextRevision, tracker.Current(otherContext, areaId));
    }

    /// <summary>Verifies that invalidating a play context with no tracked areas at all is a harmless no-op.</summary>
    [Fact]
    public void InvalidateContext_NoTrackedAreas_DoesNotThrow()
    {
        var tracker = new RevisionTracker();

        tracker.InvalidateContext(PlayContextId.NewId());
    }

    /// <summary>Verifies that a state area can advance again after its context was invalidated, starting from the initial revision.</summary>
    [Fact]
    public void AdvanceOnChange_AfterInvalidateContext_StartsFromInitialAgain()
    {
        var tracker = new RevisionTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        var areaId = new StateAreaId("Character");
        tracker.AdvanceOnChange(playContextId, areaId);
        tracker.InvalidateContext(playContextId);

        RevisionNumber advanced = tracker.AdvanceOnChange(playContextId, areaId);

        Assert.Equal(RevisionNumber.Initial.Next(), advanced);
    }

    /// <summary>Verifies that revisions do not survive a host restart: a fresh tracker starts with every area at the initial revision.</summary>
    [Fact]
    public void NewTracker_AfterPriorTrackerAdvanced_StartsAtInitial()
    {
        var priorTracker = new RevisionTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        var areaId = new StateAreaId("Character");
        priorTracker.AdvanceOnChange(playContextId, areaId);

        var restartedTracker = new RevisionTracker();

        Assert.Equal(RevisionNumber.Initial, restartedTracker.Current(playContextId, areaId));
    }

    /// <summary>Verifies that invalidating a context a second time is a harmless no-op.</summary>
    [Fact]
    public void InvalidateContext_CalledTwice_StaysAtInitial()
    {
        var tracker = new RevisionTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        var areaId = new StateAreaId("Character");
        tracker.AdvanceOnChange(playContextId, areaId);

        tracker.InvalidateContext(playContextId);
        tracker.InvalidateContext(playContextId);

        Assert.Equal(RevisionNumber.Initial, tracker.Current(playContextId, areaId));
    }

    /// <summary>Verifies that concurrent advances on the same state area never lose an increment.</summary>
    [Fact]
    public async Task AdvanceOnChange_ConcurrentAdvancesOnSameArea_NeverLosesAnIncrement()
    {
        var tracker = new RevisionTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        var areaId = new StateAreaId("Character");

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => tracker.AdvanceOnChange(playContextId, areaId))));

        Assert.Equal(50UL, tracker.Current(playContextId, areaId).Value);
    }
}
