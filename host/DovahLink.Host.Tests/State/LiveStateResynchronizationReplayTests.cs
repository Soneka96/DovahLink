using DovahLink.Host.Client.Protocol;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

/// <summary>
/// Invariants for replayability after Adapter continuity loss: pre-loss values are not current while
/// gated, and a successful resynchronization -- even one whose baselines are all identical to the
/// pre-loss values -- must leave every area replayable to a brand-new client without any later gameplay change.
/// </summary>
public class LiveStateResynchronizationReplayTests
{
    /// <summary>Every production area, as theory data, so each can be the last baseline to arrive.</summary>
    public static IEnumerable<object[]> EachProductionArea() =>
        LivePipelineHarness.ProductionAreas.Select(area => new object[] { area });

    /// <summary>Builds a synchronized harness, then loses continuity and reconnects as a new generation, with resynchronization begun but no baseline applied.</summary>
    /// <param name="values">The values established before the loss.</param>
    private static LivePipelineHarness BuildReconnectedAndGated(ProductionStateValues values)
    {
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(values);
        harness.LoseContinuity();
        harness.ConnectAdapter();
        harness.BeginResynchronization();
        return harness;
    }

    /// <summary>Verifies old-generation state is neither typed-current nor replayable once continuity is lost.</summary>
    [Fact]
    public void ContinuityLoss_AfterSynchronization_GatesEveryAreaUntilResynchronized()
    {
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(Fixtures.BuildProductionStateValues());

        harness.LoseContinuity();

        Assert.Equal(LivePipelineHarness.ProductionAreas, harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
        Assert.All(LivePipelineHarness.ProductionAreas, area => Assert.False(harness.HasCurrentTypedValue(area)));
    }

    /// <summary>Verifies partially delivered baselines never make any area readable before resynchronization completes.</summary>
    [Fact]
    public void Resynchronization_WithOnlySomeBaselinesApplied_KeepsEveryAreaGated()
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildReconnectedAndGated(values);

        foreach (string area in LivePipelineHarness.ProductionAreas.Take(LivePipelineHarness.ProductionAreas.Count - 1))
        {
            harness.ApplyProductionBaseline(area, values);
        }

        harness.AcceptAdapterPlan();

        Assert.True(harness.AdapterTracker.NeedsResynchronization);
        Assert.Equal(LivePipelineHarness.ProductionAreas, harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }

    /// <summary>Verifies same-value resynchronization leaves all eight areas replayable, whichever area's baseline arrives last.</summary>
    /// <param name="lastArea">The area whose baseline arrives last.</param>
    [Theory]
    [MemberData(nameof(EachProductionArea))]
    public void SameValueResynchronization_WhicheverAreaIsLast_MakesEveryAreaReplayable(string lastArea)
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildReconnectedAndGated(values);

        harness.ApplyAllProductionBaselines(values, lastArea);
        harness.AcceptAdapterPlan();

        Assert.False(harness.AdapterTracker.NeedsResynchronization);
        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
        Assert.All(LivePipelineHarness.ProductionAreas, area => Assert.True(harness.HasCurrentTypedValue(area), area));
    }

    /// <summary>Verifies the Adapter plan acknowledgement arriving before the baselines does not change the outcome.</summary>
    /// <param name="lastArea">The area whose baseline arrives last.</param>
    [Theory]
    [MemberData(nameof(EachProductionArea))]
    public void SameValueResynchronization_PlanAcceptedBeforeBaselines_MakesEveryAreaReplayable(string lastArea)
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildReconnectedAndGated(values);

        harness.AcceptAdapterPlan();
        harness.ApplyAllProductionBaselines(values, lastArea);

        Assert.False(harness.AdapterTracker.NeedsResynchronization);
        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }

    /// <summary>Verifies a brand-new client after same-value resynchronization receives every pre-loss value with no gameplay change.</summary>
    [Fact]
    public void NewClient_AfterSameValueResynchronization_ReceivesAllBaselinesWithPreLossValues()
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(values);
        LivePipelineClient beforeLoss = harness.CreateClient();
        beforeLoss.Subscribe(LivePipelineHarness.ProductionAreas);
        beforeLoss.Disconnect();
        harness.LoseContinuity();
        harness.ConnectAdapter();
        harness.BeginResynchronization();
        harness.ApplyAllProductionBaselines(values);
        harness.AcceptAdapterPlan();

        LivePipelineClient afterResync = harness.CreateClient();
        afterResync.Subscribe(LivePipelineHarness.ProductionAreas);

        Assert.Equal(
            LivePipelineHarness.ProductionAreas.Order(),
            afterResync.Snapshots.Select(snapshot => snapshot.StateArea).Order());
        foreach (ReceivedSnapshot received in afterResync.Snapshots)
        {
            ReceivedSnapshot original = beforeLoss.Snapshots.Single(snapshot => snapshot.StateArea == received.StateArea);
            Assert.Equal(original.Data.GetRawText(), received.Data.GetRawText());
            Assert.Equal(harness.CurrentRevision(received.StateArea).Value, received.Revision);
            Assert.Equal(harness.PlayContext.ToString(), received.PlayContextId);
        }

        Assert.Equal(0, afterResync.ErrorCount);
    }

    /// <summary>Verifies replayability is restored without any change publication when every baseline equals the prior value.</summary>
    [Fact]
    public void SameValueResynchronization_RestoresReplayWithoutAnyChangePublication()
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildReconnectedAndGated(values);
        List<string> changed = [];
        harness.Feed.SnapshotChanged += snapshot => changed.Add(snapshot.StateArea.Value);

        harness.ApplyAllProductionBaselines(values);
        harness.AcceptAdapterPlan();

        Assert.Empty(changed);
        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }

    /// <summary>Verifies the control case: a baseline that differs is published as a change, and the unchanged rest are still replayable.</summary>
    [Fact]
    public void Resynchronization_WithOneChangedBaseline_PublishesOnlyThatAreaAndRestoresAll()
    {
        ProductionStateValues before = Fixtures.BuildProductionStateValues();
        ProductionStateValues after = Fixtures.BuildProductionStateValues(locationName: "Solitude");
        LivePipelineHarness harness = BuildReconnectedAndGated(before);
        List<string> changed = [];
        harness.Feed.SnapshotChanged += snapshot => changed.Add(snapshot.StateArea.Value);

        harness.ApplyAllProductionBaselines(after);
        harness.AcceptAdapterPlan();

        Assert.Equal([Constants.PlayerLocationStateArea], changed);
        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }

    /// <summary>Verifies a same-value Level baseline is replayable without a Level-changed Event ever being raised.</summary>
    [Fact]
    public void SameValueResynchronization_Level_IsReplayableWithoutAnyLevelEvent()
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues(level: 43);
        LivePipelineHarness harness = BuildReconnectedAndGated(values);
        int events = 0;
        harness.Feed.EventOccurred += _ => events++;

        harness.ApplyAllProductionBaselines(values);
        harness.AcceptAdapterPlan();

        Assert.Equal(0, events);
        Assert.True(harness.Feed.TryGetSnapshot(new StateAreaId(Constants.CharacterLevelStateArea), out StateSnapshotPublication? level));
        Assert.Equal(43, level!.Data.GetProperty("value").GetInt32());
        LivePipelineClient client = harness.CreateClient();
        client.Subscribe([Constants.CharacterLevelStateArea]);
        ReceivedSnapshot received = Assert.Single(client.Snapshots);
        Assert.Equal(43, received.Data.GetProperty("value").GetInt32());
        Assert.Equal(harness.CurrentRevision(Constants.CharacterLevelStateArea).Value, received.Revision);
    }

    /// <summary>Verifies a Level Event after a same-value baseline chains from the baseline revision and updates what is replayed.</summary>
    [Fact]
    public void LevelChanged_AfterSameValueResynchronization_ChainsFromBaselineRevisionAndUpdatesReplay()
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues(level: 43);
        LivePipelineHarness harness = BuildReconnectedAndGated(values);
        harness.ApplyAllProductionBaselines(values);
        harness.AcceptAdapterPlan();
        LivePipelineClient live = harness.CreateClient();
        live.Subscribe([Constants.CharacterLevelStateArea]);
        ulong baselineRevision = Assert.Single(live.Snapshots).Revision;

        harness.ApplyLevelChanged(44);

        Assert.True(harness.Feed.TryGetSnapshot(new StateAreaId(Constants.CharacterLevelStateArea), out StateSnapshotPublication? snapshot));
        Assert.Equal(44, snapshot!.Data.GetProperty("value").GetInt32());
        Assert.Equal(baselineRevision + 1, snapshot.Revision.Value);
        StateEventPayload delivered = Assert.Single(live.Events);
        Assert.Equal(baselineRevision, delivered.BaseRevision);
        Assert.Equal(baselineRevision + 1, delivered.Revision);
        Assert.Equal(44, delivered.Data.GetProperty("value").GetInt32());
        LivePipelineClient fresh = harness.CreateClient();
        fresh.Subscribe([Constants.CharacterLevelStateArea]);
        Assert.Equal(44, Assert.Single(fresh.Snapshots).Data.GetProperty("value").GetInt32());
    }

    /// <summary>Verifies repeated continuity-loss cycles, each resynchronized with identical values, keep every area replayable.</summary>
    [Fact]
    public void RepeatedContinuityLoss_WithSameValueResynchronizations_KeepsEveryAreaReplayable()
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(values);

        for (int cycle = 0; cycle < 3; cycle++)
        {
            harness.LoseContinuity();
            Assert.Equal(LivePipelineHarness.ProductionAreas, harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
            harness.ConnectAdapter();
            harness.BeginResynchronization();
            harness.ApplyAllProductionBaselines(values);
            harness.AcceptAdapterPlan();

            Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
            LivePipelineClient client = harness.CreateClient();
            client.Subscribe(LivePipelineHarness.ProductionAreas);
            Assert.Equal(LivePipelineHarness.ProductionAreas.Count, client.Snapshots.Count);
        }
    }

    /// <summary>Verifies an unchanged unavailable (null) baseline is still a replayable current snapshot, so the Host reports unavailability truthfully.</summary>
    [Fact]
    public void SameValueResynchronization_WithUnavailableBaselines_ReplaysNullSnapshots()
    {
        var unavailable = new ProductionStateValues(null, null, null, null, null, null, null, null);
        LivePipelineHarness harness = BuildReconnectedAndGated(unavailable);
        harness.ApplyAllProductionBaselines(unavailable);
        harness.AcceptAdapterPlan();

        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
        LivePipelineClient client = harness.CreateClient();
        client.Subscribe(LivePipelineHarness.ProductionAreas);
        Assert.Equal(LivePipelineHarness.ProductionAreas.Count, client.Snapshots.Count);
        Assert.All(client.Snapshots, snapshot => Assert.Equal(System.Text.Json.JsonValueKind.Null, snapshot.Data.GetProperty("value").ValueKind));
    }
}
