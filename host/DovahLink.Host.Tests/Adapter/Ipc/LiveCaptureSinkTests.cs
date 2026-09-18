using System.Buffers.Binary;
using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="LiveCaptureSink"/>.</summary>
public class LiveCaptureSinkTests
{
    private static readonly StateAreaId HealthArea = new(Constants.CharacterHealthStateArea);
    private static readonly StateAreaId MagickaArea = new(Constants.CharacterMagickaStateArea);
    private static readonly StateAreaId StaminaArea = new(Constants.CharacterStaminaStateArea);
    private static readonly StateAreaId XpArea = new(Constants.CharacterXpStateArea);
    private static readonly StateAreaId LevelArea = new(Constants.CharacterLevelStateArea);

    private sealed record Fixture(
        LiveCaptureSink Sink,
        StatePublicationFeed Feed,
        IStatePublisher<float?> FloatPublisher,
        FakeAdapterAvailabilityTracker AdapterTracker,
        FakePlayContextTracker PlayContextTracker,
        PlayContextId Context,
        IResynchronizationTransactionCoordinator Coordinator,
        AdapterCaptureSource Source);

    /// <summary>
    /// Builds a sink wired exactly like production composition, but with controllable adapter/play-context
    /// trackers and a real StatePublisher/StatePublicationFeed pair so applied values are actually
    /// observable.
    /// </summary>
    /// <param name="coordinatorOverride">
    /// The resynchronization transaction coordinator to wire in, or <see langword="null"/> for the
    /// real <see cref="ResynchronizationTransactionCoordinator"/> -- override with a
    /// <see cref="FakeResynchronizationTransactionCoordinator"/> when a test needs to observe whether
    /// this sink ever claimed a token or recorded an area accepted, rather than only the resulting
    /// state.
    /// </param>
    private static Fixture CreateReady(IResynchronizationTransactionCoordinator? coordinatorOverride = null)
    {
        var playContextTracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        playContextTracker.NotifyTransition(context);
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var registeredAreas = new RegisteredStateAreaPolicy();
        foreach (StateAreaDefinition area in LiveStateCatalog.Default.StateAreas)
        {
            registeredAreas.TryRegister(area.Id);
        }

        var feed = new StatePublicationFeed(adapterTracker, playContextTracker, registeredAreas);
        var revisionTracker = new RevisionTracker();
        var floatPublisher = new StatePublisher<float?>(revisionTracker, playContextTracker, adapterTracker);
        var levelPublisher = new StatePublisher<ushort?>(revisionTracker, playContextTracker, adapterTracker);
        IResynchronizationTransactionCoordinator coordinator = coordinatorOverride ?? new ResynchronizationTransactionCoordinator(LiveStateCatalog.Default, adapterTracker);
        var sink = new LiveCaptureSink(LiveStateCatalog.Default, floatPublisher, levelPublisher, feed, adapterTracker, playContextTracker, coordinator, new FakeClock());
        var source = new AdapterCaptureSource(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration);
        return new Fixture(sink, feed, floatPublisher, adapterTracker, playContextTracker, context, coordinator, source);
    }

    private static byte[] EncodeVitals(float health, float magicka, float stamina)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0, 4), health);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4, 4), magicka);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), stamina);
        return bytes;
    }

    private static byte[] EncodeFloat(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] EncodeUInt16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    private static float? ReadValue(JsonElement data) =>
        data.GetProperty("value").ValueKind == JsonValueKind.Null ? null : data.GetProperty("value").GetSingle();

    /// <summary>Verifies that one coherent vitals capture applies all three resource areas independently.</summary>
    [Fact]
    public void ApplyCaptureResult_Vitals_AppliesAllThreeAreasIndependently()
    {
        Fixture fixture = CreateReady();
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(93.4f, 71.0f, 100.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.True(fixture.Feed.TryGetSnapshot(HealthArea, out StateSnapshotPublication? health));
        Assert.Equal(93.4f, ReadValue(health!.Data));
        Assert.True(fixture.Feed.TryGetSnapshot(MagickaArea, out StateSnapshotPublication? magicka));
        Assert.Equal(71.0f, ReadValue(magicka!.Data));
        Assert.True(fixture.Feed.TryGetSnapshot(StaminaArea, out StateSnapshotPublication? stamina));
        Assert.Equal(100.0f, ReadValue(stamina!.Data));
    }

    /// <summary>Verifies that only the area whose value actually changed advances its revision, matching the roadmap's "only Health revision advances" acceptance scenario.</summary>
    [Fact]
    public void ApplyCaptureResult_VitalsSecondCaptureChangesOnlyHealth_OnlyHealthRevisionAdvances()
    {
        Fixture fixture = CreateReady();
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(93.4f, 71.0f, 100.0f)), fixture.Source);
        RevisionNumber healthRevisionBefore = fixture.FloatPublisher.CurrentRevision(HealthArea);
        RevisionNumber magickaRevisionBefore = fixture.FloatPublisher.CurrentRevision(MagickaArea);
        RevisionNumber staminaRevisionBefore = fixture.FloatPublisher.CurrentRevision(StaminaArea);

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(2, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(80.0f, 71.0f, 100.0f)), fixture.Source);

        Assert.NotEqual(healthRevisionBefore, fixture.FloatPublisher.CurrentRevision(HealthArea));
        Assert.Equal(magickaRevisionBefore, fixture.FloatPublisher.CurrentRevision(MagickaArea));
        Assert.Equal(staminaRevisionBefore, fixture.FloatPublisher.CurrentRevision(StaminaArea));
        Assert.True(fixture.Feed.TryGetSnapshot(HealthArea, out StateSnapshotPublication? health));
        Assert.Equal(80.0f, ReadValue(health!.Data));
    }

    /// <summary>Verifies that an unavailable capture applies an explicit null value, never a fabricated zero.</summary>
    [Fact]
    public void ApplyCaptureResult_XpUnavailable_AppliesExplicitNullNotZero()
    {
        Fixture fixture = CreateReady();
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Unavailable, fixture.Context, []);

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? xp));
        Assert.Null(ReadValue(xp!.Data));
    }

    /// <summary>Verifies that a level capture publishes through EventOccurred, not SnapshotChanged, matching its Event update mode.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEvent_PublishesThroughEventOccurredNotSnapshotChanged()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        StateEventPublication? raisedEvent = null;
        bool snapshotChangedRaised = false;
        fixture.Feed.EventOccurred += publication => raisedEvent = publication;
        fixture.Feed.SnapshotChanged += _ => snapshotChangedRaised = true;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.NotNull(raisedEvent);
        Assert.Equal(LevelArea, raisedEvent!.StateArea);
        Assert.Equal(RevisionNumber.Initial, raisedEvent.BaseRevision);
        Assert.Equal(RevisionNumber.Initial.Next(), raisedEvent.Revision);
        Assert.False(snapshotChangedRaised);
        Assert.Empty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>
    /// Verifies that a level-changed Event arriving during resynchronization may be authorized and
    /// applied without being recorded as an accepted baseline area.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventWhileNeedsResynchronization_DoesNotRecordAreaAccepted()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.NotEmpty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that a level-changed Event during resynchronization remains an Event publication with the expected revisions.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventWhileNeedsResynchronization_PublishesEventNotSnapshot()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
        StateEventPublication? raisedEvent = null;
        bool snapshotChangedRaised = false;
        fixture.Feed.EventOccurred += publication => raisedEvent = publication;
        fixture.Feed.SnapshotChanged += _ => snapshotChangedRaised = true;

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);

        Assert.NotNull(raisedEvent);
        Assert.Equal(LevelArea, raisedEvent!.StateArea);
        Assert.Equal(RevisionNumber.Initial, raisedEvent.BaseRevision);
        Assert.Equal(RevisionNumber.Initial.Next(), raisedEvent.Revision);
        Assert.Equal(11, raisedEvent.Data.GetProperty("value").GetUInt16());
        Assert.False(snapshotChangedRaised);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that successive resynchronization Events chain their base and resulting revisions.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventsWhileNeedsResynchronization_ChainRevisions()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
        var raisedEvents = new List<StateEventPublication>();
        fixture.Feed.EventOccurred += publication => raisedEvents.Add(publication);

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(12)), fixture.Source);

        Assert.Equal(2, raisedEvents.Count);
        Assert.Equal(RevisionNumber.Initial, raisedEvents[0].BaseRevision);
        Assert.Equal(RevisionNumber.Initial.Next(), raisedEvents[0].Revision);
        Assert.Equal(raisedEvents[0].Revision, raisedEvents[1].BaseRevision);
        Assert.Equal(raisedEvents[0].Revision.Next(), raisedEvents[1].Revision);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that an Event without current resynchronization authorization is dropped safely.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventWhileNeedsResynchronizationWithoutToken_DropsWithoutPublication()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        bool eventRaised = false;
        fixture.Feed.EventOccurred += _ => eventRaised = true;

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);

        Assert.NotEmpty(fakeCoordinator.AcquireTokenCalls);
        Assert.False(eventRaised);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that the actual level baseline sample records the Level area after an Event arrived first.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventBeforeLevelBaseline_OnlyBaselineRecordsAreaAccepted()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Sample,
            (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);

        Assert.Single(fakeCoordinator.RecordAreaAcceptedCalls);
        Assert.Equal(LevelArea, fakeCoordinator.RecordAreaAcceptedCalls[0].AreaId);
    }

    /// <summary>Verifies that an Event cannot complete a transaction when the Level baseline remains outstanding.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventBeforeBaseline_CannotCompleteResynchronization()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Sample,
            (uint)CharacterSampleToken.CharacterVitals,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeVitals(90.0f, 80.0f, 70.0f)), fixture.Source);
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Sample,
            (uint)CharacterSampleToken.CharacterXp,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeFloat(50.0f)), fixture.Source);
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);
        fixture.Coordinator.RecordAdapterPlanAccepted(true, fixture.Source.InstanceId, fixture.Source.ConnectionGeneration, fixture.Context, fixture.PlayContextTracker.TransitionGeneration);

        Assert.True(fixture.AdapterTracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that a level baseline sample -- unlike the level-changed event above -- publishes
    /// through SnapshotChanged, not EventOccurred: the baseline establishes the current authoritative
    /// level as replaceable state, not an ordered change, even though it shares the same decode and
    /// the same area as the level-changed event.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_LevelBaselineSample_PublishesThroughSnapshotChangedNotEventOccurred()
    {
        Fixture fixture = CreateReady();
        // A baseline sample is only ever sent, and only ever a valid apply, while the adapter genuinely
        // needs resynchronization -- matching real production sequencing.
        fixture.AdapterTracker.NeedsResynchronization = true;
        StateSnapshotPublication? raisedSnapshot = null;
        bool eventOccurredRaised = false;
        fixture.Feed.SnapshotChanged += publication => raisedSnapshot = publication;
        fixture.Feed.EventOccurred += _ => eventOccurredRaised = true;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.NotNull(raisedSnapshot);
        Assert.Equal(LevelArea, raisedSnapshot!.StateArea);
        Assert.False(eventOccurredRaised);
    }

    /// <summary>Verifies that an unrecognized capture key is silently dropped rather than applied.</summary>
    [Fact]
    public void ApplyCaptureResult_UnknownCaptureKey_DoesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, CaptureKey: 999, CaptureAvailability.Available, fixture.Context, EncodeFloat(1.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>
    /// Verifies that CaptureResultApplied is raised with the arriving result and the passed-in
    /// source's own connection generation -- not a value rediscovered from the adapter availability
    /// tracker's own current state, which this test deliberately sets to a different value to prove
    /// the passed source, not the tracker, is authoritative.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_RaisesCaptureResultAppliedWithSourcesConnectionGeneration()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.CurrentConnectionGeneration = 999;
        var captureResult = new IpcCaptureResultMessage(3, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(1.0f));
        var source = new AdapterCaptureSource(fixture.AdapterTracker.CurrentInstanceId!.Value, 7);
        List<(IpcCaptureResultMessage CaptureResult, long ConnectionGeneration)> raised = [];
        fixture.Sink.CaptureResultApplied += (result, generation) => raised.Add((result, generation));

        fixture.Sink.ApplyCaptureResult(captureResult, source);

        Assert.Equal([(captureResult, 7L)], raised);
    }

    /// <summary>Verifies that a capture from the currently available adapter connection is accepted.</summary>
    [Fact]
    public void ApplyCaptureResult_CurrentExactSource_AppliesNormally()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.CurrentConnectionGeneration = 1;
        AdapterCaptureSource currentSource = new(fixture.Source.InstanceId, 1);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, currentSource);

        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? xp));
        Assert.Equal(50.0f, ReadValue(xp!.Data));
    }

    /// <summary>Verifies that a capture from an old connection generation is dropped without state or publication effects.</summary>
    [Fact]
    public void ApplyCaptureResult_OldConnectionGeneration_DropsWithoutMutation()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.CurrentConnectionGeneration = 2;
        AdapterCaptureSource staleSource = new(fixture.Source.InstanceId, 1);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));
        int snapshotPublicationCount = 0;
        int eventPublicationCount = 0;
        fixture.Feed.SnapshotChanged += _ => snapshotPublicationCount++;
        fixture.Feed.EventOccurred += _ => eventPublicationCount++;

        fixture.Sink.ApplyCaptureResult(captureResult, staleSource);

        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        Assert.Equal(0, snapshotPublicationCount);
        Assert.Equal(0, eventPublicationCount);
        Assert.Empty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that a capture from an old adapter instance is dropped without state or publication effects.</summary>
    [Fact]
    public void ApplyCaptureResult_OldAdapterInstance_DropsWithoutMutation()
    {
        Fixture fixture = CreateReady();
        AdapterCaptureSource staleSource = new(AdapterInstanceId.NewId(), fixture.Source.ConnectionGeneration);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));
        int snapshotPublicationCount = 0;
        fixture.Feed.SnapshotChanged += _ => snapshotPublicationCount++;

        fixture.Sink.ApplyCaptureResult(captureResult, staleSource);

        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        Assert.Equal(0, snapshotPublicationCount);
    }

    /// <summary>Verifies that a delayed generation-one result is dropped after the same adapter reconnects as generation two.</summary>
    [Fact]
    public void ApplyCaptureResult_DelayedGenerationOneAfterReconnect_DropsWithoutMutation()
    {
        Fixture fixture = CreateReady();
        AdapterInstanceId instanceId = fixture.Source.InstanceId;
        fixture.AdapterTracker.CommitConnected(instanceId, 2);
        AdapterCaptureSource delayedSource = new(instanceId, 1);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, delayedSource);

        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that a delayed baseline cannot claim resynchronization progress for a newer connection generation.</summary>
    [Fact]
    public void ApplyCaptureResult_DelayedBaselineFromOldGeneration_DoesNotRecordNewGenerationProgress()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.CurrentConnectionGeneration = 2;
        fixture.AdapterTracker.NeedsResynchronization = true;
        AdapterCaptureSource delayedSource = new(fixture.Source.InstanceId, 1);
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, delayedSource);

        Assert.Empty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>
    /// Verifies that CaptureResultApplied still fires for an unrecognized capture key -- before this
    /// sink's own recognition check -- so a listener learns a reply arrived even when this sink itself
    /// goes on to drop it.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_UnknownCaptureKey_StillRaisesCaptureResultApplied()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, CaptureKey: 999, CaptureAvailability.Available, fixture.Context, EncodeFloat(1.0f));
        int raisedCount = 0;
        fixture.Sink.CaptureResultApplied += (_, _) => raisedCount++;

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Equal(1, raisedCount);
    }

    /// <summary>Verifies that a capture stamped with a play context other than the current one is dropped rather than misattributed.</summary>
    [Fact]
    public void ApplyCaptureResult_StalePlayContext_DoesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, PlayContextId.NewId(), EncodeFloat(1.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that a vitals capture with a non-finite value applies none of the three areas, not just the invalid one.</summary>
    [Fact]
    public void ApplyCaptureResult_VitalsContainsNaN_AppliesNoneOfTheThreeAreas()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(93.4f, float.NaN, 100.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(HealthArea, out _));
        Assert.False(fixture.Feed.TryGetSnapshot(MagickaArea, out _));
        Assert.False(fixture.Feed.TryGetSnapshot(StaminaArea, out _));
    }

    /// <summary>Verifies that a vitals payload of the wrong length applies nothing.</summary>
    [Fact]
    public void ApplyCaptureResult_VitalsMalformedLength_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, new byte[10]);

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(HealthArea, out _));
    }

    /// <summary>
    /// Verifies that while the adapter needs resynchronization, a capture is still applied -- routed
    /// through the baseline path instead of the ordinary one -- and its value is genuinely stored
    /// (visible once resynchronization completes), even though StatePublicationFeed.TryGetSnapshot
    /// withholds it as a pull read for as long as resynchronization stays outstanding.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_WhileNeedsResynchronization_StillAppliesThroughBaselinePath()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        fixture.AdapterTracker.NeedsResynchronization = false;
        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? xp));
        Assert.Equal(50.0f, ReadValue(xp!.Data));
    }

    /// <summary>
    /// Verifies that an ordinary, scheduler-issued sample reply arriving while the adapter needs
    /// resynchronization never satisfies that resynchronization: capture purpose is decided from the
    /// capture's own correlation id -- zero only for a baseline or a native event, nonzero only for an
    /// ordinary ReadSample reply -- never inferred from the coincidence of a resynchronization
    /// happening to be outstanding when the reply arrives. Proven directly against the
    /// resynchronization coordinator rather than only the resulting state, since an ordinary apply's
    /// own rejection (proven separately by every other "while NeedsResynchronization" test in this
    /// file) would look identical from the outside whether or not it had also, incorrectly, claimed a
    /// baseline token along the way.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_OrdinarySampleWhileNeedsResynchronization_NeverRecordsAreaAccepted()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Empty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that applying the same value twice publishes only once, since the second apply does not change anything.</summary>
    [Fact]
    public void ApplyCaptureResult_SameValueTwice_PublishesOnlyOnce()
    {
        Fixture fixture = CreateReady();
        var raised = new List<StateSnapshotPublication>();
        fixture.Feed.SnapshotChanged += publication =>
        {
            if (publication.StateArea == XpArea)
            {
                raised.Add(publication);
            }
        };
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);
        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Single(raised);
    }

    /// <summary>Verifies that a capture is rejected while the adapter is unavailable, since StatePublisher.Apply itself gates on it.</summary>
    [Fact]
    public void ApplyCaptureResult_AdapterUnavailable_DoesNothing()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.Current = AdapterAvailability.Unavailable;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that an experience payload of the wrong length applies nothing, symmetric with the vitals case.</summary>
    [Fact]
    public void ApplyCaptureResult_XpMalformedLength_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, new byte[3]);

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that a non-finite experience value applies nothing, symmetric with the vitals case.</summary>
    [Fact]
    public void ApplyCaptureResult_XpNaN_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(float.NaN));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that an unavailable experience capture carrying a nonempty payload (a malformed combination the wire codec should never actually produce) is defensively rejected rather than misread.</summary>
    [Fact]
    public void ApplyCaptureResult_XpUnavailableWithNonemptyPayload_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Unavailable, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>
    /// Verifies that the level baseline sample token -- not just the level-changed event -- decodes
    /// and applies to the same level area, including while resynchronizing, proving the generic
    /// apply-and-publish routing works for the ushort-valued publisher too. The applied value is
    /// genuinely stored (visible once resynchronization completes), even though
    /// StatePublicationFeed.TryGetSnapshot withholds it as a pull read until then.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_LevelBaselineSampleWhileNeedsResynchronization_AppliesToLevelAreaThroughBaselinePath()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(LevelArea, out _));
        fixture.AdapterTracker.NeedsResynchronization = false;
        Assert.True(fixture.Feed.TryGetSnapshot(LevelArea, out StateSnapshotPublication? level));
        Assert.Equal(12, level!.Data.GetProperty("value").GetUInt16());
    }

    /// <summary>
    /// Verifies that an accepted resynchronization baseline whose value genuinely changed still
    /// publishes through SnapshotChanged, proving the unchanged-baseline handling below did not fold
    /// this case into a silent EstablishBaseline-only path.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_ResynchronizationBaselineChanged_StillRaisesSnapshotChanged()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;
        StateSnapshotPublication? raised = null;
        fixture.Feed.SnapshotChanged += publication =>
        {
            if (publication.StateArea == XpArea)
            {
                raised = publication;
            }
        };
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.NotNull(raised);
        Assert.Equal(50.0f, ReadValue(raised!.Data));
    }

    /// <summary>
    /// Verifies that an accepted resynchronization baseline whose value is unchanged from what was
    /// already stored still restores the publication feed's pull-read cache after a continuity loss
    /// cleared it -- not only the publisher's own authoritative store, which a same-value baseline
    /// already updates correctly. Without this, a client requesting a snapshot right after
    /// resynchronization completes would be told no value is available merely because nothing about
    /// it changed.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_ResynchronizationBaselineUnchangedAfterDisconnect_RestoresFeedSnapshot()
    {
        Fixture fixture = CreateReady();
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var initialCapture = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(100.0f));
        fixture.Sink.ApplyCaptureResult(initialCapture, fixture.Source);
        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out _));

        // A continuity loss unconditionally clears the feed's own pull-read cache, per
        // StatePublicationFeed's documented defense-in-depth clearing.
        fixture.AdapterTracker.PublishTransition(new AdapterAvailabilityTransition(
            AdapterAvailability.Available, AdapterAvailability.Unavailable, fixture.AdapterTracker.CurrentInstanceId, fixture.AdapterTracker.CurrentConnectionGeneration));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));

        // Reconnect and resynchronize the exact same value.
        fixture.AdapterTracker.Current = AdapterAvailability.Available;
        fixture.AdapterTracker.NeedsResynchronization = true;
        var resyncCapture = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(100.0f));
        fixture.Sink.ApplyCaptureResult(resyncCapture, fixture.Source);
        fixture.AdapterTracker.NeedsResynchronization = false;

        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? xp));
        Assert.Equal(100.0f, ReadValue(xp!.Data));
    }

    /// <summary>Verifies that a resynchronization token already claimed by another caller is a silent drop, not a crash.</summary>
    [Fact]
    public void ApplyCaptureResult_ResynchronizationTokenAlreadyClaimed_DoesNothingAndDoesNotThrow()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;
        fixture.AdapterTracker.TryClaimResynchronizationToken(); // claimed by someone else first
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        Exception? escaped = Record.Exception(() => fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source));

        Assert.Null(escaped);
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }
}
