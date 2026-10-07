using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests that <see cref="StatePublicationFeed"/> is a stateless view over <see cref="IAuthoritativeStateStore"/>.</summary>
public class StatePublicationFeedTests
{
    /// <summary>The area these tests write.</summary>
    private const string Area = "area_a";

    /// <summary>Verifies that a read through the feed returns exactly the store's current Snapshot and tracks later changes without any copy of its own.</summary>
    [Fact]
    public void TryGetSnapshot_ReflectsStoreStateWithoutOwnCopy()
    {
        var rig = new AuthoritativeStateStoreRig(Area);
        var feed = new StatePublicationFeed(rig.Store);
        Assert.False(feed.TryGetSnapshot(new StateAreaId(Area), out _));

        rig.Apply(Area, 1);
        Assert.True(feed.TryGetSnapshot(new StateAreaId(Area), out StateSnapshotPublication? first));
        Assert.Equal(rig.Snapshot(Area), first);

        rig.Apply(Area, 2);
        Assert.True(feed.TryGetSnapshot(new StateAreaId(Area), out StateSnapshotPublication? second));
        Assert.Equal(RevisionNumber.Initial.Next().Next(), second!.Revision);

        rig.LoseContinuity();
        Assert.False(feed.TryGetSnapshot(new StateAreaId(Area), out _));
    }

    /// <summary>Verifies that subscribing through the feed receives the store's publications, and unsubscribing stops them.</summary>
    [Fact]
    public void Events_SubscribeAndUnsubscribe_ForwardToStore()
    {
        var rig = new AuthoritativeStateStoreRig(Area, "area_b");
        var feed = new StatePublicationFeed(rig.Store);
        var snapshots = new List<StateSnapshotPublication>();
        var events = new List<StateEventPublication>();
        int hints = 0;
        Action<StateSnapshotPublication> onSnapshot = snapshots.Add;
        Action<StateEventPublication> onEvent = events.Add;
        Action onHint = () => hints++;
        feed.SnapshotChanged += onSnapshot;
        feed.EventOccurred += onEvent;
        feed.SnapshotAvailabilityChanged += onHint;

        rig.Apply(Area, 1);
        rig.ApplyEvent("area_b", 2);
        rig.Reconnect();
        rig.Baseline(Area, 1);
        rig.CompleteResynchronization();

        Assert.Single(snapshots);
        Assert.Single(events);
        Assert.Equal(1, hints);

        feed.SnapshotChanged -= onSnapshot;
        feed.EventOccurred -= onEvent;
        feed.SnapshotAvailabilityChanged -= onHint;
        rig.Apply(Area, 3);
        rig.ApplyEvent("area_b", 4);

        Assert.Single(snapshots);
        Assert.Single(events);

        rig.LoseContinuity();
        rig.Reconnect();
        rig.Baseline(Area, 3);
        rig.CompleteResynchronization();

        Assert.Equal(1, hints);
    }

    /// <summary>Verifies that the boundary baseline is produced by the store, so it is revision zero and never stored.</summary>
    [Fact]
    public void CreateUnavailableBoundaryBaseline_DelegatesToStore()
    {
        var rig = new AuthoritativeStateStoreRig(Area);
        var feed = new StatePublicationFeed(rig.Store);

        StateSnapshotPublication baseline = feed.CreateUnavailableBoundaryBaseline(
            new StateAreaId(Area), rig.PlayContextTracker.GetSnapshot(), AuthoritativeStateStoreRig.At);

        Assert.Equal(RevisionNumber.Initial, baseline.Revision);
        Assert.Equal(new StateAreaId(Area), baseline.StateArea);
        Assert.Equal(AuthoritativeStateStoreRig.At, baseline.OccurredAt);
        Assert.Equal(rig.ContextId, baseline.PlayContextId);
        Assert.False(feed.TryGetSnapshot(new StateAreaId(Area), out _));
    }
}
