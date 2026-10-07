using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

/// <summary>
/// Proves the live-state infrastructure is generic: areas registered only through a custom catalog,
/// with value types production never mentions, inherit the same replay and resynchronization
/// guarantees as the eight production areas. A hardcoded dependency on the production domains would fail these.
/// </summary>
public class LiveStateGenericAreaTests
{
    /// <summary>The first invented area, carrying a string value.</summary>
    private const string FutureArea = "future_area";

    /// <summary>The second invented area, carrying an integer value, so no single value type is assumed.</summary>
    private const string FutureAreaTwo = "future_area_two";

    /// <summary>The invented areas together, in catalog order.</summary>
    private static readonly string[] FutureAreas = [FutureArea, FutureAreaTwo];

    /// <summary>The generic chain under test: a harness over the custom catalog and one publisher per invented value type.</summary>
    /// <param name="Harness">The composed real chain.</param>
    /// <param name="Text">The publisher for <see cref="FutureArea"/>.</param>
    /// <param name="Number">The publisher for <see cref="FutureAreaTwo"/>.</param>
    private sealed record Chain(LivePipelineHarness Harness, IStatePublisher<string?> Text, IStatePublisher<int?> Number);

    /// <summary>Builds a catalog whose only state areas are the invented ones, each with a required baseline Sample.</summary>
    private static LiveStateCatalog BuildFutureCatalog() =>
        new(
            [
                new CaptureUnitDefinition(CaptureSourceKind.Sample, 9001, RateClass.Slow, SynchronizationRole.BaselineSample, [new StateAreaId(FutureArea)]),
                new CaptureUnitDefinition(CaptureSourceKind.Sample, 9002, RateClass.Slow, SynchronizationRole.BaselineSample, [new StateAreaId(FutureAreaTwo)]),
            ],
            [
                new StateAreaDefinition(new StateAreaId(FutureArea), UpdateMode.Snapshot),
                new StateAreaDefinition(new StateAreaId(FutureAreaTwo), UpdateMode.Snapshot),
            ]);

    /// <summary>Builds the generic chain and, unless asked not to, synchronizes both invented areas through the real resynchronization path.</summary>
    /// <param name="synchronize">Whether to connect and baseline both areas.</param>
    private static Chain BuildChain(bool synchronize = true)
    {
        var harness = new LivePipelineHarness(BuildFutureCatalog());
        var chain = new Chain(harness, harness.CreatePublisher<string?>(), harness.CreatePublisher<int?>());
        if (synchronize)
        {
            harness.ConnectAdapter();
            ApplyFutureBaselines(chain, "alpha", 7);
            harness.AcceptAdapterPlan();
        }

        return chain;
    }

    /// <summary>Begins resynchronization for the current connection if needed and applies both invented baselines.</summary>
    /// <param name="chain">The generic chain.</param>
    /// <param name="text">The <see cref="FutureArea"/> baseline value.</param>
    /// <param name="number">The <see cref="FutureAreaTwo"/> baseline value.</param>
    /// <param name="secondLast">Whether <see cref="FutureArea"/> should arrive last instead of <see cref="FutureAreaTwo"/>.</param>
    private static void ApplyFutureBaselines(Chain chain, string text, int number, bool secondLast = false)
    {
        chain.Harness.BeginResynchronization();
        if (secondLast)
        {
            chain.Harness.ApplyBaseline(chain.Number, FutureAreaTwo, (int?)number);
            chain.Harness.ApplyBaseline(chain.Text, FutureArea, text);
        }
        else
        {
            chain.Harness.ApplyBaseline(chain.Text, FutureArea, text);
            chain.Harness.ApplyBaseline(chain.Number, FutureAreaTwo, (int?)number);
        }
    }

    /// <summary>Verifies the infrastructure registers and serves exactly the catalog's areas, not the production eight.</summary>
    [Fact]
    public void CustomCatalog_RegistersOnlyItsOwnAreas()
    {
        Chain chain = BuildChain(synchronize: false);

        Assert.Equal(2, chain.Harness.Registered.Count);
        Assert.True(chain.Harness.Registered.IsRegistered(new StateAreaId(FutureArea)));
        Assert.False(chain.Harness.Registered.IsRegistered(new StateAreaId(Constants.CharacterLevelStateArea)));
        LivePipelineClient client = chain.Harness.CreateClient();
        Assert.Equal(FutureAreas, client.Subscribe([.. FutureAreas, Constants.CharacterLevelStateArea]));
    }

    /// <summary>Verifies an invented area's current state survives client replacement and is replayed without a new capture.</summary>
    [Fact]
    public void FutureArea_AfterClientReplacement_ReplaysSameBaselines()
    {
        Chain chain = BuildChain();
        LivePipelineClient clientA = chain.Harness.CreateClient();
        clientA.Subscribe(FutureAreas);
        clientA.Disconnect();

        LivePipelineClient clientB = chain.Harness.CreateClient();
        clientB.Subscribe(FutureAreas);

        Assert.Equal(FutureAreas.Order(), clientB.Snapshots.Select(snapshot => snapshot.StateArea).Order());
        foreach (ReceivedSnapshot received in clientB.Snapshots)
        {
            ReceivedSnapshot original = clientA.Snapshots.Single(snapshot => snapshot.StateArea == received.StateArea);
            Assert.Equal(original.Data.GetRawText(), received.Data.GetRawText());
            Assert.Equal(original.Revision, received.Revision);
        }

        Assert.Equal("alpha", clientB.Snapshots.Single(snapshot => snapshot.StateArea == FutureArea).Data.GetProperty("value").GetString());
        Assert.Equal(7, clientB.Snapshots.Single(snapshot => snapshot.StateArea == FutureAreaTwo).Data.GetProperty("value").GetInt32());
    }

    /// <summary>Verifies the typed and replay views agree for invented areas, as for production ones.</summary>
    [Fact]
    public void FutureArea_WhenSynchronized_TypedValueAndReplaySnapshotAgree()
    {
        Chain chain = BuildChain();

        Assert.True(chain.Text.TryGetCurrentValue(new StateAreaId(FutureArea), out string? text));
        Assert.Equal("alpha", text);
        Assert.True(chain.Number.TryGetCurrentValue(new StateAreaId(FutureAreaTwo), out int? number));
        Assert.Equal(7, number);
        Assert.Empty(chain.Harness.UnreadableAreas(FutureAreas));
        Assert.True(chain.Harness.Feed.TryGetSnapshot(new StateAreaId(FutureArea), out StateSnapshotPublication? snapshot));
        Assert.Equal(chain.Text.CurrentRevision(new StateAreaId(FutureArea)), snapshot!.Revision);
    }

    /// <summary>Verifies continuity loss gates invented areas, and a same-value resynchronization makes them replayable to a new client.</summary>
    /// <param name="futureAreaLast">Whether <see cref="FutureArea"/> (rather than <see cref="FutureAreaTwo"/>) arrives last.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FutureArea_AfterSameValueResynchronization_IsReplayableToNewClient(bool futureAreaLast)
    {
        Chain chain = BuildChain();
        chain.Harness.LoseContinuity();
        Assert.Equal(FutureAreas, chain.Harness.UnreadableAreas(FutureAreas));
        chain.Harness.ConnectAdapter();

        ApplyFutureBaselines(chain, "alpha", 7, secondLast: futureAreaLast);
        chain.Harness.AcceptAdapterPlan();

        Assert.False(chain.Harness.AdapterTracker.NeedsResynchronization);
        Assert.Empty(chain.Harness.UnreadableAreas(FutureAreas));
        LivePipelineClient client = chain.Harness.CreateClient();
        client.Subscribe(FutureAreas);
        Assert.Equal(FutureAreas.Order(), client.Snapshots.Select(snapshot => snapshot.StateArea).Order());
    }

    /// <summary>Verifies completion is gated by the custom catalog's own required areas, not by the production set.</summary>
    [Fact]
    public void FutureArea_ResynchronizationWithOneBaselineMissing_StaysGated()
    {
        Chain chain = BuildChain();
        chain.Harness.LoseContinuity();
        chain.Harness.ConnectAdapter();
        chain.Harness.BeginResynchronization();
        chain.Harness.ApplyBaseline(chain.Text, FutureArea, "alpha");

        chain.Harness.AcceptAdapterPlan();

        Assert.True(chain.Harness.AdapterTracker.NeedsResynchronization);
        Assert.Equal(FutureAreas, chain.Harness.UnreadableAreas(FutureAreas));
    }

    /// <summary>Verifies a changed invented baseline is published as a change and replayed with its new value.</summary>
    [Fact]
    public void FutureArea_ResynchronizationWithChangedValue_PublishesAndReplaysNewValue()
    {
        Chain chain = BuildChain();
        chain.Harness.LoseContinuity();
        chain.Harness.ConnectAdapter();
        List<string> changed = [];
        chain.Harness.Feed.SnapshotChanged += snapshot => changed.Add(snapshot.StateArea.Value);

        ApplyFutureBaselines(chain, "beta", 7);
        chain.Harness.AcceptAdapterPlan();

        Assert.Equal([FutureArea], changed);
        LivePipelineClient client = chain.Harness.CreateClient();
        client.Subscribe(FutureAreas);
        Assert.Equal("beta", client.Snapshots.Single(snapshot => snapshot.StateArea == FutureArea).Data.GetProperty("value").GetString());
    }

    /// <summary>
    /// Verifies an invented area inherits the completion-ordering guarantee: a client waiting during a
    /// same-value resynchronization receives every baseline, including the unchanged last one.
    /// </summary>
    /// <param name="futureAreaLast">Whether <see cref="FutureArea"/> (rather than <see cref="FutureAreaTwo"/>) arrives last.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FutureArea_WaitingSubscriberDuringSameValueResynchronization_ReceivesEveryBaseline(bool futureAreaLast)
    {
        Chain chain = BuildChain();
        chain.Harness.LoseContinuity();
        chain.Harness.ConnectAdapter();
        chain.Harness.BeginResynchronization();
        LivePipelineClient client = chain.Harness.CreateClient();
        client.Subscribe(FutureAreas);
        chain.Harness.AcceptAdapterPlan();

        ApplyFutureBaselines(chain, "alpha", 7, secondLast: futureAreaLast);

        string[] missing = FutureAreas.Except(client.Snapshots.Select(snapshot => snapshot.StateArea)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"Waiting subscriber never received: [{string.Join(", ", missing)}] (last baseline applied: {(futureAreaLast ? FutureArea : FutureAreaTwo)}).");
    }
}
