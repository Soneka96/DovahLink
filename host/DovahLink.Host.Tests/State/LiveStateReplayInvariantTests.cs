using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

/// <summary>
/// Invariants for Host-owned current state across public-client replacement: client connection
/// lifetime must never own, mutate, or gate the Host's current state. Every test establishes state
/// through the real capture chain; none pre-populates the feed.
/// </summary>
public class LiveStateReplayInvariantTests
{
    /// <summary>Builds a harness whose eight areas are synchronized from the default representative values.</summary>
    private static LivePipelineHarness BuildSynchronized()
    {
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(Fixtures.BuildProductionStateValues());
        return harness;
    }

    /// <summary>Orders a client's snapshots by area so two clients can be compared pairwise.</summary>
    /// <param name="client">The client whose received snapshots to order.</param>
    private static ReceivedSnapshot[] ByArea(LivePipelineClient client) =>
        client.Snapshots.OrderBy(snapshot => snapshot.StateArea, StringComparer.Ordinal).ToArray();

    /// <summary>Verifies a brand-new client receives exactly what the first client did, with no capture in between.</summary>
    [Fact]
    public void NewClient_AfterPreviousClientDisconnected_ReceivesSameBaselinesWithoutNewCapture()
    {
        LivePipelineHarness harness = BuildSynchronized();
        LivePipelineClient clientA = harness.CreateClient();
        clientA.Subscribe(LivePipelineHarness.ProductionAreas);
        Assert.Equal(LivePipelineHarness.ProductionAreas.Count, clientA.Snapshots.Count);
        clientA.Disconnect();
        long generationBefore = harness.AdapterTracker.CurrentConnectionGeneration;

        LivePipelineClient clientB = harness.CreateClient();
        clientB.Subscribe(LivePipelineHarness.ProductionAreas);

        ReceivedSnapshot[] first = ByArea(clientA);
        ReceivedSnapshot[] second = ByArea(clientB);
        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i].StateArea, second[i].StateArea);
            Assert.Equal(first[i].Revision, second[i].Revision);
            Assert.Equal(first[i].Data.GetRawText(), second[i].Data.GetRawText());
            Assert.Equal(first[i].StateAuthorityId, second[i].StateAuthorityId);
            Assert.Equal(harness.PlayContext.ToString(), second[i].PlayContextId);
            Assert.Equal(harness.CurrentRevision(second[i].StateArea).Value, second[i].Revision);
        }

        Assert.Equal(generationBefore, harness.AdapterTracker.CurrentConnectionGeneration);
        Assert.Empty(harness.Recovery.RecoveryRequests);
        Assert.Equal(0, clientB.ErrorCount);
    }

    /// <summary>Verifies the replayed values are the captured ones, not merely present.</summary>
    [Fact]
    public void NewClient_AfterPreviousClientDisconnected_ReceivesCapturedValues()
    {
        LivePipelineHarness harness = BuildSynchronized();
        harness.CreateClient().Subscribe(LivePipelineHarness.ProductionAreas);
        LivePipelineClient clientB = harness.CreateClient();

        clientB.Subscribe(LivePipelineHarness.ProductionAreas);

        Assert.Equal(43, ByArea(clientB).Single(s => s.StateArea == Constants.CharacterLevelStateArea).Data.GetProperty("value").GetInt32());
        Assert.Equal("Whiterun", ByArea(clientB).Single(s => s.StateArea == Constants.PlayerLocationStateArea).Data.GetProperty("value").GetProperty("locationName").GetString());
        Assert.Equal("Lydia", ByArea(clientB).Single(s => s.StateArea == Constants.CharacterIdentityStateArea).Data.GetProperty("value").GetProperty("name").GetString());
    }

    /// <summary>Verifies repeated client replacement, as repeated hot restarts, keeps replaying the same state.</summary>
    [Fact]
    public void NewClient_AfterManyReplacements_StillReceivesAllBaselines()
    {
        LivePipelineHarness harness = BuildSynchronized();

        for (int i = 0; i < 5; i++)
        {
            LivePipelineClient client = harness.CreateClient();
            Assert.Equal(LivePipelineHarness.ProductionAreas, client.Subscribe(LivePipelineHarness.ProductionAreas));
            Assert.Equal(LivePipelineHarness.ProductionAreas.Order(), client.Snapshots.Select(snapshot => snapshot.StateArea).Order());
            client.Disconnect();
        }
    }

    /// <summary>Verifies a later client subscribing to a subset receives exactly that subset's current baselines.</summary>
    [Fact]
    public void NewClient_SubscribingToSubset_ReceivesOnlyThoseCurrentBaselines()
    {
        LivePipelineHarness harness = BuildSynchronized();
        harness.CreateClient().Subscribe(LivePipelineHarness.ProductionAreas);
        string[] subset = [Constants.CharacterLevelStateArea, Constants.GameTimeStateArea, Constants.TrackedQuestsStateArea];
        LivePipelineClient client = harness.CreateClient();

        Assert.Equal(subset, client.Subscribe(subset));

        Assert.Equal(subset.Order(), client.Snapshots.Select(snapshot => snapshot.StateArea).Order());
    }

    /// <summary>Verifies a change captured between two client lifetimes is what the second client replays, with an advanced revision.</summary>
    [Fact]
    public void NewClient_AfterCaptureBetweenClients_ReplaysTheNewerValue()
    {
        LivePipelineHarness harness = BuildSynchronized();
        LivePipelineClient clientA = harness.CreateClient();
        clientA.Subscribe(LivePipelineHarness.ProductionAreas);
        clientA.Disconnect();
        ProductionStateValues next = Fixtures.BuildProductionStateValues(locationName: "Windhelm");
        harness.ApplySample(Constants.PlayerLocationStateArea, next.Location);

        LivePipelineClient clientB = harness.CreateClient();
        clientB.Subscribe(LivePipelineHarness.ProductionAreas);

        ReceivedSnapshot before = ByArea(clientA).Single(s => s.StateArea == Constants.PlayerLocationStateArea);
        ReceivedSnapshot after = ByArea(clientB).Single(s => s.StateArea == Constants.PlayerLocationStateArea);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal("Windhelm", after.Data.GetProperty("value").GetProperty("locationName").GetString());
    }

    /// <summary>Verifies a client's whole lifetime leaves the Host's replayable snapshots untouched.</summary>
    [Fact]
    public void ClientDisconnect_AfterSubscribe_LeavesHostSnapshotsUnchanged()
    {
        LivePipelineHarness harness = BuildSynchronized();
        Dictionary<string, StateSnapshotPublication> before = [];
        foreach (string area in LivePipelineHarness.ProductionAreas)
        {
            Assert.True(harness.Feed.TryGetSnapshot(new StateAreaId(area), out StateSnapshotPublication? snapshot));
            before[area] = snapshot!;
        }

        LivePipelineClient client = harness.CreateClient();
        client.Subscribe(LivePipelineHarness.ProductionAreas);
        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
        client.Disconnect();

        foreach (string area in LivePipelineHarness.ProductionAreas)
        {
            Assert.True(harness.Feed.TryGetSnapshot(new StateAreaId(area), out StateSnapshotPublication? after));
            Assert.Equal(before[area], after);
            Assert.True(harness.HasCurrentTypedValue(area));
        }
    }

    /// <summary>Verifies disconnecting a client that never subscribed is harmless and idempotent.</summary>
    [Fact]
    public void ClientDisconnect_WithoutSubscribe_IsIdempotentAndLeavesStateReplayable()
    {
        LivePipelineHarness harness = BuildSynchronized();
        LivePipelineClient client = harness.CreateClient();

        client.Disconnect();
        client.Disconnect();

        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }

    /// <summary>Verifies that a typed value the Host considers authoritative is also replayable, with the same revision, for every area.</summary>
    [Fact]
    public void SynchronizedHost_EveryAuthoritativeTypedValue_HasMatchingReplaySnapshot()
    {
        LivePipelineHarness harness = BuildSynchronized();

        foreach (string area in LivePipelineHarness.ProductionAreas)
        {
            Assert.True(harness.HasCurrentTypedValue(area), $"{area}: store has no current value");
            Assert.True(harness.Feed.TryGetSnapshot(new StateAreaId(area), out StateSnapshotPublication? snapshot), $"{area}: store has no replayable snapshot");
            Assert.Equal(harness.CurrentRevision(area), snapshot!.Revision);
        }
    }

    /// <summary>Verifies the typed and replay views stay in agreement through ordinary changed and unchanged samples and a Level Event.</summary>
    [Fact]
    public void SynchronizedHost_AfterOrdinaryUpdatesAndLevelEvent_TypedAndReplayViewsAgree()
    {
        LivePipelineHarness harness = BuildSynchronized();
        ProductionStateValues next = Fixtures.BuildProductionStateValues(locationName: "Riften");

        harness.ApplySample(Constants.PlayerLocationStateArea, next.Location);
        harness.ApplySample(Constants.PlayerLocationStateArea, next.Location);
        harness.ApplyLevelChanged(44);

        foreach (string area in LivePipelineHarness.ProductionAreas)
        {
            Assert.True(harness.HasCurrentTypedValue(area), $"{area}: store has no current value");
            Assert.True(harness.Feed.TryGetSnapshot(new StateAreaId(area), out StateSnapshotPublication? snapshot), $"{area}: store has no replayable snapshot");
            Assert.Equal(harness.CurrentRevision(area), snapshot!.Revision);
        }

        LivePipelineClient client = harness.CreateClient();
        client.Subscribe(LivePipelineHarness.ProductionAreas);
        Assert.Equal(44, ByArea(client).Single(s => s.StateArea == Constants.CharacterLevelStateArea).Data.GetProperty("value").GetInt32());
        Assert.Equal("Riften", ByArea(client).Single(s => s.StateArea == Constants.PlayerLocationStateArea).Data.GetProperty("value").GetProperty("locationName").GetString());
    }

    /// <summary>Verifies a play-context change leaves neither the typed nor the replay view serving the previous save.</summary>
    [Fact]
    public void PlayContextTransition_AfterSynchronization_ReplaysNothingFromThePreviousContext()
    {
        LivePipelineHarness harness = BuildSynchronized();

        harness.PlayContextTracker.NotifyTransition(DovahLink.Host.Identity.PlayContextId.NewId());

        Assert.Equal(LivePipelineHarness.ProductionAreas, harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
        Assert.All(LivePipelineHarness.ProductionAreas, area => Assert.False(harness.HasCurrentTypedValue(area)));
    }
}
