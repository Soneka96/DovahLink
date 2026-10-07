using DovahLink.Host.Client.Protocol;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

/// <summary>
/// Invariants for the moment resynchronization completion becomes observable: every required
/// area's current baseline must already be readable by then, and a client already waiting for a
/// baseline must receive every one without resubscribing, a new gameplay change, or a deadline.
/// All coordination is synchronous event hooks; nothing waits on a timer.
/// </summary>
public class LiveStateResynchronizationOrderingTests
{
    /// <summary>Every production area, as theory data, so each can be the last baseline to arrive.</summary>
    public static IEnumerable<object[]> EachProductionArea() =>
        LivePipelineHarness.ProductionAreas.Select(area => new object[] { area });

    /// <summary>Builds a harness that lost continuity, reconnected as a new generation, and began resynchronization with nothing applied yet.</summary>
    /// <param name="values">The values established before the loss.</param>
    private static LivePipelineHarness BuildGated(ProductionStateValues values)
    {
        var harness = new LivePipelineHarness();
        harness.EstablishInitialState(values);
        harness.LoseContinuity();
        harness.ConnectAdapter();
        harness.BeginResynchronization();
        return harness;
    }

    /// <summary>Lists the areas present in <paramref name="harness"/>'s feed right now, for failure messages.</summary>
    /// <param name="harness">The harness to inspect.</param>
    private static string DescribeMissing(LivePipelineHarness harness) =>
        string.Join(", ", harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));

    /// <summary>
    /// Verifies that at the instant <see cref="Adapter.IAdapterAvailabilityTracker.Resynchronized"/> is
    /// observed, every required area's baseline is already readable -- the invariant a consumer
    /// reacting to completion depends on. The Adapter plan is acknowledged first so the last baseline
    /// is the transaction's completing piece.
    /// </summary>
    /// <param name="lastArea">The area whose unchanged baseline arrives last and completes the transaction.</param>
    [Theory]
    [MemberData(nameof(EachProductionArea))]
    public void Resynchronized_ObservedOnTrackerEvent_EveryRequiredBaselineIsAlreadyReadable(string lastArea)
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildGated(values);
        string? missingWhenObserved = null;
        harness.AdapterTracker.Resynchronized += (_, _) => missingWhenObserved = DescribeMissing(harness);
        harness.AcceptAdapterPlan();

        harness.ApplyAllProductionBaselines(values, lastArea);

        Assert.NotNull(missingWhenObserved);
        Assert.True(
            missingWhenObserved.Length == 0,
            $"Resynchronized became observable while these baselines were not yet readable: [{missingWhenObserved}] (last baseline applied: {lastArea}).");
    }

    /// <summary>
    /// Verifies the same invariant on the feed's own <see cref="IStatePublicationFeed.SnapshotAvailabilityChanged"/>
    /// signal, which is the wake every waiting subscription actually reacts to.
    /// </summary>
    /// <param name="lastArea">The area whose unchanged baseline arrives last and completes the transaction.</param>
    [Theory]
    [MemberData(nameof(EachProductionArea))]
    public void SnapshotAvailabilityChanged_RaisedByCompletion_EveryRequiredBaselineIsAlreadyReadable(string lastArea)
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildGated(values);
        List<string> missingPerWake = [];
        harness.Feed.SnapshotAvailabilityChanged += () => missingPerWake.Add(DescribeMissing(harness));
        harness.AcceptAdapterPlan();

        harness.ApplyAllProductionBaselines(values, lastArea);

        string missing = Assert.Single(missingPerWake);
        Assert.True(
            missing.Length == 0,
            $"The only availability wake fired while these baselines were not yet readable: [{missing}] (last baseline applied: {lastArea}).");
    }

    /// <summary>Control: when the Adapter plan acknowledgement is the completing piece, every baseline is readable at completion.</summary>
    [Fact]
    public void Resynchronized_WhenAdapterPlanCompletesTransaction_EveryRequiredBaselineIsAlreadyReadable()
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildGated(values);
        string? missingWhenObserved = null;
        harness.AdapterTracker.Resynchronized += (_, _) => missingWhenObserved = DescribeMissing(harness);
        harness.ApplyAllProductionBaselines(values);

        harness.AcceptAdapterPlan();

        Assert.Equal(string.Empty, missingWhenObserved);
    }

    /// <summary>Control: when the completing baseline changed value, the wake is a change publication and the baseline is readable by then.</summary>
    /// <param name="lastArea">The area whose changed baseline arrives last and completes the transaction.</param>
    [Theory]
    [MemberData(nameof(EachProductionArea))]
    public void Resynchronized_WhenCompletingBaselineChangedValue_ChangedAreaIsReadableOnItsPublication(string lastArea)
    {
        ProductionStateValues before = Fixtures.BuildProductionStateValues(level: 43, locationName: "Whiterun", characterName: "Lydia");
        ProductionStateValues after = Fixtures.BuildProductionStateValues(level: 44, locationName: "Riften", characterName: "Serana");
        LivePipelineHarness harness = BuildGated(before);
        bool readableOnPublication = false;
        harness.Feed.SnapshotChanged += snapshot =>
        {
            if (snapshot.StateArea.Value == lastArea)
            {
                readableOnPublication = harness.Feed.TryGetSnapshot(snapshot.StateArea, out _);
            }
        };
        harness.AcceptAdapterPlan();

        harness.ApplyAllProductionBaselines(after, lastArea);

        // Areas whose Fixtures value does not differ between the two sets are unchanged baselines; skip them here.
        bool lastAreaChanged = lastArea is Constants.CharacterLevelStateArea or Constants.PlayerLocationStateArea or Constants.CharacterIdentityStateArea;
        if (lastAreaChanged)
        {
            Assert.True(readableOnPublication, $"{lastArea}: changed completing baseline was not readable when its publication was raised.");
        }

        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }

    /// <summary>
    /// Verifies a client that subscribed while resynchronization was outstanding receives every
    /// accepted area's baseline once resynchronization completes, even when the completing
    /// baseline is unchanged -- without resubscribing, a new gameplay change, or waiting out a deadline.
    /// </summary>
    /// <param name="lastArea">The area whose unchanged baseline arrives last and completes the transaction.</param>
    [Theory]
    [MemberData(nameof(EachProductionArea))]
    public void WaitingSubscriber_SameValueResynchronization_ReceivesEveryBaselineWithoutResubscribing(string lastArea)
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildGated(values);
        LivePipelineClient client = harness.CreateClient();
        client.Subscribe(LivePipelineHarness.ProductionAreas);
        Assert.Empty(client.Snapshots);
        harness.AcceptAdapterPlan();

        harness.ApplyAllProductionBaselines(values, lastArea);

        string[] received = client.Snapshots.Select(snapshot => snapshot.StateArea).ToArray();
        string[] missing = LivePipelineHarness.ProductionAreas.Except(received).ToArray();
        Assert.True(
            missing.Length == 0,
            $"Waiting subscriber never received: [{string.Join(", ", missing)}] (last baseline applied: {lastArea}); "
            + $"feed readable for them now: [{string.Join(", ", missing.Where(area => harness.Feed.TryGetSnapshot(new StateAreaId(area), out _)))}].");
        Assert.Equal(0, client.ErrorCount);
    }

    /// <summary>Control: a waiting subscriber still receives every baseline when the last area's value changed, because a change publication wakes it.</summary>
    [Fact]
    public void WaitingSubscriber_WhenLastBaselineChangedValue_ReceivesEveryBaseline()
    {
        ProductionStateValues before = Fixtures.BuildProductionStateValues(level: 43);
        ProductionStateValues after = Fixtures.BuildProductionStateValues(level: 44);
        LivePipelineHarness harness = BuildGated(before);
        LivePipelineClient client = harness.CreateClient();
        client.Subscribe(LivePipelineHarness.ProductionAreas);
        harness.AcceptAdapterPlan();

        harness.ApplyAllProductionBaselines(after, lastArea: Constants.CharacterLevelStateArea);

        Assert.Equal(LivePipelineHarness.ProductionAreas.Order(), client.Snapshots.Select(snapshot => snapshot.StateArea).Order());
        Assert.Equal(44, client.Snapshots.Single(snapshot => snapshot.StateArea == Constants.CharacterLevelStateArea).Data.GetProperty("value").GetInt32());
    }

    /// <summary>
    /// Verifies a pending <c>snapshot_request</c> on an area that was never subscribed is answered once
    /// resynchronization completes with an unchanged last baseline.
    /// </summary>
    /// <param name="lastArea">The area whose unchanged baseline arrives last and completes the transaction.</param>
    [Theory]
    [MemberData(nameof(EachProductionArea))]
    public void PendingSnapshotRequest_SameValueResynchronization_IsAnsweredWhenCompletionBecomesObservable(string lastArea)
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildGated(values);
        LivePipelineClient client = harness.CreateClient();
        Assert.True(client.Subscription.HandleSnapshotRequest(lastArea, "request-1"));
        Assert.Empty(client.Snapshots);
        harness.AcceptAdapterPlan();

        harness.ApplyAllProductionBaselines(values, lastArea);

        ReceivedSnapshot answer = Assert.Single(client.Snapshots);
        Assert.Equal(lastArea, answer.StateArea);
        Assert.Equal(0, client.ErrorCount);
    }

    /// <summary>
    /// Characterization: once the Apply call returns, the last unchanged baseline IS readable. Together
    /// with the ordering tests above, this distinguishes "the data is never restored" from "it is restored
    /// only after the completion signal already fired".
    /// </summary>
    /// <param name="lastArea">The area whose unchanged baseline arrives last and completes the transaction.</param>
    [Theory]
    [MemberData(nameof(EachProductionArea))]
    public void SameValueResynchronization_AfterLastBaselineReturns_FeedHoldsEveryBaseline(string lastArea)
    {
        ProductionStateValues values = Fixtures.BuildProductionStateValues();
        LivePipelineHarness harness = BuildGated(values);
        harness.AcceptAdapterPlan();

        harness.ApplyAllProductionBaselines(values, lastArea);

        Assert.Empty(harness.UnreadableAreas(LivePipelineHarness.ProductionAreas));
    }
}
