using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

/// <summary>
/// Tests for <see cref="LivePipelineHarness"/>: they prove the harness drives the real capture-to-replay
/// chain, so the invariant tests built on it cannot silently pass over a pre-populated fake.
/// </summary>
public class LivePipelineHarnessTests
{
    /// <summary>Verifies initial resynchronization through the real chain leaves every production area replayable.</summary>
    [Fact]
    public void EstablishInitialState_RealChain_MakesEveryProductionAreaReplayable()
    {
        var harness = new LivePipelineHarness();

        harness.EstablishInitialState(Fixtures.BuildProductionStateValues());

        Assert.False(harness.AdapterTracker.NeedsResynchronization);
        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
        Assert.Equal(LivePipelineHarness.ProductionAreas.Count, harness.Registered.Count);
    }

    /// <summary>Verifies the feed holds nothing before any capture is applied.</summary>
    [Fact]
    public void UnreadableAreas_BeforeAnyCapture_ReportsEveryProductionArea()
    {
        var harness = new LivePipelineHarness();

        Assert.Equal(LivePipelineHarness.ProductionAreas, harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }

    /// <summary>Verifies resynchronization does not complete while a required baseline is outstanding.</summary>
    [Fact]
    public void EstablishInitialState_MissingBaseline_LeavesResynchronizationOutstanding()
    {
        var harness = new LivePipelineHarness();
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        harness.ConnectAdapter();
        harness.BeginResynchronization();
        foreach (string area in LivePipelineHarness.ProductionAreas.Where(area => area != Constants.GameTimeStateArea))
        {
            harness.ApplyProductionBaseline(area, values);
        }

        harness.AcceptAdapterPlan();

        Assert.True(harness.AdapterTracker.NeedsResynchronization);
        Assert.Empty(harness.Recovery.RecoveryRequests);
    }

    /// <summary>Verifies a fresh client receives all eight baselines decoded from the real feed.</summary>
    [Fact]
    public void CreateClient_AfterInitialState_DecodesAllEightBaselines()
    {
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(Fixtures.BuildProductionStateValues());
        LivePipelineClient client = harness.CreateClient();

        client.Subscribe(LivePipelineHarness.ProductionAreas);

        Assert.Equal(
            LivePipelineHarness.ProductionAreas.Order(),
            client.Snapshots.Select(snapshot => snapshot.StateArea).Order());
        Assert.Equal(43, client.Snapshots.Single(snapshot => snapshot.StateArea == Constants.CharacterLevelStateArea).Data.GetProperty("value").GetInt32());
        Assert.Equal(0, client.ErrorCount);
    }

    /// <summary>Verifies continuity loss makes every area unreadable until resynchronization completes.</summary>
    [Fact]
    public void LoseContinuity_AfterInitialState_MakesEveryAreaUnreadable()
    {
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(Fixtures.BuildProductionStateValues());

        harness.LoseContinuity();

        Assert.Equal(LivePipelineHarness.ProductionAreas, harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }

    /// <summary>Verifies the last-area option really controls which baseline arrives last, which ordering tests rely on.</summary>
    [Fact]
    public void ApplyAllProductionBaselines_WithLastArea_AppliesThatAreaLast()
    {
        var harness = new LivePipelineHarness();
        harness.ConnectAdapter();
        harness.BeginResynchronization();
        List<string> published = [];
        harness.Feed.SnapshotChanged += snapshot => published.Add(snapshot.StateArea.Value);

        harness.ApplyAllProductionBaselines(Fixtures.BuildProductionStateValues(), lastArea: Constants.CharacterVitalsStateArea);

        Assert.Equal(LivePipelineHarness.ProductionAreas.Count, published.Count);
        Assert.Equal(Constants.CharacterVitalsStateArea, published[^1]);
    }

    /// <summary>Verifies the Event helper takes the reliable Event path, advancing the revision from the baseline.</summary>
    [Fact]
    public void ApplyLevelChanged_AfterBaseline_PublishesEventAdvancingBaselineRevision()
    {
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(Fixtures.BuildProductionStateValues(level: 43));
        List<StateEventPublication> events = [];
        harness.Feed.EventOccurred += events.Add;

        harness.ApplyLevelChanged(44);

        StateEventPublication published = Assert.Single(events);
        Assert.Equal(Constants.CharacterLevelStateArea, published.StateArea.Value);
        Assert.Equal(published.BaseRevision.Next(), published.Revision);
    }

    /// <summary>Verifies a second connect after continuity loss starts a newer generation that again requires resynchronization.</summary>
    [Fact]
    public void ConnectAdapter_AfterContinuityLoss_StartsNewGenerationNeedingResynchronization()
    {
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(Fixtures.BuildProductionStateValues());
        AdapterCaptureSource first = harness.Source;
        harness.LoseContinuity();

        harness.ConnectAdapter();

        Assert.True(harness.Source.ConnectionGeneration > first.ConnectionGeneration);
        Assert.NotEqual(first.InstanceId, harness.Source.InstanceId);
        Assert.True(harness.AdapterTracker.NeedsResynchronization);
        Assert.Equal(AdapterAvailability.Available, harness.AdapterTracker.Current);
    }

    /// <summary>Verifies the baseline helper rejects an area the production dispatch does not know.</summary>
    [Fact]
    public void ApplyProductionBaseline_UnknownArea_Throws()
    {
        var harness = new LivePipelineHarness();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => harness.ApplyProductionBaseline("future_area", Fixtures.BuildProductionStateValues()));
    }
}
