using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for <see cref="AuthoritativeStateStore"/>, using invented areas and value types so no production domain is assumed.</summary>
public class AuthoritativeStateStoreTests
{
    /// <summary>The Snapshot-style area most tests write.</summary>
    private const string Area = "area_a";

    /// <summary>A second area, used to prove independence and distinct value types.</summary>
    private const string OtherArea = "area_b";

    /// <summary>Builds a rig with both invented areas registered.</summary>
    private static AuthoritativeStateStoreRig BuildRig() => new(Area, OtherArea);

    /// <summary>Reads the integer carried by a Snapshot's <c>{ value }</c> data.</summary>
    /// <param name="snapshot">The Snapshot to read.</param>
    private static int ValueOf(StateSnapshotPublication snapshot) => snapshot.Data.GetProperty("value").GetInt32();

    /// <summary>Verifies that the first value becomes revision one and is immediately replayable as a complete Snapshot.</summary>
    [Fact]
    public void Apply_FirstValue_BecomesRevisionOneAndReplayable()
    {
        AuthoritativeStateStoreRig rig = BuildRig();

        StateApplyResult result = rig.Apply(Area, 42);

        Assert.Equal(new StateApplyResult(true, true, RevisionNumber.Initial, RevisionNumber.Initial.Next()), result);
        StateSnapshotPublication snapshot = rig.Snapshot(Area);
        Assert.Equal(RevisionNumber.Initial.Next(), snapshot.Revision);
        Assert.Equal(42, ValueOf(snapshot));
        Assert.Equal(rig.ContextId, snapshot.PlayContextId);
        Assert.Equal(rig.PlayContextTracker.TransitionGeneration, snapshot.PlayContextGeneration);
        Assert.Equal(AuthoritativeStateStoreRig.At, snapshot.OccurredAt);
        Assert.True(rig.Store.TryGetCurrentValue(new StateAreaId(Area), out int typed));
        Assert.Equal(42, typed);
    }

    /// <summary>Verifies that re-applying the same value keeps the revision, the replay Snapshot, and its capture time, and publishes nothing.</summary>
    [Fact]
    public void Apply_SameValue_DoesNotAdvanceRevisionOrPublish()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        StateSnapshotPublication before = rig.Snapshot(Area);
        int raised = 0;
        rig.Store.SnapshotChanged += _ => raised++;
        rig.Store.EventOccurred += _ => raised++;

        StateApplyResult result = rig.Apply(Area, 42, AuthoritativeStateStoreRig.At.AddMinutes(5));

        Assert.True(result.Accepted);
        Assert.False(result.Changed);
        Assert.Equal(before.Revision, result.Revision);
        Assert.Equal(before.Revision, result.BaseRevision);
        Assert.Equal(before, rig.Snapshot(Area));
        Assert.Equal(0, raised);
    }

    /// <summary>Verifies that a changed value advances the revision by exactly one and replaces the replay Snapshot.</summary>
    [Fact]
    public void Apply_ChangedValue_AdvancesRevisionAndReplacesSnapshot()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);

        StateApplyResult result = rig.Apply(Area, 43, AuthoritativeStateStoreRig.At.AddMinutes(1));

        Assert.True(result.Changed);
        Assert.Equal(RevisionNumber.Initial.Next(), result.BaseRevision);
        Assert.Equal(RevisionNumber.Initial.Next().Next(), result.Revision);
        StateSnapshotPublication snapshot = rig.Snapshot(Area);
        Assert.Equal(43, ValueOf(snapshot));
        Assert.Equal(result.Revision, snapshot.Revision);
        Assert.Equal(AuthoritativeStateStoreRig.At.AddMinutes(1), snapshot.OccurredAt);
    }

    /// <summary>Verifies that a Snapshot-mode change raises only <see cref="IAuthoritativeStateStore.SnapshotChanged"/>, carrying the committed state.</summary>
    [Fact]
    public void Apply_Changed_RaisesOnlySnapshotChanged()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        var snapshots = new List<StateSnapshotPublication>();
        var events = new List<StateEventPublication>();
        rig.Store.SnapshotChanged += snapshots.Add;
        rig.Store.EventOccurred += events.Add;

        rig.Apply(Area, 42);

        StateSnapshotPublication published = Assert.Single(snapshots);
        Assert.Empty(events);
        Assert.Equal(rig.Snapshot(Area), published);
    }

    /// <summary>Verifies that an Event-mode change raises only <see cref="IAuthoritativeStateStore.EventOccurred"/> with base and new revision, and the state stays replayable as a Snapshot.</summary>
    [Fact]
    public void ApplyEvent_Changed_RaisesOnlyEventAndStateIsReplayableAsSnapshot()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 43);
        var snapshots = new List<StateSnapshotPublication>();
        var events = new List<StateEventPublication>();
        rig.Store.SnapshotChanged += snapshots.Add;
        rig.Store.EventOccurred += events.Add;

        StateApplyResult result = rig.ApplyEvent(Area, 44);

        StateEventPublication published = Assert.Single(events);
        Assert.Empty(snapshots);
        Assert.Equal(RevisionNumber.Initial.Next(), published.BaseRevision);
        Assert.Equal(RevisionNumber.Initial.Next().Next(), published.Revision);
        Assert.Equal(result.Revision, published.Revision);
        Assert.Equal(44, published.Data.GetProperty("value").GetInt32());
        StateSnapshotPublication replay = rig.Snapshot(Area);
        Assert.Equal(published.Revision, replay.Revision);
        Assert.Equal(44, ValueOf(replay));
    }

    /// <summary>Verifies that an Event carrying an unchanged value publishes nothing and keeps the revision.</summary>
    [Fact]
    public void ApplyEvent_SameValue_PublishesNothing()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.ApplyEvent(Area, 44);
        int raised = 0;
        rig.Store.EventOccurred += _ => raised++;

        StateApplyResult result = rig.ApplyEvent(Area, 44);

        Assert.True(result.Accepted);
        Assert.False(result.Changed);
        Assert.Equal(0, raised);
    }

    /// <summary>Verifies that a subscriber already observes the committed replay Snapshot and revision, for both publication kinds.</summary>
    [Fact]
    public void Publication_Subscriber_SeesCurrentSnapshotAlreadyCommitted()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        StateSnapshotPublication? seenDuringSnapshot = null;
        StateSnapshotPublication? seenDuringEvent = null;
        rig.Store.SnapshotChanged += _ => rig.Store.TryGetSnapshot(new StateAreaId(Area), out seenDuringSnapshot);
        rig.Store.EventOccurred += _ => rig.Store.TryGetSnapshot(new StateAreaId(OtherArea), out seenDuringEvent);

        rig.Apply(Area, 1);
        rig.ApplyEvent(OtherArea, 2);

        Assert.Equal(RevisionNumber.Initial.Next(), seenDuringSnapshot!.Revision);
        Assert.Equal(1, ValueOf(seenDuringSnapshot));
        Assert.Equal(RevisionNumber.Initial.Next(), seenDuringEvent!.Revision);
        Assert.Equal(2, ValueOf(seenDuringEvent));
    }

    /// <summary>Verifies that a failing subscriber neither blocks the next subscriber nor undoes the commit.</summary>
    [Fact]
    public void Publication_SubscriberThrows_StateStaysCommittedAndOtherSubscriberRuns()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        bool secondRan = false;
        rig.Store.SnapshotChanged += _ => throw new InvalidOperationException("subscriber failure");
        rig.Store.SnapshotChanged += _ => secondRan = true;

        StateApplyResult result = rig.Apply(Area, 7);

        Assert.True(result.Accepted);
        Assert.True(secondRan);
        Assert.Equal(7, ValueOf(rig.Snapshot(Area)));
    }

    /// <summary>Verifies that distinct areas hold independent values and revisions.</summary>
    [Fact]
    public void Apply_DistinctAreas_AreIndependent()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 1);
        rig.Apply(Area, 2);
        rig.Apply(OtherArea, "text");

        Assert.Equal(RevisionNumber.Initial.Next().Next(), rig.Store.CurrentRevision(new StateAreaId(Area)));
        Assert.Equal(RevisionNumber.Initial.Next(), rig.Store.CurrentRevision(new StateAreaId(OtherArea)));
    }

    /// <summary>Verifies that areas may hold unrelated value types, including nullable value types and records, each round-tripping through its typed read.</summary>
    [Fact]
    public void Apply_MultipleValueTypes_EachRoundTripsThroughTypedRead()
    {
        var rig = new AuthoritativeStateStoreRig("text", "number", "pair", "missing");

        rig.Apply("text", (string?)"alpha");
        rig.Apply("number", (ushort?)43);
        rig.Apply("pair", new KeyValuePair<string, int>("k", 9));
        rig.Apply("missing", (int?)null);

        Assert.True(rig.Store.TryGetCurrentValue(new StateAreaId("text"), out string? text));
        Assert.Equal("alpha", text);
        Assert.True(rig.Store.TryGetCurrentValue(new StateAreaId("number"), out ushort? number));
        Assert.Equal((ushort?)43, number);
        Assert.True(rig.Store.TryGetCurrentValue(new StateAreaId("pair"), out KeyValuePair<string, int> pair));
        Assert.Equal(9, pair.Value);
        Assert.True(rig.Store.TryGetCurrentValue(new StateAreaId("missing"), out int? missing));
        Assert.Null(missing);
        Assert.True(rig.Snapshot("missing").Data.GetProperty("value").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    /// <summary>Verifies that an explicit null value is a first-class value: applying it twice is unchanged, and changing it to a value advances the revision.</summary>
    [Fact]
    public void Apply_NullableValue_NullIsComparedLikeAnyOtherValue()
    {
        var rig = new AuthoritativeStateStoreRig(Area);

        Assert.True(rig.Apply(Area, (int?)null).Changed);
        Assert.False(rig.Apply(Area, (int?)null).Changed);
        Assert.True(rig.Apply(Area, (int?)5).Changed);
    }

    /// <summary>Verifies that using one area with a second value type fails closed for every write path and for typed reads, and still does after a play-context transition.</summary>
    [Fact]
    public void IncompatibleValueType_FailsClosedOnEveryPath()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 1);

        Assert.Throws<InvalidOperationException>(() => rig.Apply(Area, "text"));
        Assert.Throws<InvalidOperationException>(() => rig.ApplyEvent(Area, "text"));
        Assert.Throws<InvalidOperationException>(() => rig.Store.TryGetCurrentValue(new StateAreaId(Area), out string? _));
        rig.PlayContextTracker.NotifyTransition(PlayContextId.NewId());
        Assert.Throws<InvalidOperationException>(() => rig.Store.ApplyEvent(
            rig.Adapter.CurrentInstanceId!.Value, rig.Adapter.CurrentConnectionGeneration, rig.PlayContextTracker.Current!.Value,
            rig.PlayContextTracker.TransitionGeneration, null, AuthoritativeStateStoreRig.At, new StateAreaId(Area), "text"));
    }

    /// <summary>Verifies that a baseline cannot rebind an area to another value type either.</summary>
    [Fact]
    public void IncompatibleValueType_BaselineFailsClosed()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 1);
        rig.Reconnect();

        Assert.Throws<InvalidOperationException>(() => rig.Baseline(Area, "text"));
    }

    /// <summary>Verifies that an adapter continuity loss makes every area unreadable and advances each populated revision once, while the typed value is retained internally.</summary>
    [Fact]
    public void ContinuityLoss_GatesReadsAndAdvancesRevision()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        rig.Apply(OtherArea, 1);

        rig.LoseContinuity();

        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(OtherArea), out _));
        Assert.False(rig.Store.TryGetCurrentValue(new StateAreaId(Area), out int _));
        Assert.Equal(RevisionNumber.Initial.Next().Next(), rig.Store.CurrentRevision(new StateAreaId(Area)));
    }

    /// <summary>Verifies that a reconnect gates reads until resynchronization completes, even though state was committed during the gate.</summary>
    [Fact]
    public void Reconnect_StateCommittedDuringResynchronization_IsWithheldUntilItCompletes()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        rig.LoseContinuity();
        rig.Reconnect();

        Assert.True(rig.Baseline(Area, 42).Accepted);
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));

        rig.CompleteResynchronization();

        Assert.True(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
    }

    /// <summary>
    /// Verifies same-value resynchronization: after continuity loss, a baseline equal to the retained
    /// value restores replay under fresh provenance and capture time, creates no value revision, and
    /// raises no change notification -- only the availability hint on completion.
    /// </summary>
    [Fact]
    public void SameValueResynchronization_RestoresReplayWithoutValueRevisionOrChangeNotification()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        long firstGeneration = rig.Adapter.CurrentConnectionGeneration;
        rig.LoseContinuity();
        rig.Reconnect();
        RevisionNumber revisionAfterLoss = rig.Store.CurrentRevision(new StateAreaId(Area));
        int changes = 0;
        int availabilityHints = 0;
        rig.Store.SnapshotChanged += _ => changes++;
        rig.Store.EventOccurred += _ => changes++;
        rig.Store.SnapshotAvailabilityChanged += () => availabilityHints++;
        DateTimeOffset baselineTime = AuthoritativeStateStoreRig.At.AddMinutes(10);

        StateApplyResult result = rig.Baseline(Area, 42, occurredAt: baselineTime);
        rig.CompleteResynchronization();

        Assert.True(result.Accepted);
        Assert.False(result.Changed);
        Assert.Equal(revisionAfterLoss, result.Revision);
        Assert.Equal(revisionAfterLoss, result.BaseRevision);
        Assert.True(rig.Adapter.CurrentConnectionGeneration > firstGeneration);
        StateSnapshotPublication replay = rig.Snapshot(Area);
        Assert.Equal(42, ValueOf(replay));
        Assert.Equal(revisionAfterLoss, replay.Revision);
        Assert.Equal(baselineTime, replay.OccurredAt);
        Assert.Equal(0, changes);
        Assert.Equal(1, availabilityHints);
    }

    /// <summary>Verifies that a resynchronization baseline whose value differs is a normal change announced in the requested mode.</summary>
    [Theory]
    [InlineData(UpdateMode.Snapshot)]
    [InlineData(UpdateMode.Event)]
    public void ChangedResynchronizationBaseline_IsAnnouncedInRequestedMode(UpdateMode mode)
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        rig.LoseContinuity();
        rig.Reconnect();
        var snapshots = new List<StateSnapshotPublication>();
        var events = new List<StateEventPublication>();
        rig.Store.SnapshotChanged += snapshots.Add;
        rig.Store.EventOccurred += events.Add;

        StateApplyResult result = rig.Baseline(Area, 43, mode);
        rig.CompleteResynchronization();

        Assert.True(result.Changed);
        Assert.Equal(mode == UpdateMode.Snapshot ? 1 : 0, snapshots.Count);
        Assert.Equal(mode == UpdateMode.Event ? 1 : 0, events.Count);
        Assert.Equal(43, ValueOf(rig.Snapshot(Area)));
        Assert.Equal(result.Revision, rig.Snapshot(Area).Revision);
    }

    /// <summary>
    /// Verifies that an unchanged ordinary capture re-establishes replay for an area that lost
    /// currentness but was not part of the completed resynchronization, with no change notification --
    /// an accepted capture under current authority is authoritative by definition.
    /// </summary>
    [Fact]
    public void UnchangedOrdinaryCapture_AfterResynchronizationWithoutBaseline_RestoresReplay()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        rig.LoseContinuity();
        rig.Reconnect();
        rig.CompleteResynchronization();
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
        int changes = 0;
        rig.Store.SnapshotChanged += _ => changes++;

        StateApplyResult result = rig.Apply(Area, 42, AuthoritativeStateStoreRig.At.AddMinutes(3));

        Assert.False(result.Changed);
        Assert.Equal(AuthoritativeStateStoreRig.At.AddMinutes(3), rig.Snapshot(Area).OccurredAt);
        Assert.Equal(0, changes);
    }

    /// <summary>Verifies that a play-context transition invalidates the old context's state and restarts revisions at one, never replaying old state under the new context.</summary>
    [Fact]
    public void PlayContextTransition_InvalidatesOldStateAndRestartsRevisions()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        rig.Apply(Area, 43);
        var newContext = PlayContextId.NewId();

        rig.PlayContextTracker.NotifyTransition(newContext);

        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
        Assert.False(rig.Store.TryGetCurrentValue(new StateAreaId(Area), out int _));
        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(Area)));
        StateApplyResult result = rig.Store.Apply(
            rig.Adapter.CurrentInstanceId!.Value, rig.Adapter.CurrentConnectionGeneration, newContext,
            rig.PlayContextTracker.TransitionGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 43);
        Assert.True(result.Changed);
        Assert.Equal(RevisionNumber.Initial.Next(), result.Revision);
        Assert.Equal(newContext, rig.Snapshot(Area).PlayContextId);
    }

    /// <summary>Verifies that a capture accepted under the new context before a delayed transition handler runs survives that handler's cleanup of the prior context.</summary>
    [Fact]
    public async Task Apply_DuringDelayedPlayContextTransition_PreservesNewContextState()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 1);
        var secondContext = PlayContextId.NewId();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.PlayContextTracker.Transitioned += transition =>
        {
            if (transition.NewPlayContextId == secondContext)
            {
                entered.Set();
                release.Wait();
            }
        };

        Task transitionTask = Task.Run(() => rig.PlayContextTracker.NotifyTransition(secondContext));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        StateApplyResult result = rig.Store.Apply(
            rig.Adapter.CurrentInstanceId!.Value, rig.Adapter.CurrentConnectionGeneration, secondContext,
            rig.PlayContextTracker.TransitionGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 2);
        release.Set();
        await transitionTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Accepted);
        Assert.Equal(2, ValueOf(rig.Snapshot(Area)));
        Assert.Equal(RevisionNumber.Initial.Next(), rig.Snapshot(Area).Revision);
    }

    /// <summary>Verifies that an availability transition during a delayed play-context transition cannot create a revision in the new context.</summary>
    [Fact]
    public async Task AdapterAvailabilityDuringDelayedPlayContextTransition_DoesNotCreateGhostRevision()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 1);
        var secondContext = PlayContextId.NewId();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.PlayContextTracker.Transitioned += transition =>
        {
            if (transition.NewPlayContextId == secondContext)
            {
                entered.Set();
                release.Wait();
            }
        };

        Task transitionTask = Task.Run(() => rig.PlayContextTracker.NotifyTransition(secondContext));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        rig.LoseContinuity();

        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(Area)));
        release.Set();
        await transitionTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(Area)));
    }

    /// <summary>Verifies that a rotated state authority hides the stored state even before anything else changes.</summary>
    [Fact]
    public void AuthorityRotation_HidesStoredState()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);

        rig.Authority.NotifyRotated();

        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
    }

    /// <summary>Verifies that every committed Snapshot carries the authority that was current at commit time.</summary>
    [Fact]
    public void Apply_StampsCurrentStateAuthority()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        StateAuthorityId before = rig.Snapshot(Area).StateAuthorityId;

        rig.Authority.NotifyRotated();
        rig.LoseContinuity();
        rig.Reconnect();
        rig.Baseline(Area, 42);
        rig.CompleteResynchronization();

        Assert.NotEqual(before, rig.Snapshot(Area).StateAuthorityId);
    }

    /// <summary>Verifies that a state authority rotating while a value is being committed rejects the commit instead of stamping the old authority.</summary>
    [Fact]
    public void Apply_AuthorityRotatesDuringCommit_IsRejected()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Authority.OnCurrentRead = rig.Authority.NotifyRotated;

        StateApplyResult result = rig.Apply(Area, 42);

        Assert.Equal(StateApplyResult.Rejected, result);
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
    }

    /// <summary>Verifies the boundary baseline is revision zero, is never stored, and leaves the first real capture at revision one.</summary>
    [Fact]
    public void CreateUnavailableBoundaryBaseline_IsRevisionZeroAndDoesNotConsumeFirstRevision()
    {
        AuthoritativeStateStoreRig rig = BuildRig();

        StateSnapshotPublication baseline = rig.Store.CreateUnavailableBoundaryBaseline(
            new StateAreaId(Area), rig.PlayContextTracker.GetSnapshot(), AuthoritativeStateStoreRig.At);

        Assert.Equal(RevisionNumber.Initial, baseline.Revision);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, baseline.Data.GetProperty("value").ValueKind);
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(Area)));
        Assert.Equal(RevisionNumber.Initial.Next(), rig.Apply(Area, 42).Revision);
    }

    /// <summary>Verifies the boundary baseline preserves a null play context and rejects unregistered areas.</summary>
    [Fact]
    public void CreateUnavailableBoundaryBaseline_NullContextPreservedAndUnregisteredAreaThrows()
    {
        AuthoritativeStateStoreRig rig = BuildRig();

        StateSnapshotPublication baseline = rig.Store.CreateUnavailableBoundaryBaseline(
            new StateAreaId(Area), new PlayContextSnapshot(null, 3), AuthoritativeStateStoreRig.At);

        Assert.Null(baseline.PlayContextId);
        Assert.Equal(3, baseline.PlayContextGeneration);
        Assert.Throws<ArgumentException>(() => rig.Store.CreateUnavailableBoundaryBaseline(
            new StateAreaId("unregistered"), rig.PlayContextTracker.GetSnapshot(), AuthoritativeStateStoreRig.At));
    }

    /// <summary>Verifies that applying before any play context exists fails loudly rather than storing under no context.</summary>
    [Fact]
    public void Apply_NoPlayContextYet_Throws()
    {
        var adapter = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var registered = new RegisteredStateAreaPolicy();
        registered.TryRegister(new StateAreaId(Area));
        var store = new AuthoritativeStateStore(adapter, new FakePlayContextTracker(), registered, new FakeStateAuthorityLifecycle());

        Assert.Throws<InvalidOperationException>(() => store.Apply(
            adapter.CurrentInstanceId!.Value, adapter.CurrentConnectionGeneration, PlayContextId.NewId(), 0,
            AuthoritativeStateStoreRig.At, new StateAreaId(Area), 1));
        Assert.False(store.TryGetSnapshot(new StateAreaId(Area), out _));
        Assert.Equal(RevisionNumber.Initial, store.CurrentRevision(new StateAreaId(Area)));
    }

    /// <summary>Verifies the rejection matrix: each stale or unauthorized write returns the fixed rejected result and leaves no state behind.</summary>
    [Theory]
    [InlineData("staleInstance")]
    [InlineData("staleConnectionGeneration")]
    [InlineData("stalePlayContextId")]
    [InlineData("stalePlayContextGeneration")]
    [InlineData("adapterUnavailable")]
    [InlineData("ordinaryWhileResynchronizationRequired")]
    [InlineData("unregisteredArea")]
    public void Apply_Rejections_LeaveNoState(string scenario)
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        AdapterInstanceId instance = rig.Adapter.CurrentInstanceId!.Value;
        long generation = rig.Adapter.CurrentConnectionGeneration;
        PlayContextId context = rig.ContextId;
        long contextGeneration = rig.PlayContextTracker.TransitionGeneration;
        string area = Area;
        switch (scenario)
        {
            case "staleInstance": instance = AdapterInstanceId.NewId(); break;
            case "staleConnectionGeneration": generation++; break;
            case "stalePlayContextId": context = PlayContextId.NewId(); break;
            case "stalePlayContextGeneration": contextGeneration++; break;
            case "adapterUnavailable": rig.Adapter.Current = AdapterAvailability.Unavailable; break;
            case "ordinaryWhileResynchronizationRequired": rig.Adapter.NeedsResynchronization = true; break;
            case "unregisteredArea": area = "unregistered"; break;
        }

        StateApplyResult result = rig.Store.Apply(instance, generation, context, contextGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(area), 42);

        Assert.Equal(StateApplyResult.Rejected, result);
        rig.Adapter.Current = AdapterAvailability.Available;
        rig.Adapter.NeedsResynchronization = false;
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(area), out _));
        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(area)));
    }

    /// <summary>Verifies the baseline rejection matrix: outside a resynchronization, with a foreign token, or with stale play-context provenance, or for an unregistered area. A rejected baseline never runs the commit hook.</summary>
    [Theory]
    [InlineData("outsideResynchronization")]
    [InlineData("foreignToken")]
    [InlineData("stalePlayContextId")]
    [InlineData("stalePlayContextGeneration")]
    [InlineData("unregisteredArea")]
    public void ApplyResynchronizationBaseline_Rejections_LeaveNoState(string scenario)
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Reconnect();
        IAdapterResynchronizationToken token = rig.Adapter.TryClaimResynchronizationToken()!;
        PlayContextId context = rig.ContextId;
        long contextGeneration = rig.PlayContextTracker.TransitionGeneration;
        string area = Area;
        bool committed = false;
        switch (scenario)
        {
            case "unregisteredArea": area = "unregistered"; break;
            case "outsideResynchronization": rig.Adapter.NeedsResynchronization = false; break;
            case "foreignToken": token = new ForeignToken(); break;
            case "stalePlayContextId": context = PlayContextId.NewId(); break;
            case "stalePlayContextGeneration": contextGeneration++; break;
        }

        StateApplyResult result = rig.Store.ApplyResynchronizationBaseline(
            UpdateMode.Snapshot, token, context, contextGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(area), 42,
            onCommitted: () => committed = true);

        Assert.Equal(StateApplyResult.Rejected, result);
        Assert.False(committed);
        rig.Adapter.NeedsResynchronization = false;
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(area), out _));
        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(area)));
    }

    /// <summary>Verifies that an Event during resynchronization needs the current token, and uses ordinary authority once resynchronization ends.</summary>
    [Fact]
    public void ApplyEvent_DuringResynchronization_RequiresCurrentToken()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Reconnect();
        AdapterInstanceId instance = rig.Adapter.CurrentInstanceId!.Value;
        long generation = rig.Adapter.CurrentConnectionGeneration;
        long contextGeneration = rig.PlayContextTracker.TransitionGeneration;

        StateApplyResult withoutToken = rig.Store.ApplyEvent(
            instance, generation, rig.ContextId, contextGeneration, null, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 44);
        StateApplyResult withForeignToken = rig.Store.ApplyEvent(
            instance, generation, rig.ContextId, contextGeneration, new ForeignToken(), AuthoritativeStateStoreRig.At, new StateAreaId(Area), 44);
        StateApplyResult withToken = rig.ApplyEvent(Area, 44);
        rig.CompleteResynchronization();
        StateApplyResult afterResynchronization = rig.Store.ApplyEvent(
            instance, generation, rig.ContextId, contextGeneration, null, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 45);

        Assert.Equal(StateApplyResult.Rejected, withoutToken);
        Assert.Equal(StateApplyResult.Rejected, withForeignToken);
        Assert.True(withToken.Accepted);
        Assert.True(afterResynchronization.Accepted);
        Assert.Equal(RevisionNumber.Initial.Next().Next(), afterResynchronization.Revision);
    }

    /// <summary>Verifies the Event rejection matrix: each stale or unauthorized Event returns the fixed rejected result and leaves no state behind.</summary>
    [Theory]
    [InlineData("staleInstance")]
    [InlineData("staleConnectionGeneration")]
    [InlineData("stalePlayContextId")]
    [InlineData("stalePlayContextGeneration")]
    [InlineData("adapterUnavailable")]
    [InlineData("unregisteredArea")]
    public void ApplyEvent_Rejections_LeaveNoState(string scenario)
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        AdapterInstanceId instance = rig.Adapter.CurrentInstanceId!.Value;
        long generation = rig.Adapter.CurrentConnectionGeneration;
        PlayContextId context = rig.ContextId;
        long contextGeneration = rig.PlayContextTracker.TransitionGeneration;
        string area = Area;
        switch (scenario)
        {
            case "staleInstance": instance = AdapterInstanceId.NewId(); break;
            case "staleConnectionGeneration": generation++; break;
            case "stalePlayContextId": context = PlayContextId.NewId(); break;
            case "stalePlayContextGeneration": contextGeneration++; break;
            case "adapterUnavailable": rig.Adapter.Current = AdapterAvailability.Unavailable; break;
            case "unregisteredArea": area = "unregistered"; break;
        }

        StateApplyResult result = rig.Store.ApplyEvent(instance, generation, context, contextGeneration, null, AuthoritativeStateStoreRig.At, new StateAreaId(area), 44);

        Assert.Equal(StateApplyResult.Rejected, result);
        rig.Adapter.Current = AdapterAvailability.Available;
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(area), out _));
        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(area)));
    }

    /// <summary>Verifies that a baseline is rejected while the adapter is unavailable, even with a token.</summary>
    [Fact]
    public void ApplyResynchronizationBaseline_AdapterUnavailable_IsRejected()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Reconnect();
        IAdapterResynchronizationToken token = rig.Adapter.TryClaimResynchronizationToken()!;
        rig.LoseContinuity();

        StateApplyResult result = rig.Store.ApplyResynchronizationBaseline(
            UpdateMode.Snapshot, token, rig.ContextId, rig.PlayContextTracker.TransitionGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 42);

        Assert.Equal(StateApplyResult.Rejected, result);
    }

    /// <summary>Verifies that a token from a previous resynchronization gate is rejected after the gate is re-armed.</summary>
    [Fact]
    public void ApplyResynchronizationBaseline_TokenFromPreviousGate_IsRejected()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Reconnect();
        IAdapterResynchronizationToken previousGateToken = rig.Adapter.TryClaimResynchronizationToken()!;
        rig.Reconnect();
        rig.Adapter.TryClaimResynchronizationToken();

        StateApplyResult result = rig.Store.ApplyResynchronizationBaseline(
            UpdateMode.Snapshot, previousGateToken, rig.ContextId, rig.PlayContextTracker.TransitionGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 42);

        Assert.Equal(StateApplyResult.Rejected, result);
    }

    /// <summary>Verifies that the Event and baseline paths fail loudly, like the ordinary path, when no play context exists yet.</summary>
    [Fact]
    public void ApplyEventAndBaseline_NoPlayContextYet_Throw()
    {
        var adapter = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var registered = new RegisteredStateAreaPolicy();
        registered.TryRegister(new StateAreaId(Area));
        var store = new AuthoritativeStateStore(adapter, new FakePlayContextTracker(), registered, new FakeStateAuthorityLifecycle());

        Assert.Throws<InvalidOperationException>(() => store.ApplyEvent(
            adapter.CurrentInstanceId!.Value, adapter.CurrentConnectionGeneration, PlayContextId.NewId(), 0, null,
            AuthoritativeStateStoreRig.At, new StateAreaId(Area), 1));
        adapter.CommitConnected(adapter.CurrentInstanceId!.Value, 1);
        IAdapterResynchronizationToken token = adapter.TryClaimResynchronizationToken()!;
        Assert.Throws<InvalidOperationException>(() => store.ApplyResynchronizationBaseline(
            UpdateMode.Snapshot, token, PlayContextId.NewId(), 0, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 1));
    }

    /// <summary>Verifies that a baseline can create an area's first record, announced and replayable once resynchronization completes.</summary>
    [Fact]
    public void ApplyResynchronizationBaseline_FirstRecordForArea_BecomesRevisionOne()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Reconnect();

        StateApplyResult result = rig.Baseline(Area, 42);
        rig.CompleteResynchronization();

        Assert.Equal(new StateApplyResult(true, true, RevisionNumber.Initial, RevisionNumber.Initial.Next()), result);
        Assert.Equal(42, ValueOf(rig.Snapshot(Area)));
    }

    /// <summary>Verifies that an unchanged baseline requested in Event mode publishes nothing, like Snapshot mode.</summary>
    [Fact]
    public void SameValueEventModeBaseline_PublishesNothingAndRestoresReplay()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.ApplyEvent(Area, 44);
        rig.LoseContinuity();
        rig.Reconnect();
        int raised = 0;
        rig.Store.SnapshotChanged += _ => raised++;
        rig.Store.EventOccurred += _ => raised++;

        StateApplyResult result = rig.Baseline(Area, 44, UpdateMode.Event);
        rig.CompleteResynchronization();

        Assert.False(result.Changed);
        Assert.Equal(0, raised);
        Assert.Equal(44, ValueOf(rig.Snapshot(Area)));
    }

    /// <summary>Verifies that a failing Event subscriber neither blocks the next subscriber nor undoes the commit.</summary>
    [Fact]
    public void EventPublication_SubscriberThrows_StateStaysCommittedAndOtherSubscriberRuns()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        bool secondRan = false;
        rig.Store.EventOccurred += _ => throw new InvalidOperationException("subscriber failure");
        rig.Store.EventOccurred += _ => secondRan = true;

        StateApplyResult result = rig.ApplyEvent(Area, 7);

        Assert.True(result.Accepted);
        Assert.True(secondRan);
        Assert.Equal(7, ValueOf(rig.Snapshot(Area)));
    }

    /// <summary>Verifies that the availability hint is raised only when resynchronization completes, and that a failing hint subscriber does not stop the next one.</summary>
    [Fact]
    public void SnapshotAvailabilityChanged_RaisedOnlyOnCompletion_AndContainsSubscriberFailure()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        int hints = 0;
        rig.Store.SnapshotAvailabilityChanged += () => throw new InvalidOperationException("subscriber failure");
        rig.Store.SnapshotAvailabilityChanged += () => hints++;
        rig.Reconnect();
        rig.Baseline(Area, 1);
        rig.LoseContinuity();

        Assert.Equal(0, hints);

        rig.Reconnect();
        rig.Baseline(Area, 1);
        rig.CompleteResynchronization();

        Assert.Equal(1, hints);
    }

    /// <summary>Verifies that the commit hook runs once, after the baseline is committed and before the change notification, for a changed and an unchanged baseline.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyResynchronizationBaseline_OnCommitted_RunsOnceAfterCommitAndBeforeNotification(bool unchanged)
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        rig.LoseContinuity();
        rig.Reconnect();
        var order = new List<string>();
        RevisionNumber? revisionAtHook = null;
        rig.Store.SnapshotChanged += _ => order.Add("notification");

        rig.Baseline(Area, unchanged ? 42 : 43, onCommitted: () =>
        {
            order.Add("committed");
            revisionAtHook = rig.Store.CurrentRevision(new StateAreaId(Area));
        });

        Assert.Equal(unchanged ? ["committed"] : ["committed", "notification"], order);
        Assert.Equal(rig.Store.CurrentRevision(new StateAreaId(Area)), revisionAtHook);
    }

    /// <summary>Verifies that the commit hook never runs for a rejected baseline.</summary>
    [Fact]
    public void ApplyResynchronizationBaseline_Rejected_DoesNotRunOnCommitted()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Reconnect();
        IAdapterResynchronizationToken token = rig.Adapter.TryClaimResynchronizationToken()!;
        bool ran = false;

        StateApplyResult result = rig.Store.ApplyResynchronizationBaseline(
            UpdateMode.Snapshot, token, rig.ContextId, rig.PlayContextTracker.TransitionGeneration + 1, AuthoritativeStateStoreRig.At,
            new StateAreaId(Area), 42, onCommitted: () => ran = true);

        Assert.False(result.Accepted);
        Assert.False(ran);
    }

    /// <summary>
    /// Characterization: a throwing commit hook is a caller invariant violation and surfaces to the
    /// caller rather than being swallowed, but the baseline is already committed and replayable, and
    /// the store's lock is released so later applications still work. The production hook cannot throw.
    /// </summary>
    [Fact]
    public void ApplyResynchronizationBaseline_OnCommittedThrows_PropagatesAfterCommitAndLeavesStoreUsable()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        rig.LoseContinuity();
        rig.Reconnect();

        Assert.Throws<InvalidOperationException>(() =>
            rig.Baseline(Area, 43, onCommitted: () => throw new InvalidOperationException("hook failed")));

        rig.Adapter.NeedsResynchronization = false;
        Assert.True(rig.Store.TryGetSnapshot(new StateAreaId(Area), out StateSnapshotPublication? snapshot));
        Assert.Equal(43, snapshot.Data.GetProperty("value").GetInt32());
        Assert.True(rig.Store.Apply(rig.Adapter.CurrentInstanceId!.Value, rig.Adapter.CurrentConnectionGeneration, rig.ContextId,
            rig.PlayContextTracker.TransitionGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 44).Accepted);
    }

    /// <summary>Verifies that a new connection generation hides state committed under the old one even when no resynchronization gate is raised.</summary>
    [Fact]
    public void NewConnectionGeneration_HidesStateCommittedUnderTheOldOne()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);

        rig.Adapter.CurrentConnectionGeneration++;

        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
        Assert.False(rig.Store.TryGetCurrentValue(new StateAreaId(Area), out int _));
    }

    /// <summary>Verifies that the first play-context transition, which has no previous context, is handled without error and starts revisions fresh.</summary>
    [Fact]
    public void FirstPlayContextTransition_WithNoPreviousContext_DoesNotThrow()
    {
        var playContextTracker = new FakePlayContextTracker();
        var adapter = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var registered = new RegisteredStateAreaPolicy();
        registered.TryRegister(new StateAreaId(Area));
        var store = new AuthoritativeStateStore(adapter, playContextTracker, registered, new FakeStateAuthorityLifecycle());

        playContextTracker.NotifyTransition(PlayContextId.NewId());

        Assert.Equal(RevisionNumber.Initial, store.CurrentRevision(new StateAreaId(Area)));
        Assert.False(store.TryGetSnapshot(new StateAreaId(Area), out _));
    }

    /// <summary>Verifies that concurrent writers to one area never lose a revision: every accepted distinct value advances the revision exactly once.</summary>
    [Fact]
    public void Apply_ConcurrentDistinctValues_NeverLoseARevisionIncrement()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        const int writers = 8;
        const int perWriter = 50;
        int accepted = 0;

        Parallel.For(0, writers, writer =>
        {
            for (int i = 0; i < perWriter; i++)
            {
                if (rig.Apply(Area, writer * 1000 + i).Changed)
                {
                    Interlocked.Increment(ref accepted);
                }
            }
        });

        Assert.Equal(writers * perWriter, accepted);
        Assert.Equal(accepted, (int)rig.Store.CurrentRevision(new StateAreaId(Area)).Value);
        Assert.Equal(rig.Store.CurrentRevision(new StateAreaId(Area)), rig.Snapshot(Area).Revision);
    }

    /// <summary>Verifies that a token consumed by a completed resynchronization cannot authorize a later baseline.</summary>
    [Fact]
    public void ApplyResynchronizationBaseline_TokenAfterCompletedResynchronization_IsRejected()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Reconnect();
        IAdapterResynchronizationToken token = rig.Adapter.TryClaimResynchronizationToken()!;
        Assert.True(rig.Store.ApplyResynchronizationBaseline(
            UpdateMode.Snapshot, token, rig.ContextId, rig.PlayContextTracker.TransitionGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 1).Accepted);
        rig.Adapter.NotifyResynchronized(rig.Adapter.CurrentInstanceId!.Value, rig.Adapter.CurrentConnectionGeneration, token);
        rig.Adapter.NeedsResynchronization = true;

        StateApplyResult result = rig.Store.ApplyResynchronizationBaseline(
            UpdateMode.Snapshot, token, rig.ContextId, rig.PlayContextTracker.TransitionGeneration, AuthoritativeStateStoreRig.At, new StateAreaId(Area), 2);

        Assert.Equal(StateApplyResult.Rejected, result);
    }

    /// <summary>Verifies that a disconnect and the following reconnect each advance a populated area's revision exactly once.</summary>
    [Fact]
    public void ContinuityLossThenReconnect_AdvancesRevisionOncePerTransition()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);
        RevisionNumber synchronized = rig.Store.CurrentRevision(new StateAreaId(Area));

        rig.LoseContinuity();
        Assert.Equal(synchronized.Next(), rig.Store.CurrentRevision(new StateAreaId(Area)));

        rig.Reconnect();
        Assert.Equal(synchronized.Next().Next(), rig.Store.CurrentRevision(new StateAreaId(Area)));
    }

    /// <summary>Verifies that a different adapter instance on the same connection generation does not see the previous instance's state as current.</summary>
    [Fact]
    public void NewAdapterInstanceOnSameGeneration_HidesStateCommittedByThePreviousInstance()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 42);

        rig.Adapter.CurrentInstanceId = AdapterInstanceId.NewId();

        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
        Assert.False(rig.Store.TryGetCurrentValue(new StateAreaId(Area), out int _));
    }

    /// <summary>Verifies that reading before any play context exists reports unavailable instead of failing.</summary>
    [Fact]
    public void Read_NoPlayContextYet_ReportsUnavailable()
    {
        var adapter = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var registered = new RegisteredStateAreaPolicy();
        registered.TryRegister(new StateAreaId(Area));
        var store = new AuthoritativeStateStore(adapter, new FakePlayContextTracker(), registered, new FakeStateAuthorityLifecycle());

        Assert.False(store.TryGetSnapshot(new StateAreaId(Area), out _));
        Assert.False(store.TryGetCurrentValue(new StateAreaId(Area), out int _));
    }

    /// <summary>Verifies that a value accepted while the very first play-context transition handler is still running survives that handler.</summary>
    [Fact]
    public async Task Apply_DuringDelayedFirstPlayContextTransition_PreservesState()
    {
        var playContextTracker = new FakePlayContextTracker();
        var adapter = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var registered = new RegisteredStateAreaPolicy();
        registered.TryRegister(new StateAreaId(Area));
        var store = new AuthoritativeStateStore(adapter, playContextTracker, registered, new FakeStateAuthorityLifecycle());
        var firstContext = PlayContextId.NewId();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        playContextTracker.Transitioned += _ =>
        {
            entered.Set();
            release.Wait();
        };

        Task transitionTask = Task.Run(() => playContextTracker.NotifyTransition(firstContext));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        StateApplyResult result = store.Apply(
            adapter.CurrentInstanceId!.Value, adapter.CurrentConnectionGeneration, firstContext, playContextTracker.TransitionGeneration,
            AuthoritativeStateStoreRig.At, new StateAreaId(Area), 7);
        release.Set();
        await transitionTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Accepted);
        Assert.True(store.TryGetCurrentValue(new StateAreaId(Area), out int value));
        Assert.Equal(7, value);
    }

    /// <summary>Verifies that one play-context transition resets every area, not just one.</summary>
    [Fact]
    public void PlayContextTransition_ResetsEveryArea()
    {
        AuthoritativeStateStoreRig rig = BuildRig();
        rig.Apply(Area, 1);
        rig.Apply(OtherArea, "text");

        rig.PlayContextTracker.NotifyTransition(PlayContextId.NewId());

        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(Area), out _));
        Assert.False(rig.Store.TryGetSnapshot(new StateAreaId(OtherArea), out _));
        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(Area)));
        Assert.Equal(RevisionNumber.Initial, rig.Store.CurrentRevision(new StateAreaId(OtherArea)));
    }

    /// <summary>A resynchronization token the tracker never issued.</summary>
    private sealed class ForeignToken : IAdapterResynchronizationToken
    {
    }
}
