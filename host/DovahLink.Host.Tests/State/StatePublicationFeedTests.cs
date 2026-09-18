using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for <see cref="StatePublicationFeed"/>.</summary>
public class StatePublicationFeedTests
{
    private static readonly StateAreaId AreaId = new("character_health");
    private static readonly JsonElement Data = JsonDocument.Parse("""{"value":93.4}""").RootElement;

    private static (StatePublicationFeed Feed, FakeAdapterAvailabilityTracker AdapterTracker, FakePlayContextTracker PlayContextTracker, RegisteredStateAreaPolicy RegisteredAreas, PlayContextId Context)
        CreateReadyFeed()
    {
        var playContextTracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        playContextTracker.NotifyTransition(context);
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var registeredAreas = new RegisteredStateAreaPolicy();
        registeredAreas.TryRegister(AreaId);
        var feed = new StatePublicationFeed(adapterTracker, playContextTracker, registeredAreas);
        return (feed, adapterTracker, playContextTracker, registeredAreas, context);
    }

    /// <summary>Verifies that a fresh, registered snapshot publishes: raises SnapshotChanged and becomes the value TryGetSnapshot returns.</summary>
    [Fact]
    public void PublishSnapshot_Fresh_RaisesSnapshotChangedAndUpdatesTryGetSnapshot()
    {
        (StatePublicationFeed feed, _, _, _, PlayContextId context) = CreateReadyFeed();
        StateSnapshotPublication? raised = null;
        feed.SnapshotChanged += publication => raised = publication;

        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

        Assert.NotNull(raised);
        Assert.Equal(AreaId, raised!.StateArea);
        Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
        Assert.Equal(raised, stored);
    }

    /// <summary>Verifies that a fresh, registered event publishes: raises EventOccurred and also becomes the value TryGetSnapshot returns, since an Event-mode area still answers baseline reads.</summary>
    [Fact]
    public void PublishEvent_Fresh_RaisesEventOccurredAndUpdatesTryGetSnapshot()
    {
        (StatePublicationFeed feed, _, _, _, PlayContextId context) = CreateReadyFeed();
        StateEventPublication? raised = null;
        feed.EventOccurred += publication => raised = publication;

        feed.PublishEvent(AreaId, RevisionNumber.Initial, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

        Assert.NotNull(raised);
        Assert.Equal(AreaId, raised!.StateArea);
        Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
        Assert.Equal(raised.Revision, stored!.Revision);
        Assert.Equal(raised.Data, stored.Data);
    }

    /// <summary>Verifies that publishing to an unregistered area is a silent no-op: no event, no stored value.</summary>
    [Fact]
    public void PublishSnapshot_AreaNotRegistered_DoesNothing()
    {
        var playContextTracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        playContextTracker.NotifyTransition(context);
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var feed = new StatePublicationFeed(adapterTracker, playContextTracker, new RegisteredStateAreaPolicy());
        bool raised = false;
        feed.SnapshotChanged += _ => raised = true;

        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

        Assert.False(raised);
        Assert.False(feed.TryGetSnapshot(AreaId, out _));
    }

    /// <summary>Verifies that publishing while the adapter is unavailable is a silent no-op.</summary>
    [Fact]
    public void PublishSnapshot_AdapterUnavailable_DoesNothing()
    {
        (StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, _, _, PlayContextId context) = CreateReadyFeed();
        adapterTracker.Current = AdapterAvailability.Unavailable;
        bool raised = false;
        feed.SnapshotChanged += _ => raised = true;

        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

        Assert.False(raised);
        Assert.False(feed.TryGetSnapshot(AreaId, out _));
    }

    /// <summary>
    /// Verifies that publishing while the adapter still needs resynchronization still raises
    /// SnapshotChanged (a legitimate baseline must always be able to land), but a pull read through
    /// TryGetSnapshot withholds that same value until resynchronization actually completes -- a pull
    /// must never hand out state that could still be superseded by the rest of an in-progress
    /// resynchronization transaction.
    /// </summary>
    [Fact]
    public void PublishSnapshot_AdapterNeedsResynchronization_StillPublishesButTryGetSnapshotWithholdsIt()
    {
        // A resynchronization baseline is only ever accepted by
        // IStatePublisher<TState>.ApplyResynchronizationBaseline while NeedsResynchronization is
        // true, so this feed must not require it clear before publishing -- otherwise a legitimate
        // baseline could never be published at all.
        (StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, _, _, PlayContextId context) = CreateReadyFeed();
        adapterTracker.NeedsResynchronization = true;
        bool raised = false;
        feed.SnapshotChanged += _ => raised = true;

        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

        Assert.True(raised);
        Assert.False(feed.TryGetSnapshot(AreaId, out _));

        adapterTracker.NeedsResynchronization = false;
        Assert.True(feed.TryGetSnapshot(AreaId, out _));
    }

    /// <summary>Verifies that a publish stamped with a play context other than the current one is a silent no-op, isolating one side of the freshness check's OR condition.</summary>
    [Fact]
    public void PublishSnapshot_StalePlayContextId_DoesNothing()
    {
        (StatePublicationFeed feed, _, FakePlayContextTracker playContextTracker, _, _) = CreateReadyFeed();
        bool raised = false;
        feed.SnapshotChanged += _ => raised = true;

        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, PlayContextId.NewId(), playContextTracker.TransitionGeneration, DateTimeOffset.UtcNow);

        Assert.False(raised);
        Assert.False(feed.TryGetSnapshot(AreaId, out _));
    }

    /// <summary>Verifies that a publish stamped with a play-context generation other than the current one is a silent no-op, isolating the other side of the freshness check's OR condition.</summary>
    [Fact]
    public void PublishSnapshot_StalePlayContextGeneration_DoesNothing()
    {
        (StatePublicationFeed feed, _, FakePlayContextTracker playContextTracker, _, PlayContextId context) = CreateReadyFeed();
        bool raised = false;
        feed.SnapshotChanged += _ => raised = true;

        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, playContextTracker.TransitionGeneration + 1, DateTimeOffset.UtcNow);

        Assert.False(raised);
        Assert.False(feed.TryGetSnapshot(AreaId, out _));
    }

    /// <summary>Verifies that a second publish for the same area replaces the value TryGetSnapshot returns and raises again with the new value.</summary>
    [Fact]
    public void PublishSnapshot_SecondPublish_ReplacesStoredValueAndRaisesAgain()
    {
        (StatePublicationFeed feed, _, _, _, PlayContextId context) = CreateReadyFeed();
        var secondData = JsonDocument.Parse("""{"value":50.0}""").RootElement;
        var raised = new List<StateSnapshotPublication>();
        feed.SnapshotChanged += publication => raised.Add(publication);

        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next().Next(), secondData, context, 1, DateTimeOffset.UtcNow);

        Assert.Equal(2, raised.Count);
        Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
        Assert.Equal(RevisionNumber.Initial.Next().Next(), stored!.Revision);
        Assert.Equal(secondData, stored.Data);
    }

    /// <summary>Verifies that reading a state area that was never published reports unavailable.</summary>
    [Fact]
    public void TryGetSnapshot_NeverPublished_ReturnsFalse()
    {
        (StatePublicationFeed feed, _, _, _, _) = CreateReadyFeed();

        Assert.False(feed.TryGetSnapshot(AreaId, out _));
    }

    /// <summary>Verifies that publishing to an unregistered area is a silent no-op for an event too, symmetric with <see cref="PublishSnapshot_AreaNotRegistered_DoesNothing"/>.</summary>
    [Fact]
    public void PublishEvent_AreaNotRegistered_DoesNothing()
    {
        var playContextTracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        playContextTracker.NotifyTransition(context);
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var feed = new StatePublicationFeed(adapterTracker, playContextTracker, new RegisteredStateAreaPolicy());
        bool raised = false;
        feed.EventOccurred += _ => raised = true;

        feed.PublishEvent(AreaId, RevisionNumber.Initial, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

        Assert.False(raised);
        Assert.False(feed.TryGetSnapshot(AreaId, out _));
    }

    /// <summary>Verifies that a stale play context is a silent no-op for an event too, symmetric with <see cref="PublishSnapshot_StalePlayContextId_DoesNothing"/> -- PublishEvent shares the same freshness gate as PublishSnapshot.</summary>
    [Fact]
    public void PublishEvent_StalePlayContextId_DoesNothing()
    {
        (StatePublicationFeed feed, _, FakePlayContextTracker playContextTracker, _, _) = CreateReadyFeed();
        bool raised = false;
        feed.EventOccurred += _ => raised = true;

        feed.PublishEvent(AreaId, RevisionNumber.Initial, RevisionNumber.Initial.Next(), Data, PlayContextId.NewId(), playContextTracker.TransitionGeneration, DateTimeOffset.UtcNow);

        Assert.False(raised);
        Assert.False(feed.TryGetSnapshot(AreaId, out _));
    }

    /// <summary>Verifies that two different state areas publish and read independently, without one affecting the other.</summary>
    [Fact]
    public void PublishSnapshot_TwoDifferentAreas_AreIndependent()
    {
        (StatePublicationFeed feed, _, _, RegisteredStateAreaPolicy registeredAreas, PlayContextId context) = CreateReadyFeed();
        var otherAreaId = new StateAreaId("character_magicka");
        registeredAreas.TryRegister(otherAreaId);
        var otherData = JsonDocument.Parse("""{"value":71.0}""").RootElement;

        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
        feed.PublishSnapshot(otherAreaId, RevisionNumber.Initial.Next(), otherData, context, 1, DateTimeOffset.UtcNow);

        Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? healthSnapshot));
        Assert.Equal(Data, healthSnapshot!.Data);
        Assert.True(feed.TryGetSnapshot(otherAreaId, out StateSnapshotPublication? magickaSnapshot));
        Assert.Equal(otherData, magickaSnapshot!.Data);
    }

    /// <summary>Verifies that one SnapshotChanged subscriber throwing does not prevent the stored value from updating or another subscriber from being invoked.</summary>
    [Fact]
    public void PublishSnapshot_SubscriberThrows_StoredValueStillUpdatesAndOtherSubscriberStillRuns()
    {
        (StatePublicationFeed feed, _, _, _, PlayContextId context) = CreateReadyFeed();
        bool otherSubscriberRan = false;
        feed.SnapshotChanged += _ => throw new InvalidOperationException("boom");
        feed.SnapshotChanged += _ => otherSubscriberRan = true;

        Exception? escaped = Record.Exception(() => feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow));

        Assert.Null(escaped);
        Assert.True(otherSubscriberRan);
        Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
        Assert.Equal(Data, stored!.Data);
    }

    /// <summary>
    /// Verifies that any adapter availability transition -- proactive defense in depth alongside
    /// TryGetSnapshot's own re-check -- clears every previously stored snapshot across every area, so
    /// a later TryGetSnapshot can never resurface pre-transition data even with no new publish
    /// attempt in between.
    /// </summary>
    [Fact]
    public void AvailabilityChanged_AfterSuccessfulPublish_ClearsStoredSnapshotsAcrossAllAreas()
    {
        (StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, _, RegisteredStateAreaPolicy registeredAreas, PlayContextId context) = CreateReadyFeed();
        var otherAreaId = new StateAreaId("character_magicka");
        registeredAreas.TryRegister(otherAreaId);
        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
        feed.PublishSnapshot(otherAreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
        Assert.True(feed.TryGetSnapshot(AreaId, out _));
        Assert.True(feed.TryGetSnapshot(otherAreaId, out _));

        adapterTracker.PublishTransition(new AdapterAvailabilityTransition(AdapterAvailability.Available, AdapterAvailability.Unavailable, null, 1));

        Assert.False(feed.TryGetSnapshot(AreaId, out _));
        Assert.False(feed.TryGetSnapshot(otherAreaId, out _));
    }

    /// <summary>
    /// Verifies that a play-context transition -- proactive defense in depth alongside
    /// TryGetSnapshot's own re-check -- clears a previously stored snapshot, so a later
    /// TryGetSnapshot can never resurface pre-transition data even with no new publish attempt in
    /// between.
    /// </summary>
    [Fact]
    public void PlayContextTransitioned_AfterSuccessfulPublish_ClearsStoredSnapshot()
    {
        (StatePublicationFeed feed, _, FakePlayContextTracker playContextTracker, _, PlayContextId context) = CreateReadyFeed();
        feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
        Assert.True(feed.TryGetSnapshot(AreaId, out _));

        playContextTracker.NotifyTransition(PlayContextId.NewId());

        Assert.False(feed.TryGetSnapshot(AreaId, out _));
    }
}
