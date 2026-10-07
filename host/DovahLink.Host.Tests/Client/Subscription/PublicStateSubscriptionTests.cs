using System.Text.Json;
using DovahLink.Host;
using DovahLink.Host.Adapter;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Subscription;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Client.Subscription;

/// <summary>Tests for <see cref="PublicStateSubscription"/>.</summary>
public class PublicStateSubscriptionTests
{
    /// <summary>The default Host authority shared by publication fixtures and their envelope codec.</summary>
    private static readonly IStateAuthorityLifecycle DefaultStateAuthorityLifecycle = Fixtures.BuildStateAuthorityLifecycle();

    /// <summary>The envelope codec used to decode messages from subscriptions using the default authority.</summary>
    private static readonly PublicEnvelopeCodec codec = new(DefaultStateAuthorityLifecycle);

    /// <summary>Builds a subscription over a fresh policy, feed, play-context tracker, and state-authority lifecycle, with the given areas pre-registered.</summary>
    /// <param name="registeredAreas">The state areas to register before the test runs.</param>
    /// <param name="feed">The feed the subscription reads from; a fresh <see cref="FakeStatePublicationFeed"/> when omitted.</param>
    /// <param name="playContextTracker">The tracker the subscription reads and listens to; a fresh <see cref="FakePlayContextTracker"/> when omitted.</param>
    /// <param name="stateAuthorityLifecycle">The lifecycle the subscription listens to for rotation; a fresh <see cref="Fixtures.BuildStateAuthorityLifecycle"/> when omitted.</param>
    /// <param name="pendingBaselineDeadline">
    /// How long a pending baseline waits before timing out; a long, effectively-never-fires-during-a-
    /// test value when omitted, so no test unrelated to that specific behavior can be made flaky by a
    /// background timeout landing mid-run. Tests that specifically exercise the timeout pass a short,
    /// explicit value instead.
    /// </param>
    /// <param name="envelopeCodec">The codec bound to <paramref name="stateAuthorityLifecycle"/>, when supplied.</param>
    private (PublicStateSubscription Subscription, RegisteredStateAreaPolicy Policy, FakeStatePublicationFeed Feed) BuildSubscription(
        IEnumerable<string>? registeredAreas = null, FakeStatePublicationFeed? feed = null, IPlayContextTracker? playContextTracker = null,
        IStateAuthorityLifecycle? stateAuthorityLifecycle = null, TimeSpan? pendingBaselineDeadline = null,
        IPublicEnvelopeCodec? envelopeCodec = null)
    {
        var policy = new RegisteredStateAreaPolicy();
        foreach (string area in registeredAreas ?? [])
        {
            policy.TryRegister(new StateAreaId(area));
        }

        FakeStatePublicationFeed resolvedFeed = feed ?? new FakeStatePublicationFeed();
        IStateAuthorityLifecycle resolvedAuthority = stateAuthorityLifecycle ?? DefaultStateAuthorityLifecycle;
        resolvedFeed.CurrentStateAuthorityId = resolvedAuthority.Current;
        resolvedFeed.CurrentStateAuthorityIdProvider = () => resolvedAuthority.Current;
        var subscription = new PublicStateSubscription(
            policy, resolvedFeed, envelopeCodec ?? (stateAuthorityLifecycle is null ? codec : new PublicEnvelopeCodec(resolvedAuthority)),
            playContextTracker ?? new FakePlayContextTracker(), resolvedAuthority,
            pendingBaselineDeadline ?? TimeSpan.FromSeconds(30));
        return (subscription, policy, resolvedFeed);
    }

    /// <summary>Builds a real store, feed view, and subscription whose adapter is available but still requires resynchronization.</summary>
    /// <param name="area">The single registered area for the subscription.</param>
    /// <param name="pendingBaselineDeadline">The deadline for a baseline that remains unavailable.</param>
    /// <param name="retainedValue">A value the store already holds for <paramref name="area"/> from before a continuity loss, or <see langword="null"/> for an empty store.</param>
    /// <returns>The subscription, feed, store, and current resynchronization authorization needed by the test.</returns>
    private (PublicStateSubscription Subscription, StatePublicationFeed Feed, FakeAdapterAvailabilityTracker AdapterTracker, FakePlayContextTracker PlayContextTracker, AdapterInstanceId AdapterInstanceId, IAdapterResynchronizationToken ResynchronizationToken, IAuthoritativeStateStore Store)
        BuildResynchronizingSubscription(string area, TimeSpan pendingBaselineDeadline, int? retainedValue = null)
    {
        var adapterTracker = new FakeAdapterAvailabilityTracker
        {
            Current = AdapterAvailability.Available,
            CurrentConnectionGeneration = 1,
            NeedsResynchronization = true,
        };
        AdapterInstanceId adapterInstanceId = adapterTracker.CurrentInstanceId!.Value;
        IAdapterResynchronizationToken resynchronizationToken = adapterTracker.TryClaimResynchronizationToken()
            ?? throw new InvalidOperationException("The test adapter did not provide a resynchronization token.");
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var policy = new RegisteredStateAreaPolicy();
        policy.TryRegister(new StateAreaId(area));
        IStateAuthorityLifecycle authorityLifecycle = Fixtures.BuildStateAuthorityLifecycle();
        var store = new AuthoritativeStateStore(adapterTracker, playContextTracker, policy, authorityLifecycle);
        if (retainedValue is not null)
        {
            // Seed the value as an ordinary capture, then lose currentness the way a continuity loss does,
            // so the baseline the test applies later is an unchanged resynchronization baseline.
            adapterTracker.NeedsResynchronization = false;
            store.Apply(adapterInstanceId, adapterTracker.CurrentConnectionGeneration, playContextTracker.Current!.Value,
                playContextTracker.TransitionGeneration, DateTimeOffset.UtcNow, new StateAreaId(area), retainedValue.Value);
            adapterTracker.PublishTransition(new AdapterAvailabilityTransition(
                AdapterAvailability.Unavailable, AdapterAvailability.Available, adapterInstanceId, adapterTracker.CurrentConnectionGeneration));
            adapterTracker.NeedsResynchronization = true;
        }

        var feed = new StatePublicationFeed(store);
        var subscription = new PublicStateSubscription(
            policy, feed, new PublicEnvelopeCodec(authorityLifecycle), playContextTracker, authorityLifecycle, pendingBaselineDeadline);
        return (subscription, feed, adapterTracker, playContextTracker, adapterInstanceId, resynchronizationToken, store);
    }

    /// <summary>
    /// Drives a full <c>subscribe</c> exchange the way a caller with no competing Control/Recovery
    /// lane send of its own would: the decision-only <see cref="PublicStateSubscription.HandleSubscribe"/>
    /// followed by the deferred-error flush and baseline delivery for whatever it accepted. The
    /// production caller sends the ACK between reconciliation and that flush; most tests care about
    /// the combined outcome, not the call split itself.
    /// </summary>
    /// <param name="subscription">The subscription under test.</param>
    /// <param name="subscribeMessageId">The <c>subscribe</c> message's own id.</param>
    /// <param name="requestedStateAreas">The state areas the client requested.</param>
    /// <param name="reservedControlCapacity">Forwarded to <see cref="PublicStateSubscription.HandleSubscribe"/>; zero when omitted, since these tests send no competing message of their own.</param>
    private static (IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected) Subscribe(
        PublicStateSubscription subscription, string subscribeMessageId, IReadOnlyList<string> requestedStateAreas, int reservedControlCapacity = 0)
    {
        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe(
            requestedStateAreas,
            reservedControlCapacity,
            baselineCorrelationMessageId: subscribeMessageId);
        subscription.SendSupersededSnapshotRequestErrors();
        subscription.EstablishAcceptedBaselines(accepted, subscribeMessageId);
        return (accepted, rejected);
    }

    /// <summary>Reads the desired state areas from one canonical subscription fixture.</summary>
    /// <param name="fixtureName">The fixture filename under the shared subscriptions directory.</param>
    /// <returns>The complete desired state-area set encoded by the fixture.</returns>
    private static IReadOnlyList<string> ReadSubscribeAreas(string fixtureName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", "subscriptions", fixtureName);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("payload").GetProperty("stateAreas")
            .EnumerateArray().Select(area => area.GetString()!).ToArray();
    }

    /// <summary>Builds a representative snapshot value for the given area.</summary>
    private static StateSnapshotPublication BuildSnapshot(
        string area, ulong revision = 1, PlayContextId? playContextId = null, long playContextGeneration = 0, StateAuthorityId? stateAuthorityId = null) =>
        new(new StateAreaId(area), stateAuthorityId ?? DefaultStateAuthorityLifecycle.Current, new RevisionNumber(revision), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { value = 42 }), playContextId, playContextGeneration);

    /// <summary>Builds a representative event value for the given area.</summary>
    private static StateEventPublication BuildEvent(
        string area, ulong baseRevision, ulong revision, PlayContextId? playContextId = null, long playContextGeneration = 0, StateAuthorityId? stateAuthorityId = null) =>
        new(new StateAreaId(area), stateAuthorityId ?? DefaultStateAuthorityLifecycle.Current, new RevisionNumber(baseRevision), new RevisionNumber(revision), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { value = 99 }), playContextId, playContextGeneration);

    /// <summary>Verifies that a requested area which is registered is accepted.</summary>
    [Fact]
    public void HandleSubscribe_RegisteredArea_Accepts()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Empty(rejected);
    }

    /// <summary>Verifies that a requested area which is not registered is rejected.</summary>
    [Fact]
    public void HandleSubscribe_UnregisteredArea_Rejects()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription();
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Empty(accepted);
        Assert.Equal(["area_a"], rejected);
    }

    /// <summary>Verifies that a mix of registered and unregistered requested areas is partitioned correctly.</summary>
    [Fact]
    public void HandleSubscribe_MixedAreas_PartitionsCorrectly()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = Subscribe(subscription, "sub-1", ["area_a", "area_b"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Equal(["area_b"], rejected);
    }

    /// <summary>Verifies that a snapshot already current before subscription is sent as the baseline without a later mutation.</summary>
    [Fact]
    public void HandleSubscribe_RegisteredAreaWithAvailableSnapshot_SendsBaselineOnControlLaneCorrelatedToSubscribeMessageId()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 7));
        var connectionContext = new FakePublicConnectionContext();
        var sessionId = SessionId.NewId();
        subscription.Bind(connectionContext, sessionId);

        Subscribe(subscription, "sub-1", ["area_a"]);

        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal("sub-1", envelope.CorrelationId);
        Assert.Equal(sessionId.ToString(), envelope.SessionId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal("area_a", payload!.StateArea);
        Assert.Equal(7UL, payload.Revision);
        Assert.Equal(42, payload.Data.GetProperty("value").GetInt32());
    }

    /// <summary>
    /// Verifies the core reason <see cref="PublicStateSubscription.HandleSubscribe"/> and
    /// <see cref="PublicStateSubscription.EstablishAcceptedBaselines"/> are two separate calls: the
    /// decision alone sends nothing, so a caller can guarantee its own message (a <c>subscription_ack</c>)
    /// reaches the Control/Recovery lane first.
    /// </summary>
    [Fact]
    public void HandleSubscribe_DoesNotSendUntilEstablishAcceptedBaselinesCalled()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, _) = subscription.HandleSubscribe(["area_a"], reservedControlCapacity: 0);
        Assert.Empty(connectionContext.SentPayloads);

        subscription.EstablishAcceptedBaselines(accepted, "sub-1");
        Assert.Single(connectionContext.SentPayloads);
    }

    /// <summary>
    /// Verifies that requesting more new areas than the Control/Recovery lane has spare capacity for
    /// -- after reserving the caller's own upcoming send -- accepts only as many as fit and rejects
    /// the rest, rather than accepting all of them and later overflowing the lane.
    /// </summary>
    [Fact]
    public void HandleSubscribe_MoreNewAreasThanControlCapacity_AcceptsOnlyWhatFitsAndRejectsTheRest()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a", "area_b", "area_c"]);
        var connectionContext = new FakePublicConnectionContext { RemainingOutboundCapacityResult = 2 };
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe(
            ["area_a", "area_b", "area_c"], reservedControlCapacity: 1); // budget = 2 - 1 = 1 area

        Assert.Equal(["area_a"], accepted);
        Assert.Equal(["area_b", "area_c"], rejected);
    }

    /// <summary>Verifies that a reservation already at or beyond the lane's remaining capacity clamps the budget to zero rather than going negative, rejecting every area that needs a baseline.</summary>
    [Fact]
    public void HandleSubscribe_ReservedCapacityAtOrBeyondRemaining_RejectsEveryAreaNeedingBaseline()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext { RemainingOutboundCapacityResult = 0 };
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe(["area_a"], reservedControlCapacity: 1);

        Assert.Empty(accepted);
        Assert.Equal(["area_a"], rejected);
    }

    /// <summary>Verifies that an already-live area does not consume any of the reserved-capacity budget, since it needs no baseline resend.</summary>
    [Fact]
    public void HandleSubscribe_AlreadyLiveAreaAmongNewOnes_DoesNotConsumeCapacityBudget()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a", "area_b"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]); // area_a is now live

        connectionContext.RemainingOutboundCapacityResult = 1;
        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe(
            ["area_a", "area_b"], reservedControlCapacity: 0); // budget = 1; area_a is free, area_b spends the only slot

        Assert.Equal(["area_a", "area_b"], accepted);
        Assert.Empty(rejected);
    }

    /// <summary>Verifies that each subscribe request replaces this connection's desired set and removed areas stop publishing.</summary>
    [Fact]
    public void HandleSubscribe_ReconcilesCompleteDesiredSetAndStopsRemovedAreas()
    {
        IReadOnlyList<string> areaA = ReadSubscribeAreas("subscribe.json");
        IReadOnlyList<string> areaAB = ReadSubscribeAreas("subscribe-add-area.json");
        IReadOnlyList<string> areaB = ReadSubscribeAreas("subscribe-replacement.json");
        IReadOnlyList<string> empty = ReadSubscribeAreas("subscribe-empty.json");
        string firstArea = areaA.Single();
        string secondArea = areaB.Single();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(areaAB);
        feed.SetSnapshot(new StateAreaId(firstArea), BuildSnapshot(firstArea));
        feed.SetSnapshot(new StateAreaId(secondArea), BuildSnapshot(secondArea));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-1", areaA);
        Subscribe(subscription, "sub-2", areaAB);
        int sentBeforeRepeat = connectionContext.SentPayloads.Count;
        Subscribe(subscription, "sub-3", areaAB);
        Assert.Equal(sentBeforeRepeat, connectionContext.SentPayloads.Count);

        Subscribe(subscription, "sub-4", areaB);
        connectionContext.SentPayloads.Clear();
        connectionContext.SentSnapshots.Clear();
        feed.RaiseEvent(BuildEvent(firstArea, 1, 2));
        feed.RaiseEvent(BuildEvent(secondArea, 1, 2));
        feed.RaiseSnapshotChanged(BuildSnapshot(firstArea, revision: 2));
        feed.RaiseSnapshotChanged(BuildSnapshot(secondArea, revision: 2));

        (byte[] eventBytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(eventBytes, out PublicEnvelope? eventEnvelope));
        Assert.Equal(PublicMessageType.StateEvent, eventEnvelope!.MessageType);
        Assert.True(codec.TryDecodePayload(eventEnvelope, out StateEventPayload? eventPayload));
        Assert.Equal(secondArea, eventPayload!.StateArea);
        (StateAreaId snapshotArea, _) = Assert.Single(connectionContext.SentSnapshots);
        Assert.Equal(secondArea, snapshotArea.Value);

        Subscribe(subscription, "sub-5", empty);
        connectionContext.SentPayloads.Clear();
        connectionContext.SentSnapshots.Clear();
        feed.RaiseEvent(BuildEvent(secondArea, 2, 3));
        feed.RaiseSnapshotChanged(BuildSnapshot(secondArea, revision: 3));

        Assert.Empty(connectionContext.SentPayloads);
        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that duplicate areas in one desired set are acknowledged once and establish only one baseline.</summary>
    [Fact]
    public void HandleSubscribe_DuplicateAreasAreIdempotent()
    {
        const string area = "area_a";
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription([area]);
        feed.SetSnapshot(new StateAreaId(area), BuildSnapshot(area));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = Subscribe(
            subscription, "sub-1", [area, area]);

        Assert.Equal([area], accepted);
        Assert.Empty(rejected);
        Assert.Single(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that removing an area cancels its pending baseline timeout.</summary>
    [Fact]
    public async Task HandleSubscribe_RemovingPendingAreaCancelsItsBaselineTimeout()
    {
        const string area = "area_a";
        (PublicStateSubscription subscription, _, _) = BuildSubscription(
            [area], pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-1", [area]);
        Subscribe(subscription, "sub-2", []);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Empty(connectionContext.SentPayloads);
        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that a boundary's admitted R0 keeps an accepted area live even when no spare baseline capacity remains.</summary>
    [Fact]
    public void HandleSubscribe_BoundaryBaselineKeepsPreviouslyActiveAreaAccepted()
    {
        const string area = "area_a";
        var playContextTracker = new FakePlayContextTracker();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            [area], playContextTracker: playContextTracker);
        feed.SetSnapshot(new StateAreaId(area), BuildSnapshot(area));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", [area]);
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        connectionContext.RemainingOutboundCapacityResult = 0;

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) =
            subscription.HandleSubscribe([area], reservedControlCapacity: 1);
        subscription.EstablishAcceptedBaselines(accepted, "sub-2");
        connectionContext.SentPayloads.Clear();
        connectionContext.SentSnapshots.Clear();
        feed.RaiseEvent(BuildEvent(
            area,
            0,
            1,
            playContextId: playContextTracker.Current,
            playContextGeneration: playContextTracker.TransitionGeneration));
        feed.RaiseSnapshotChanged(BuildSnapshot(
            area,
            revision: 1,
            playContextId: playContextTracker.Current,
            playContextGeneration: playContextTracker.TransitionGeneration));

        Assert.Equal([area], accepted);
        Assert.Empty(rejected);
        Assert.Single(connectionContext.SentPayloads);
        Assert.Single(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that <see cref="PublicStateSubscription.EstablishAcceptedBaselines"/> does not resend a baseline for an area that is already live.</summary>
    [Fact]
    public void EstablishAcceptedBaselines_AlreadyLiveArea_DoesNotResend()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]); // area_a is now live
        int sentAfterFirstBaseline = connectionContext.SentPayloads.Count;

        subscription.EstablishAcceptedBaselines(["area_a"], "sub-2");

        Assert.Equal(sentAfterFirstBaseline, connectionContext.SentPayloads.Count);
    }

    /// <summary>
    /// Verifies that an accepted subscribe area with no value available at subscribe time reuses the
    /// same bounded pending-baseline machinery as snapshot_request: the baseline is delivered
    /// automatically once a value becomes available, without a second explicit client request.
    /// </summary>
    [Fact]
    public void EstablishAcceptedBaselines_AcceptedAreaWithNoSnapshotAtSubscribeTime_LaterSnapshotAutomaticallyDelivers()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, _) = Subscribe(subscription, "sub-1", ["area_a"]);
        Assert.Equal(["area_a"], accepted);
        Assert.Empty(connectionContext.SentPayloads);

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 9));
        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 9));

        (byte[] bytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal("sub-1", envelope!.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(9UL, payload!.Revision);
    }

    /// <summary>
    /// Verifies that an accepted subscription remains eligible for synchronization after its
    /// correlated baseline request times out, and that its uncorrelated late baseline stays ordered
    /// ahead of Events even when those Events arrive reentrantly during baseline admission.
    /// </summary>
    [Fact]
    public async Task EstablishAcceptedBaselines_TimedOutAreaReceivesUncorrelatedLateSnapshotAndForwardsEvents()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            ["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, _) = Subscribe(subscription, "sub-1", ["area_a"]);
        Assert.Equal(["area_a"], accepted);
        Assert.Empty(connectionContext.SentPayloads);

        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 1);
        (byte[] errorBytes, PublicOutboundLane errorLane) = connectionContext.SentPayloads[0];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, errorLane);
        Assert.True(codec.TryDecode(errorBytes, out PublicEnvelope? errorEnvelope));
        Assert.Equal(PublicMessageType.Error, errorEnvelope!.MessageType);
        Assert.Equal("sub-1", errorEnvelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(errorEnvelope, out ErrorPayload? errorPayload));
        Assert.Equal(PublicProtocolErrorCode.TemporarilyUnavailable, errorPayload!.Code);
        Assert.True(errorPayload.Retryable);

        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 2, revision: 3));
        };
        connectionContext.OnTrySendPayload = (bytes, _) =>
        {
            if (codec.TryDecode(bytes, out PublicEnvelope? envelope)
                && envelope!.MessageType == PublicMessageType.StateEvent
                && codec.TryDecodePayload(envelope, out StateEventPayload? payload)
                && payload!.Revision == 2)
            {
                feed.RaiseEvent(BuildEvent("area_a", baseRevision: 3, revision: 4));
            }
        };

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1));
        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 1));

        Assert.Equal(5, connectionContext.SentPayloads.Count);
        (byte[] baselineBytes, PublicOutboundLane baselineLane) = connectionContext.SentPayloads[1];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, baselineLane);
        Assert.True(codec.TryDecode(baselineBytes, out PublicEnvelope? baselineEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, baselineEnvelope!.MessageType);
        Assert.Null(baselineEnvelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(baselineEnvelope, out StateSnapshotPayload? baseline));
        Assert.Equal(1UL, baseline!.Revision);
        Assert.Empty(connectionContext.SentSnapshots);

        List<ulong> eventRevisions = connectionContext.SentPayloads
            .Skip(2)
            .Select(sent =>
            {
                Assert.Equal(PublicOutboundLane.Data, sent.Lane);
                Assert.True(codec.TryDecode(sent.Payload, out PublicEnvelope? envelope));
                Assert.Equal(PublicMessageType.StateEvent, envelope!.MessageType);
                Assert.Null(envelope.CorrelationId);
                Assert.True(codec.TryDecodePayload(envelope, out StateEventPayload? payload));
                return payload!.Revision;
            })
            .ToList();
        Assert.Equal([2UL, 3UL, 4UL], eventRevisions);
    }

    /// <summary>
    /// Verifies that a late baseline declined by Control/Recovery admission leaves the accepted area
    /// gated until a later baseline is admitted.
    /// </summary>
    [Fact]
    public async Task EstablishAcceptedBaselines_LateBaselineDeclined_DoesNotForwardEventsUntilRetryIsAdmitted()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            ["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-pressure", ["area_a"]);
        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 1);
        connectionContext.TrySendResult = false;

        StateSnapshotPublication snapshot = BuildSnapshot("area_a", revision: 10);
        feed.SetSnapshot(new StateAreaId("area_a"), snapshot);
        feed.RaiseSnapshotChanged(snapshot);
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11));

        Assert.Equal(3, connectionContext.SentPayloads.Count);
        Assert.All(connectionContext.SentPayloads.Skip(1), sent =>
            Assert.Equal(PublicOutboundLane.ControlOrRecovery, sent.Lane));
        Assert.Empty(connectionContext.SentSnapshots);

        connectionContext.TrySendResult = true;
        feed.RaiseSnapshotChanged(snapshot);
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11));

        Assert.Equal(5, connectionContext.SentPayloads.Count);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, connectionContext.SentPayloads[3].Lane);
        Assert.Equal(PublicOutboundLane.Data, connectionContext.SentPayloads[4].Lane);
    }

    /// <summary>Verifies that a Snapshot racing timeout-response admission is queued after the terminal correlated error.</summary>
    [Fact]
    public async Task FailPendingBaselineOnTimeout_SnapshotDuringErrorAdmissionFollowsTerminalError()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            ["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext { TrySendResult = true };
        connectionContext.OnTrySendPayload = (bytes, _) =>
        {
            if (!codec.TryDecode(bytes, out PublicEnvelope? envelope)
                || envelope!.MessageType != PublicMessageType.Error)
            {
                return;
            }

            StateSnapshotPublication snapshot = BuildSnapshot("area_a", revision: 1);
            feed.SetSnapshot(new StateAreaId("area_a"), snapshot);
            feed.RaiseSnapshotChanged(snapshot);
        };
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-race", ["area_a"]);
        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 2);

        Assert.True(codec.TryDecode(connectionContext.SentPayloads[0].Payload, out PublicEnvelope? error));
        Assert.Equal(PublicMessageType.Error, error!.MessageType);
        Assert.Equal("sub-race", error.CorrelationId);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? baseline));
        Assert.Equal(PublicMessageType.StateSnapshot, baseline!.MessageType);
        Assert.Null(baseline.CorrelationId);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, connectionContext.SentPayloads[0].Lane);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, connectionContext.SentPayloads[1].Lane);
    }

    /// <summary>Verifies that a one-shot request's timeout does not cancel an already accepted ongoing subscription.</summary>
    [Fact]
    public async Task HandleSnapshotRequest_TimedOutRequestOnAcceptedAreaStillSynchronizesLater()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            ["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-1", ["area_a"]);
        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 1);
        Assert.True(subscription.HandleSnapshotRequest("area_a", "request-1"));
        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 2);

        Assert.True(codec.TryDecode(connectionContext.SentPayloads[0].Payload, out PublicEnvelope? subscribeError));
        Assert.Equal("sub-1", subscribeError!.CorrelationId);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? requestError));
        Assert.Equal(PublicMessageType.Error, requestError!.MessageType);
        Assert.Equal("request-1", requestError.CorrelationId);

        StateSnapshotPublication snapshot = BuildSnapshot("area_a", revision: 1);
        feed.SetSnapshot(new StateAreaId("area_a"), snapshot);
        feed.RaiseSnapshotChanged(snapshot);

        Assert.Equal(3, connectionContext.SentPayloads.Count);
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[2];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? baseline));
        Assert.Equal(PublicMessageType.StateSnapshot, baseline!.MessageType);
        Assert.Null(baseline.CorrelationId);
    }

    /// <summary>Verifies that a future registered area inherits late-baseline synchronization without domain-specific handling.</summary>
    [Fact]
    public async Task EstablishAcceptedBaselines_FutureAreaReceivesLateSnapshotAfterTimeout()
    {
        const string area = "future_area";
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            [area], pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-future", [area]);
        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 1);

        feed.SetSnapshot(new StateAreaId(area), BuildSnapshot(area, revision: 7));
        feed.RaiseSnapshotChanged(BuildSnapshot(area, revision: 7));

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[1];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Null(envelope!.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(area, payload!.StateArea);
        Assert.Equal(7UL, payload.Revision);
    }

    /// <summary>Verifies that an availability notification after timeout wakes an accepted area when resynchronization made its Snapshot readable.</summary>
    [Fact]
    public async Task OnSnapshotAvailabilityChanged_TimedOutAcceptedAreaSynchronizesAvailableSnapshot()
    {
        const string area = "future_area";
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            [area], pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-resync", [area]);
        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 1);

        feed.SetSnapshot(new StateAreaId(area), BuildSnapshot(area, revision: 8));
        feed.RaiseSnapshotAvailabilityChanged();

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[1];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Null(envelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(area, payload!.StateArea);
        Assert.Equal(8UL, payload.Revision);
    }

    /// <summary>Verifies that a one-shot snapshot_request remains terminal after timing out and does not start forwarding later state.</summary>
    [Fact]
    public async Task HandleSnapshotRequest_TimedOutUnsubscribedAreaDoesNotForwardLaterState()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            ["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Assert.True(subscription.HandleSnapshotRequest("area_a", "request-1"));
        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 1);
        (byte[] errorBytes, _) = connectionContext.SentPayloads[0];
        Assert.True(codec.TryDecode(errorBytes, out PublicEnvelope? errorEnvelope));
        Assert.Equal(PublicMessageType.Error, errorEnvelope!.MessageType);
        Assert.Equal("request-1", errorEnvelope.CorrelationId);

        StateSnapshotPublication snapshot = BuildSnapshot("area_a", revision: 1);
        feed.SetSnapshot(new StateAreaId("area_a"), snapshot);
        feed.RaiseSnapshotChanged(snapshot);
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Single(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that a late Snapshot from a previous play context cannot establish an accepted area's baseline.</summary>
    [Fact]
    public async Task OnSnapshotChanged_TimedOutAreaIgnoresSnapshotFromPreviousPlayContext()
    {
        const string area = "area_a";
        var tracker = new FakePlayContextTracker();
        PlayContextId saveA = PlayContextId.NewId();
        tracker.NotifyTransition(saveA);
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            [area], playContextTracker: tracker, pendingBaselineDeadline: TimeSpan.FromMilliseconds(40));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-save-a", [area]);
        await WaitUntilAsync(() => connectionContext.SentPayloads.Count == 1);

        PlayContextId saveB = PlayContextId.NewId();
        tracker.NotifyTransition(saveB);
        int sentAfterSaveBoundary = connectionContext.SentPayloads.Count;
        StateSnapshotPublication staleSaveA = BuildSnapshot(
            area, revision: 1, playContextId: saveA, playContextGeneration: 1);
        feed.SetSnapshot(new StateAreaId(area), staleSaveA);
        feed.RaiseSnapshotChanged(staleSaveA);
        Assert.Equal(sentAfterSaveBoundary, connectionContext.SentPayloads.Count);
        Assert.Empty(connectionContext.SentSnapshots);

        StateSnapshotPublication currentSaveB = BuildSnapshot(
            area, revision: 1, playContextId: saveB, playContextGeneration: 2);
        feed.SetSnapshot(new StateAreaId(area), currentSaveB);
        feed.RaiseSnapshotChanged(currentSaveB);

        Assert.Equal(sentAfterSaveBoundary, connectionContext.SentPayloads.Count);
        (StateAreaId snapshotArea, byte[] bytes) = Assert.Single(connectionContext.SentSnapshots);
        Assert.Equal(area, snapshotArea.Value);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal(saveB.ToString(), envelope.PlayContextId);
        Assert.Null(envelope.CorrelationId);
    }

    /// <summary>
    /// Verifies the subscribe-side symmetry of <see cref="HandleSnapshotRequest_SecondRequestForSameStillPendingArea_SupersedesFirstCorrelation"/>:
    /// a second <c>subscribe</c> for the same still-pending area supersedes the first, so once a
    /// value becomes available, only one baseline is delivered, correlated to the second (most
    /// recent) subscribe message id.
    /// </summary>
    [Fact]
    public void EstablishAcceptedBaselines_SecondSubscribeForSameStillPendingArea_SupersedesFirstCorrelation()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-1", ["area_a"]);
        Subscribe(subscription, "sub-2", ["area_a"]);
        Assert.Empty(connectionContext.SentPayloads);

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        feed.RaiseSnapshotChanged(BuildSnapshot("area_a"));

        (byte[] bytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal("sub-2", envelope!.CorrelationId);
    }

    /// <summary>
    /// Verifies that a bare <c>snapshot_request</c> for an area this connection never subscribed to
    /// remains a one-time pull even after its pending baseline resolves: reaching
    /// <see cref="AreaDeliveryPhase.Live"/> does not, by itself, start ongoing event forwarding, which
    /// stays gated on <c>subscribe</c> acceptance regardless of phase.
    /// </summary>
    [Fact]
    public void HandleSnapshotRequest_NeverSubscribed_ResolvedPendingBaselineDoesNotStartOngoingForwarding()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        subscription.HandleSnapshotRequest("area_a", "req-1");
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1));
        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 1));
        Assert.Single(connectionContext.SentPayloads); // the one resolved baseline only

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Single(connectionContext.SentPayloads); // unchanged: never subscribed, so the later event never forwards
    }

    /// <summary>Verifies that an accepted area with no available snapshot sends nothing, without failing the subscribe call.</summary>
    [Fact]
    public void HandleSubscribe_RegisteredAreaWithNoSnapshotAvailable_AcceptsButSendsNothing()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, _) = Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Empty(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that an accepted subscription automatically receives an unchanged baseline after overall resynchronization completes.</summary>
    /// <returns>Completes after confirming the fulfilled subscription does not later time out.</returns>
    [Fact]
    public async Task HandleSubscribe_UnchangedBaselineBecomesReadableAfterResynchronization_SendsInitialSnapshotWithoutResubscribingOrTimingOut()
    {
        (PublicStateSubscription subscription, StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, FakePlayContextTracker playContextTracker, AdapterInstanceId adapterInstanceId, IAdapterResynchronizationToken resynchronizationToken, IAuthoritativeStateStore store) =
            BuildResynchronizingSubscription("area_a", TimeSpan.FromMilliseconds(150), retainedValue: 42);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe(["area_a"], reservedControlCapacity: 0);
        Assert.Equal(["area_a"], accepted);
        Assert.Empty(rejected);
        subscription.EstablishAcceptedBaselines(accepted, "sub-1");

        PlayContextSnapshot playContext = playContextTracker.GetSnapshot();
        Assert.False(store.ApplyResynchronizationBaseline(UpdateMode.Snapshot, resynchronizationToken, playContext.Current!.Value, playContext.TransitionGeneration, DateTimeOffset.UtcNow, new StateAreaId("area_a"), 42).Changed);
        Assert.True(adapterTracker.NeedsResynchronization);
        Assert.False(feed.TryGetSnapshot(new StateAreaId("area_a"), out _));
        Assert.Empty(connectionContext.SentPayloads);

        adapterTracker.NotifyResynchronized(adapterInstanceId, adapterTracker.CurrentConnectionGeneration, resynchronizationToken);

        Assert.False(adapterTracker.NeedsResynchronization);
        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal("sub-1", envelope.CorrelationId);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Single(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that subscribing to an already-accepted, still-live area again does not resend its baseline.</summary>
    [Fact]
    public void HandleSubscribe_AlreadyAcceptedAndLiveArea_DoesNotResendBaseline()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        (IReadOnlyList<string> accepted, _) = Subscribe(subscription, "sub-2", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Single(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that the accept/reject decision does not depend on <see cref="PublicStateSubscription.Bind"/> having been called, even though nothing can be sent yet.</summary>
    [Fact]
    public void HandleSubscribe_BeforeBind_StillReportsAcceptedWithoutThrowing()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Empty(rejected);
    }

    /// <summary>Verifies that a snapshot request for a registered area with an available value sends it as a baseline through the Control/Recovery lane, correlated to the request's own message id.</summary>
    [Fact]
    public void HandleSnapshotRequest_RegisteredAreaWithSnapshot_SendsBaselineOnControlLaneCorrelatedToRequestId()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 3));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        bool result = subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.True(result);
        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal("req-1", envelope!.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(3UL, payload!.Revision);
    }

    /// <summary>Verifies that a snapshot request for a registered area with no available value returns success but sends nothing.</summary>
    [Fact]
    public void HandleSnapshotRequest_RegisteredAreaNoSnapshotAvailable_SendsNothing()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        bool result = subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.True(result);
        Assert.Empty(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that snapshot_request keeps its original correlation and is fulfilled once when an unchanged baseline becomes readable after resynchronization.</summary>
    /// <returns>Completes after confirming the fulfilled request does not later time out.</returns>
    [Fact]
    public async Task HandleSnapshotRequest_UnchangedBaselineBecomesReadableAfterResynchronization_SendsOriginalRequestOnceAndCancelsDeadline()
    {
        (PublicStateSubscription subscription, StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, FakePlayContextTracker playContextTracker, AdapterInstanceId adapterInstanceId, IAdapterResynchronizationToken resynchronizationToken, IAuthoritativeStateStore store) =
            BuildResynchronizingSubscription("area_a", TimeSpan.FromMilliseconds(150), retainedValue: 42);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Assert.True(subscription.HandleSnapshotRequest("area_a", "m1"));
        PlayContextSnapshot playContext = playContextTracker.GetSnapshot();
        Assert.False(store.ApplyResynchronizationBaseline(UpdateMode.Snapshot, resynchronizationToken, playContext.Current!.Value, playContext.TransitionGeneration, DateTimeOffset.UtcNow, new StateAreaId("area_a"), 42).Changed);
        Assert.True(adapterTracker.NeedsResynchronization);
        Assert.False(feed.TryGetSnapshot(new StateAreaId("area_a"), out _));
        Assert.Empty(connectionContext.SentPayloads);

        adapterTracker.NotifyResynchronized(adapterInstanceId, adapterTracker.CurrentConnectionGeneration, resynchronizationToken);

        Assert.False(adapterTracker.NeedsResynchronization);
        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal("m1", envelope.CorrelationId);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Single(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that a wake while resynchronization still blocks reads sends nothing and leaves the pending request's deadline active.</summary>
    /// <returns>Completes after the still-pending request reaches its bounded deadline.</returns>
    [Fact]
    public async Task HandleSnapshotRequest_ChangedSnapshotWakeWhileResynchronizing_RemainsPendingUntilDeadline()
    {
        (PublicStateSubscription subscription, StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, FakePlayContextTracker playContextTracker, _, IAdapterResynchronizationToken resynchronizationToken, IAuthoritativeStateStore store) =
            BuildResynchronizingSubscription("area_a", TimeSpan.FromMilliseconds(250));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        subscription.HandleSnapshotRequest("area_a", "m1");
        PlayContextSnapshot playContext = playContextTracker.GetSnapshot();

        Assert.True(store.ApplyResynchronizationBaseline(UpdateMode.Snapshot, resynchronizationToken, playContext.Current!.Value, playContext.TransitionGeneration, DateTimeOffset.UtcNow, new StateAreaId("area_a"), 42).Changed);

        Assert.True(adapterTracker.NeedsResynchronization);
        Assert.False(feed.TryGetSnapshot(new StateAreaId("area_a"), out _));
        Assert.Empty(connectionContext.SentPayloads);
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        (byte[] bytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.Error, envelope!.MessageType);
        Assert.Equal("m1", envelope.CorrelationId);
    }

    /// <summary>Verifies that a snapshot-availability notification racing an initial baseline read retries the in-flight request without losing its wake.</summary>
    [Fact]
    public void HandleSnapshotRequest_AvailabilityWakeDuringBaselineRead_RetriesInFlightRequestOnce()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        StateSnapshotPublication snapshot = BuildSnapshot("area_a");
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.SetSnapshot(new StateAreaId("area_a"), snapshot);
            feed.RaiseSnapshotAvailabilityChanged();
        };

        subscription.HandleSnapshotRequest("area_a", "m1");

        (byte[] bytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal("m1", envelope.CorrelationId);
    }

    /// <summary>Verifies that duplicate availability wakes after fulfillment do not resend the baseline or allow a later timeout response.</summary>
    /// <returns>Completes after confirming only the initial baseline was sent.</returns>
    [Fact]
    public async Task HandleSnapshotRequest_DuplicateAvailabilityWakesAfterFulfillment_SendsOnlyOneSnapshot()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(150));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        subscription.HandleSnapshotRequest("area_a", "m1");
        StateSnapshotPublication snapshot = BuildSnapshot("area_a");
        feed.SetSnapshot(new StateAreaId("area_a"), snapshot);
        feed.RaiseSnapshotAvailabilityChanged();

        feed.RaiseSnapshotAvailabilityChanged();
        feed.RaiseSnapshotAvailabilityChanged();
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        (byte[] bytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal("m1", envelope.CorrelationId);
    }

    /// <summary>
    /// Verifies that a snapshot_request for a registered area with no value available yet is not a
    /// dead end: once the feed later publishes a matching value, it is delivered automatically, still
    /// correlated to the original request's own message id, without requiring a second client request.
    /// </summary>
    [Fact]
    public void HandleSnapshotRequest_RegisteredAreaNoSnapshotAvailable_LaterSnapshotAutomaticallyDelivers()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        bool result = subscription.HandleSnapshotRequest("area_a", "req-1");
        Assert.True(result);
        Assert.Empty(connectionContext.SentPayloads);

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 5));
        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 5));

        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal("req-1", envelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(5UL, payload!.Revision);
    }

    /// <summary>
    /// Verifies that a snapshot_request for a registered area with no value ever becoming available
    /// is answered with an explicit, retryable TemporarilyUnavailable error once its own bounded
    /// deadline elapses, rather than leaving the client waiting forever.
    /// </summary>
    [Fact]
    public async Task HandleSnapshotRequest_RegisteredAreaNoSnapshotAvailable_TimesOutWithRetryableError()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(30));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        bool result = subscription.HandleSnapshotRequest("area_a", "req-1");
        Assert.True(result);

        await WaitUntilAsync(() => connectionContext.SentPayloads.Count > 0);

        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.Error, envelope!.MessageType);
        Assert.Equal("req-1", envelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out ErrorPayload? payload));
        Assert.Equal(PublicProtocolErrorCode.TemporarilyUnavailable, payload!.Code);
        Assert.True(payload.Retryable);
    }

    /// <summary>
    /// Verifies that a second snapshot_request for the same still-pending area terminates the first
    /// request with a retryable error, then delivers the later baseline only to the second request.
    /// </summary>
    [Fact]
    public void HandleSnapshotRequest_SecondRequestForSameStillPendingArea_SupersedesFirstCorrelation()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        subscription.HandleSnapshotRequest("area_a", "req-1");
        subscription.HandleSnapshotRequest("area_a", "req-2");

        (byte[] errorBytes, PublicOutboundLane errorLane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, errorLane);
        Assert.True(codec.TryDecode(errorBytes, out PublicEnvelope? errorEnvelope));
        Assert.Equal(PublicMessageType.Error, errorEnvelope!.MessageType);
        Assert.Equal("req-1", errorEnvelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(errorEnvelope, out ErrorPayload? errorPayload));
        Assert.Equal(PublicProtocolErrorCode.TemporarilyUnavailable, errorPayload!.Code);
        Assert.True(errorPayload.Retryable);
        Assert.Contains("superseded", errorPayload.Message, StringComparison.OrdinalIgnoreCase);

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        feed.RaiseSnapshotChanged(BuildSnapshot("area_a"));

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        (byte[] snapshotBytes, _) = connectionContext.SentPayloads[1];
        Assert.True(codec.TryDecode(snapshotBytes, out PublicEnvelope? snapshotEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, snapshotEnvelope!.MessageType);
        Assert.Equal("req-2", snapshotEnvelope.CorrelationId);
    }

    /// <summary>
    /// Verifies that a superseded request's original deadline cannot send a second error, while the
    /// replacement remains pending past that deadline and can complete before its own deadline.
    /// </summary>
    /// <returns>Completes after the superseded and replacement deadlines have both elapsed.</returns>
    [Fact]
    public async Task HandleSnapshotRequest_SupersededRequestDeadlineCannotRespondAgain()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            ["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(1200));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        subscription.HandleSnapshotRequest("area_a", "req-1");
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        subscription.HandleSnapshotRequest("area_a", "req-2");

        Assert.Single(connectionContext.SentPayloads);
        await Task.Delay(TimeSpan.FromMilliseconds(700)); // past req-1's deadline, before req-2's
        Assert.Single(connectionContext.SentPayloads);

        StateSnapshotPublication snapshot = BuildSnapshot("area_a");
        feed.SetSnapshot(snapshot.StateArea, snapshot);
        feed.RaiseSnapshotChanged(snapshot);
        Assert.Equal(2, connectionContext.SentPayloads.Count);

        await Task.Delay(TimeSpan.FromMilliseconds(700)); // req-2's deadline is cancelled by fulfillment
        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[0].Payload, out PublicEnvelope? errorEnvelope));
        Assert.Equal(PublicMessageType.Error, errorEnvelope!.MessageType);
        Assert.Equal("req-1", errorEnvelope.CorrelationId);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? snapshotEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, snapshotEnvelope!.MessageType);
        Assert.Equal("req-2", snapshotEnvelope.CorrelationId);
    }

    /// <summary>
    /// Verifies that an availability retry already fetching req-1 becomes a no-op when req-2
    /// supersedes it, leaving the replacement correlation and attempt intact.
    /// </summary>
    [Fact]
    public void SnapshotAvailabilityWake_RetrySupersededByNewRequest_CannotAffectReplacement()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            ["area_a"], pendingBaselineDeadline: TimeSpan.FromSeconds(5));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        subscription.HandleSnapshotRequest("area_a", "req-1");

        StateSnapshotPublication snapshot = BuildSnapshot("area_a");
        feed.SetSnapshot(snapshot.StateArea, snapshot);
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            subscription.HandleSnapshotRequest("area_a", "req-2");
        };

        feed.RaiseSnapshotAvailabilityChanged();

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[0].Payload, out PublicEnvelope? errorEnvelope));
        Assert.Equal(PublicMessageType.Error, errorEnvelope!.MessageType);
        Assert.Equal("req-1", errorEnvelope.CorrelationId);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? snapshotEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, snapshotEnvelope!.MessageType);
        Assert.Equal("req-2", snapshotEnvelope.CorrelationId);
    }

    /// <summary>
    /// Verifies the other side of the fulfillment race: once req-1's baseline is admitted, a
    /// reentrant req-2 is independent and cannot make req-1 receive both a snapshot and an error.
    /// </summary>
    [Fact]
    public void HandleSnapshotRequest_FulfillmentWinsRaceWithNewRequest_BothCorrelationsCompleteOnce()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        StateSnapshotPublication snapshot = BuildSnapshot("area_a");
        feed.SetSnapshot(snapshot.StateArea, snapshot);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            subscription.HandleSnapshotRequest("area_a", "req-2");
        };

        subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[0].Payload, out PublicEnvelope? firstEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, firstEnvelope!.MessageType);
        Assert.Equal("req-1", firstEnvelope.CorrelationId);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? secondEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, secondEnvelope!.MessageType);
        Assert.Equal("req-2", secondEnvelope.CorrelationId);
    }

    /// <summary>Verifies that superseding one area's pending request leaves another area's correlation pending.</summary>
    [Fact]
    public void HandleSnapshotRequest_SupersedingOneAreaLeavesOtherAreaPending()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["health", "magicka"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        subscription.HandleSnapshotRequest("health", "health-1");
        subscription.HandleSnapshotRequest("magicka", "magicka-1");

        subscription.HandleSnapshotRequest("health", "health-2");

        Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[0].Payload, out PublicEnvelope? errorEnvelope));
        Assert.Equal(PublicMessageType.Error, errorEnvelope!.MessageType);
        Assert.Equal("health-1", errorEnvelope.CorrelationId);

        StateSnapshotPublication healthSnapshot = BuildSnapshot("health");
        StateSnapshotPublication magickaSnapshot = BuildSnapshot("magicka");
        feed.SetSnapshot(healthSnapshot.StateArea, healthSnapshot);
        feed.SetSnapshot(magickaSnapshot.StateArea, magickaSnapshot);
        feed.RaiseSnapshotChanged(healthSnapshot);
        feed.RaiseSnapshotChanged(magickaSnapshot);

        Assert.Equal(3, connectionContext.SentPayloads.Count);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? healthEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, healthEnvelope!.MessageType);
        Assert.Equal("health-2", healthEnvelope.CorrelationId);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[2].Payload, out PublicEnvelope? magickaEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, magickaEnvelope!.MessageType);
        Assert.Equal("magicka-1", magickaEnvelope.CorrelationId);
    }

    /// <summary>
    /// Verifies that when subscribe takes ownership of a baseline previously pending for a
    /// snapshot_request, the request receives its terminal error and the subscribe baseline proceeds.
    /// </summary>
    [Fact]
    public void HandleSubscribe_ReplacingPendingSnapshotRequest_TerminatesRequestAndKeepsBaseline()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        subscription.HandleSnapshotRequest("area_a", "req-1");

        Subscribe(subscription, "sub-1", ["area_a"]);

        (byte[] errorBytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(errorBytes, out PublicEnvelope? errorEnvelope));
        Assert.Equal(PublicMessageType.Error, errorEnvelope!.MessageType);
        Assert.Equal("req-1", errorEnvelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(errorEnvelope, out ErrorPayload? errorPayload));
        Assert.Equal(PublicProtocolErrorCode.TemporarilyUnavailable, errorPayload!.Code);
        Assert.True(errorPayload.Retryable);

        StateSnapshotPublication snapshot = BuildSnapshot("area_a");
        feed.SetSnapshot(snapshot.StateArea, snapshot);
        feed.RaiseSnapshotChanged(snapshot);

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? baselineEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, baselineEnvelope!.MessageType);
        Assert.Equal("sub-1", baselineEnvelope.CorrelationId);
    }

    /// <summary>
    /// Verifies that a snapshot_request replacing a pending subscribe baseline does not send a new
    /// superseded error for the subscribe correlation and keeps the established request behavior.
    /// </summary>
    [Fact]
    public void HandleSnapshotRequest_ReplacingPendingSubscribeBaseline_PreservesSubscribeBehavior()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.Empty(connectionContext.SentPayloads);
        StateSnapshotPublication snapshot = BuildSnapshot("area_a");
        feed.SetSnapshot(snapshot.StateArea, snapshot);
        feed.RaiseSnapshotChanged(snapshot);

        (byte[] baselineBytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(baselineBytes, out PublicEnvelope? baselineEnvelope));
        Assert.Equal(PublicMessageType.StateSnapshot, baselineEnvelope!.MessageType);
        Assert.Equal("req-1", baselineEnvelope.CorrelationId);
    }

    /// <summary>
    /// Verifies that a play-context transition answers a one-shot request with its unavailable R0
    /// baseline instead of a stale value; later values need an accepted ongoing subscription.
    /// </summary>
    [Fact]
    public void HandleSnapshotRequest_PlayContextTransitionsWhilePending_StaleValueNeverSatisfiesIt()
    {
        var playContextTracker = new FakePlayContextTracker();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: playContextTracker);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        subscription.HandleSnapshotRequest("area_a", "req-1");

        // A value belonging to the OLD context (generation 0), still sitting in the feed at the exact
        // moment of transition -- must never be allowed to satisfy the pending request.
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextGeneration: 0));
        PlayContextId freshContext = PlayContextId.NewId();
        playContextTracker.NotifyTransition(freshContext); // generation becomes 1
        (byte[] resetBytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(resetBytes, out PublicEnvelope? resetEnvelope));
        Assert.Equal("req-1", resetEnvelope!.CorrelationId);
        Assert.True(codec.TryDecodePayload(resetEnvelope, out StateSnapshotPayload? resetPayload));
        Assert.Equal(RevisionNumber.Initial.Value, resetPayload!.Revision);
        Assert.Equal(JsonValueKind.Null, resetPayload.Data.GetProperty("value").ValueKind);

        // A new value cannot reach an area this client did not subscribe to after the R0 response.
        var freshSnapshot = BuildSnapshot("area_a", revision: 2, playContextId: freshContext, playContextGeneration: 1);
        feed.SetSnapshot(new StateAreaId("area_a"), freshSnapshot);
        feed.RaiseSnapshotChanged(freshSnapshot);

        Assert.Empty(connectionContext.SentSnapshots);
        Assert.Single(connectionContext.SentPayloads);
    }

    /// <summary>
    /// Verifies that unsubscribing while a pending-baseline deadline is armed cancels it cleanly: no
    /// TemporarilyUnavailable error is sent for it once its original deadline would have elapsed.
    /// </summary>
    [Fact]
    public async Task Unsubscribe_WhilePendingBaselineDeadlineArmed_CancelsWithoutSendingError()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"], pendingBaselineDeadline: TimeSpan.FromMilliseconds(30));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        subscription.HandleSnapshotRequest("area_a", "req-1");

        subscription.Unsubscribe();
        await Task.Delay(TimeSpan.FromMilliseconds(60)); // past the original deadline

        Assert.Empty(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that a snapshot request for an unregistered area is reported as such.</summary>
    [Fact]
    public void HandleSnapshotRequest_UnregisteredArea_ReturnsFalse()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription();
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        bool result = subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.False(result);
        Assert.Empty(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that calling <see cref="PublicStateSubscription.HandleSnapshotRequest"/> before <see cref="PublicStateSubscription.Bind"/> still reports whether the area is registered, without throwing.</summary>
    [Fact]
    public void HandleSnapshotRequest_BeforeBind_StillReturnsRegisteredWithoutThrowing()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));

        bool result = subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that a snapshot_request arms an accepted area's event-forwarding gate the same way
    /// an initial subscribe acceptance does, for the recovery case where the area had no value
    /// available at subscribe time -- so it was accepted but never actually armed -- and only
    /// becomes available later.
    /// </summary>
    [Fact]
    public void HandleSnapshotRequest_ArmsGateForAcceptedAreaWithNoSnapshotAtSubscribeTime_LaterEventsForward()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]); // accepted, but no snapshot was available yet

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        subscription.HandleSnapshotRequest("area_a", "req-1"); // sends the baseline itself, on the Control/Recovery lane
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[^1];
        Assert.Equal(PublicOutboundLane.Data, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateEvent, envelope!.MessageType);
    }

    /// <summary>
    /// Verifies that the wrapped connection declining to admit a baseline onto the Control/Recovery
    /// lane never throws or otherwise disrupts the subscribe call that triggered it, and that the area
    /// is never treated as live: a later Event for it must
    /// not be forwarded, since the client never actually received the baseline it depends on.
    /// </summary>
    [Fact]
    public void HandleSubscribe_ConnectionDeclinesControlAdmission_AreaStaysNotLive_LaterEventNotForwarded()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext { TrySendResult = false };
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, _) = Subscribe(subscription, "sub-1", ["area_a"]);
        Assert.Equal(["area_a"], accepted);

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.DoesNotContain(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);
    }

    /// <summary>
    /// Verifies the same invariant as <see cref="HandleSubscribe_ConnectionDeclinesControlAdmission_AreaStaysNotLive_LaterEventNotForwarded"/>,
    /// but through <see cref="PublicStateSubscription.HandleSnapshotRequest"/> instead of the initial subscribe.
    /// </summary>
    [Fact]
    public void HandleSnapshotRequest_ConnectionDeclinesControlAdmission_AreaStaysNotLive_LaterEventNotForwarded()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext { TrySendResult = false };
        subscription.Bind(connectionContext, SessionId.NewId());

        bool result = subscription.HandleSnapshotRequest("area_a", "req-1");
        Assert.True(result);

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.DoesNotContain(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);
    }

    /// <summary>Verifies that a later, successful <see cref="PublicStateSubscription.HandleSnapshotRequest"/> can still arm an area whose first baseline attempt was declined by the connection.</summary>
    [Fact]
    public void HandleSnapshotRequest_AfterPriorBaselineDeclined_SuccessfullyArmsArea()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext { TrySendResult = false };
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]); // declined; area not live yet

        connectionContext.TrySendResult = true;
        subscription.HandleSnapshotRequest("area_a", "req-1");
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Contains(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);
    }

    /// <summary>
    /// Verifies the earlier window of the same event-loss-avoidance invariant: an Event raised while
    /// the baseline's own snapshot fetch is still in flight -- before the barrier revision is even
    /// known -- is held rather than discarded, since <see cref="AreaDeliveryPhase.Recovering"/> with an
    /// unknown barrier still means "hold," not "not live yet."
    /// </summary>
    [Fact]
    public void OnEventOccurred_DuringSnapshotFetch_EventIsHeldNotDiscarded()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null; // the re-baseline this triggers must not itself re-enter this hook
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11));
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Equal(2, connectionContext.SentPayloads.Count); // the baseline, then the Event held during the fetch
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[^1];
        Assert.Equal(PublicOutboundLane.Data, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateEvent, envelope!.MessageType);
        Assert.True(codec.TryDecodePayload(envelope, out StateEventPayload? payload));
        Assert.Equal(11UL, payload!.Revision);
    }

    /// <summary>
    /// Verifies that a play-context transition landing while the snapshot fetch is still in flight
    /// abandons the fetched old-context baseline and sends only the new identity's R0 reset.
    /// </summary>
    [Fact]
    public void TryEstablishBaseline_EpochSupersededDuringSnapshotFetch_SendsBoundaryResetThenFreshBaseline()
    {
        var tracker = new FakePlayContextTracker();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10, playContextGeneration: 0));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        PlayContextId newContext = PlayContextId.NewId();
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            tracker.NotifyTransition(newContext); // generation 1, mid-fetch: supersedes this attempt's epoch
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        (byte[] resetBytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(resetBytes, out PublicEnvelope? resetEnvelope));
        Assert.True(codec.TryDecodePayload(resetEnvelope, out StateSnapshotPayload? resetPayload));
        Assert.Equal(RevisionNumber.Initial.Value, resetPayload!.Revision);
        Assert.Equal(JsonValueKind.Null, resetPayload.Data.GetProperty("value").ValueKind);

        // A later, legitimate attempt under the new generation still works normally.
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1, playContextId: newContext, playContextGeneration: 1));
        subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[^1].Payload, out PublicEnvelope? freshEnvelope));
        Assert.True(codec.TryDecodePayload(freshEnvelope, out StateSnapshotPayload? freshPayload));
        Assert.Equal("req-1", freshEnvelope!.CorrelationId);
        Assert.Equal(1UL, freshPayload!.Revision);
    }

    /// <summary>
    /// Verifies that removing an area while its pending client snapshot request is fetching a
    /// baseline invalidates the in-flight result and sends one correlated terminal error only after
    /// the replacement acknowledgement.
    /// </summary>
    [Fact]
    public void HandleSubscribe_RemovingAreaDuringSnapshotFetch_TerminatesRequestAfterAckAndDiscardsSnapshot()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        SessionId sessionId = SessionId.NewId();
        subscription.Bind(connectionContext, sessionId);
        (IReadOnlyList<string> accepted, _) = subscription.HandleSubscribe(["area_a"], reservedControlCapacity: 1);
        subscription.EstablishAcceptedBaselines(accepted, "sub-1");
        Assert.Empty(connectionContext.SentPayloads);

        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
            (IReadOnlyList<string> removed, IReadOnlyList<string> rejected) = subscription.HandleSubscribe([], reservedControlCapacity: 1);
            Assert.Empty(removed);
            Assert.Empty(rejected);
            Assert.Empty(connectionContext.SentPayloads);

            byte[] ack = codec.Encode(
                PublicMessageType.SubscriptionAck,
                "ack-2",
                sessionId.ToString(),
                "sub-2",
                null,
                null,
                new SubscriptionAckPayload { AcceptedStateAreas = [], RejectedStateAreas = [] });
            connectionContext.TrySend(ack, PublicOutboundLane.ControlOrRecovery);
            subscription.SendSupersededSnapshotRequestErrors();
        };

        Assert.True(subscription.HandleSnapshotRequest("area_a", "req-1"));

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[0].Payload, out PublicEnvelope? ackEnvelope));
        Assert.Equal(PublicMessageType.SubscriptionAck, ackEnvelope!.MessageType);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? errorEnvelope));
        Assert.Equal(PublicMessageType.Error, errorEnvelope!.MessageType);
        Assert.Equal("req-1", errorEnvelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(errorEnvelope, out ErrorPayload? errorPayload));
        Assert.Equal(PublicProtocolErrorCode.TemporarilyUnavailable, errorPayload!.Code);
        Assert.True(errorPayload.Retryable);

        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 2));
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 2, revision: 3));
        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.All(connectionContext.SentPayloads, sent =>
        {
            Assert.True(codec.TryDecode(sent.Payload, out PublicEnvelope? envelope));
            Assert.False(envelope!.MessageType == PublicMessageType.StateSnapshot && envelope.CorrelationId == "req-1");
        });
    }

    /// <summary>
    /// Verifies the invariant for the second race: a brand-new Event arriving while previously-held Events
    /// are being drained (after baseline admission, before the area commits Live) joins the same drain
    /// instead of racing ahead of it through an independent, unordered send -- the final wire order
    /// matches arrival order exactly.
    /// </summary>
    [Fact]
    public void OnEventOccurred_NewEventArrivesWhileHeldEventsAreDraining_DeliveredAfterAllPreviouslyHeldEventsInOrder()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11));
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 11, revision: 12));
        };
        int trySendCount = 0;
        connectionContext.OnTrySend = () =>
        {
            trySendCount++;
            if (trySendCount == 2) // draining the first held Event (revision 11), not the baseline's own send
            {
                connectionContext.OnTrySend = null;
                feed.RaiseEvent(BuildEvent("area_a", baseRevision: 12, revision: 13));
            }
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Equal(4, connectionContext.SentPayloads.Count); // the baseline, then three Events
        List<ulong> revisionsInOrder = connectionContext.SentPayloads
            .Skip(1)
            .Select(sent =>
            {
                Assert.True(codec.TryDecode(sent.Payload, out PublicEnvelope? envelope));
                Assert.True(codec.TryDecodePayload(envelope, out StateEventPayload? payload));
                return payload!.Revision;
            })
            .ToList();
        Assert.Equal([11UL, 12UL, 13UL], revisionsInOrder);
    }

    /// <summary>
    /// Verifies the guard the final epoch re-check exists for: a play-context transition landing
    /// reentrantly while previously-held Events are still being drained -- with at least one more
    /// still queued -- clears the queue and abandons the attempt; the drain loop must stop rather than
    /// continue sending from a cleared list, and the attempt's own completion must not overwrite that
    /// abandonment by still committing <see cref="AreaDeliveryPhase.Live"/> once the loop ends.
    /// </summary>
    [Fact]
    public void TryEstablishBaseline_EpochSupersededMidDrainWithItemsStillQueued_StopsDrainingAndDoesNotOverwriteAbandonment()
    {
        var tracker = new FakePlayContextTracker();
        PlayContextId firstContext = PlayContextId.NewId();
        tracker.NotifyTransition(firstContext); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10, playContextId: firstContext, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11, playContextId: firstContext, playContextGeneration: 1)); // held
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 11, revision: 12, playContextId: firstContext, playContextGeneration: 1)); // held
        };
        int trySendCount = 0;
        PlayContextId secondContext = PlayContextId.NewId();
        connectionContext.OnTrySend = () =>
        {
            trySendCount++;
            if (trySendCount == 2) // draining the first held Event, with the second still queued behind it
            {
                connectionContext.OnTrySend = null;
                tracker.NotifyTransition(secondContext); // generation 2: clears the queue, abandons this attempt
            }
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        // The original baseline, the first Event whose send was already in flight, then the R0 reset;
        // the still-held second Event must never be sent under the old context.
        Assert.Equal(3, connectionContext.SentPayloads.Count);
        Assert.DoesNotContain(
            connectionContext.SentPayloads.Skip(1),
            sent => codec.TryDecode(sent.Payload, out PublicEnvelope? envelope)
                && codec.TryDecodePayload(envelope, out StateEventPayload? payload)
                && payload!.Revision == 12UL);

        // Re-arm under the new generation and confirm a fresh Event forwards -- proving the area
        // recovered cleanly (AwaitingBaseline, not incorrectly left Live under the old generation).
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1, playContextId: secondContext, playContextGeneration: 2));
        subscription.HandleSnapshotRequest("area_a", "req-1");
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2, playContextId: secondContext, playContextGeneration: 2));

        Assert.Contains(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);
    }

    /// <summary>
    /// Verifies the invariant for the original event-loss race: an Event above the baseline's own revision,
    /// raised exactly while that baseline is being admitted onto the Control/Recovery lane, is held
    /// rather than discarded, and is released once the baseline actually lands -- instead of being
    /// silently lost because the area was not yet live at the moment the Event arrived.
    /// </summary>
    [Fact]
    public void OnEventOccurred_DuringRecovering_HoldsEventAboveBarrierAndReleasesAfterAdmission()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null; // the released Event's own TrySend must not re-trigger this
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11));
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Equal(2, connectionContext.SentPayloads.Count); // the baseline, then the released Event
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[^1];
        Assert.Equal(PublicOutboundLane.Data, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateEvent, envelope!.MessageType);
        Assert.True(codec.TryDecodePayload(envelope, out StateEventPayload? payload));
        Assert.Equal(11UL, payload!.Revision);
    }

    /// <summary>Verifies that an Event at or below the barrier revision, raised while the baseline that establishes that revision is being admitted, is discarded as superseded rather than held.</summary>
    [Fact]
    public void OnEventOccurred_DuringRecovering_DiscardsEventAtOrBelowBarrierRevision()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 9, revision: 10)); // == barrier revision
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Single(connectionContext.SentPayloads); // only the baseline; the superseded Event never forwards
    }

    /// <summary>Verifies that a fetched snapshot whose own captured play-context generation is already stale is treated as unavailable, never sent under a relabeled context.</summary>
    [Fact]
    public void HandleSubscribe_StalePublicationGeneration_TreatedAsUnavailable()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId()); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextGeneration: 0)); // stale: tracker is already at generation 1
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Empty(connectionContext.SentPayloads);
    }

    /// <summary>
    /// Verifies the recovery epoch's purpose: a play-context transition landing while a baseline send
    /// is in flight queues the new R0 reset, and the old baseline completion cannot overwrite it.
    /// </summary>
    [Fact]
    public void HandleSubscribe_ContextTransitionedDuringSend_CompletionIgnoredAreaStaysNotLive()
    {
        var tracker = new FakePlayContextTracker();
        PlayContextId firstContext = PlayContextId.NewId();
        tracker.NotifyTransition(firstContext); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextId: firstContext, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        PlayContextId secondContext = PlayContextId.NewId();
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            tracker.NotifyTransition(secondContext); // generation 2, mid-send
        };

        Subscribe(subscription, "sub-1", ["area_a"]);
        Assert.Equal(2, connectionContext.SentPayloads.Count); // the old baseline, then the boundary R0 reset
        (byte[] resetBytes, _) = connectionContext.SentPayloads[^1];
        Assert.True(codec.TryDecode(resetBytes, out PublicEnvelope? resetEnvelope));
        Assert.True(codec.TryDecodePayload(resetEnvelope, out StateSnapshotPayload? resetPayload));
        Assert.Equal(RevisionNumber.Initial.Value, resetPayload!.Revision);

        // New-generation state is admitted only after the boundary reset and follows it on the Data lane.
        feed.RaiseEvent(BuildEvent(
            "area_a",
            baseRevision: 0,
            revision: 1,
            playContextId: secondContext,
            playContextGeneration: 2));

        Assert.Equal(3, connectionContext.SentPayloads.Count);
        Assert.Equal(PublicOutboundLane.Data, connectionContext.SentPayloads[^1].Lane);
    }

    /// <summary>
    /// Verifies the held-Event overflow policy: once the bounded hold fills up, the current recovery
    /// attempt is abandoned (its held Events discarded, never released) and a fresh baseline is
    /// established from the newest authoritative snapshot instead of growing the hold unbounded.
    /// </summary>
    [Fact]
    public void OnEventOccurred_HeldEventBufferOverflow_AbandonsHeldEventsAndReBaselines()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null; // the overflow's own re-baseline send must not re-trigger this flood
            feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 99)); // the fresh value the re-baseline should pick up
            for (ulong i = 0; i <= Constants.MaxHeldRecoveryEventsPerArea; i++)
            {
                feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10 + i, revision: 11 + i));
            }
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        // The original baseline (revision 10), then the overflow-triggered re-baseline (revision 99) --
        // proving a fresh authoritative read replaced the held set rather than releasing it.
        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.DoesNotContain(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[^1];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        // The overflow-triggered re-baseline must still correlate to the original "sub-1" subscribe --
        // never a fresh host-generated id no client request ever made.
        Assert.Equal("sub-1", envelope!.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(99UL, payload!.Revision);
    }

    /// <summary>Verifies that a live area's Event is discarded, not forwarded, when its own captured play-context generation is already stale relative to the tracker's current one -- symmetric with the same check on a baseline snapshot.</summary>
    [Fact]
    public void OnEventOccurred_StaleEventGeneration_DoesNotForward()
    {
        var tracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        tracker.NotifyTransition(context); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextId: context, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentAfterBaseline = connectionContext.SentPayloads.Count;

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2, playContextId: context, playContextGeneration: 0)); // stale generation

        Assert.Equal(sentAfterBaseline, connectionContext.SentPayloads.Count);
    }

    /// <summary>Verifies that a held-Event overflow's re-baseline attempt leaves the area at <see cref="AreaDeliveryPhase.AwaitingBaseline"/>, rather than stuck mid-recovery, when no current value is available yet to re-baseline from.</summary>
    [Fact]
    public void OnEventOccurred_HeldEventBufferOverflowWithNoSnapshotAvailable_AreaAwaitsFreshBaseline()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            for (ulong i = 0; i <= Constants.MaxHeldRecoveryEventsPerArea; i++)
            {
                feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10 + i, revision: 11 + i));
            }
        };

        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentBeforeReArm = connectionContext.SentPayloads.Count; // just the original baseline; no snapshot was available to re-baseline from

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 50));
        subscription.HandleSnapshotRequest("area_a", "req-1"); // the area is still awaiting a baseline, so this re-arms it

        Assert.Equal(sentBeforeReArm + 1, connectionContext.SentPayloads.Count);
        (byte[] bytes, _) = connectionContext.SentPayloads[^1];
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(50UL, payload!.Revision);
    }

    /// <summary>Verifies that more than one held Event is released in the order it arrived once the establishing baseline is admitted.</summary>
    [Fact]
    public void OnEventOccurred_MultipleEventsHeldDuringRecovering_ReleasedInArrivalOrder()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11));
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 11, revision: 12));
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 12, revision: 13));
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Equal(4, connectionContext.SentPayloads.Count); // the baseline, then the three released Events
        List<ulong> revisionsInOrder = connectionContext.SentPayloads
            .Skip(1)
            .Select(sent =>
            {
                Assert.True(codec.TryDecode(sent.Payload, out PublicEnvelope? envelope));
                Assert.True(codec.TryDecodePayload(envelope, out StateEventPayload? payload));
                return payload!.Revision;
            })
            .ToList();
        Assert.Equal([11UL, 12UL, 13UL], revisionsInOrder);
    }

    /// <summary>Verifies that a play-context transition discards Events held for an area mid-recovery instead of releasing them once a later baseline for the new context establishes.</summary>
    [Fact]
    public void OnPlayContextTransitioned_DuringRecovering_DiscardsHeldEventsRatherThanReleasingThem()
    {
        var tracker = new FakePlayContextTracker();
        PlayContextId firstContext = PlayContextId.NewId();
        tracker.NotifyTransition(firstContext); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10, playContextId: firstContext, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        PlayContextId secondContext = PlayContextId.NewId();
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11, playContextId: firstContext, playContextGeneration: 1)); // held while Recovering
            tracker.NotifyTransition(secondContext); // generation 2; must discard the held Event above, not release it
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.DoesNotContain(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);

        // Re-arm under the new generation and confirm a fresh Event forwards normally -- proving the
        // area recovered cleanly rather than being left in a broken state by the discard.
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1, playContextId: secondContext, playContextGeneration: 2));
        subscription.HandleSnapshotRequest("area_a", "req-1");
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2, playContextId: secondContext, playContextGeneration: 2));

        Assert.Contains(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);
    }

    /// <summary>Verifies that an event for an accepted area whose snapshot has already been sent is forwarded, decoding to the expected content.</summary>
    [Fact]
    public void OnEventOccurred_AcceptedAreaWithSnapshotSent_ForwardsEvent()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Equal(2, connectionContext.SentPayloads.Count); // the baseline sent by HandleSubscribe, then the event
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[^1];
        Assert.Equal(PublicOutboundLane.Data, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateEvent, envelope!.MessageType);
        Assert.Null(envelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out StateEventPayload? payload));
        Assert.Equal("area_a", payload!.StateArea);
        Assert.Equal(1UL, payload.BaseRevision);
        Assert.Equal(2UL, payload.Revision);
    }

    /// <summary>Verifies that an event for an area that was accepted but has not yet received a snapshot is not forwarded.</summary>
    [Fact]
    public void OnEventOccurred_AcceptedAreaWithoutSnapshotYet_DoesNotForward()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]); // accepted, but no snapshot was available to send

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Empty(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that an event for an area this connection never accepted is not forwarded.</summary>
    [Fact]
    public void OnEventOccurred_UnacceptedArea_DoesNotForward()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Empty(connectionContext.SentPayloads);
    }

    /// <summary>Verifies that an event arriving after this connection has unsubscribed is not forwarded.</summary>
    [Fact]
    public void OnEventOccurred_AfterUnsubscribe_DoesNotForward()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentBeforeUnsubscribe = connectionContext.SentPayloads.Count;

        subscription.Unsubscribe();
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Equal(sentBeforeUnsubscribe, connectionContext.SentPayloads.Count);
    }

    /// <summary>
    /// Verifies that a play-context transition sends a revision-zero unavailable reset before new
    /// state for an already-accepted area is forwarded.
    /// </summary>
    [Fact]
    public void OnEventOccurred_ContextTransitioned_StopsForwardingUntilReArmed()
    {
        var tracker = new FakePlayContextTracker();
        PlayContextId firstContext = PlayContextId.NewId();
        tracker.NotifyTransition(firstContext); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextId: firstContext, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentBeforeTransition = connectionContext.SentPayloads.Count;

        PlayContextId secondContext = PlayContextId.NewId();
        tracker.NotifyTransition(secondContext); // generation 2
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 0, revision: 1, playContextId: secondContext, playContextGeneration: 2));

        Assert.Equal(sentBeforeTransition + 2, connectionContext.SentPayloads.Count); // the R0 reset, then the new-context Event
        (byte[] resetBytes, PublicOutboundLane resetLane) = connectionContext.SentPayloads[sentBeforeTransition];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, resetLane);
        Assert.True(codec.TryDecode(resetBytes, out PublicEnvelope? resetEnvelope));
        Assert.True(codec.TryDecodePayload(resetEnvelope, out StateSnapshotPayload? resetPayload));
        Assert.Equal(RevisionNumber.Initial.Value, resetPayload!.Revision);
        Assert.Equal(JsonValueKind.Null, resetPayload.Data.GetProperty("value").ValueKind);

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 2, playContextId: secondContext, playContextGeneration: 2));
        subscription.HandleSnapshotRequest("area_a", "req-1"); // re-arms under the new context
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 2, revision: 3, playContextId: secondContext, playContextGeneration: 2));

        Assert.Equal(sentBeforeTransition + 4, connectionContext.SentPayloads.Count); // the re-arm baseline, then the Event
    }

    /// <summary>Verifies that a context boundary purges once and creates R0 resets for every accepted generic area before sending them.</summary>
    [Fact]
    public void PlayContextTransitioned_ResetsEveryAcceptedAreaAfterPurgingPendingData()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId());
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) =
            BuildSubscription(["future_area_a", "future_area_b"], playContextTracker: tracker);
        var connectionContext = new FakePublicConnectionContext();
        var operationOrder = new List<string>();
        connectionContext.OnPurgePendingData = () => operationOrder.Add("purge");
        feed.OnCreateUnavailableBoundaryBaseline = area => operationOrder.Add($"create:{area.Value}");
        connectionContext.OnTrySendPayload = (bytes, lane) =>
        {
            if (lane == PublicOutboundLane.ControlOrRecovery
                && codec.TryDecode(bytes, out PublicEnvelope? envelope)
                && envelope!.MessageType == PublicMessageType.StateSnapshot
                && codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload))
            {
                operationOrder.Add($"send:{payload!.StateArea}");
            }
        };
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-boundary", ["future_area_a", "future_area_b"]);

        PlayContextId nextContext = PlayContextId.NewId();
        tracker.NotifyTransition(nextContext);

        Assert.Equal(1, connectionContext.PurgePendingDataCalls);
        Assert.Equal("purge", operationOrder[0]);
        Assert.Equal(5, operationOrder.Count);
        Assert.Contains("create:future_area_a", operationOrder);
        Assert.Contains("create:future_area_b", operationOrder);
        Assert.Contains("send:future_area_a", operationOrder);
        Assert.Contains("send:future_area_b", operationOrder);
        Assert.All(connectionContext.SentPayloads, sent => Assert.Equal(PublicOutboundLane.ControlOrRecovery, sent.Lane));

        List<StateSnapshotPayload> baselines = connectionContext.SentPayloads
            .Select(sent =>
            {
                Assert.True(codec.TryDecode(sent.Payload, out PublicEnvelope? envelope));
                Assert.Equal("sub-boundary", envelope!.CorrelationId);
                Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
                return payload!;
            })
            .ToList();
        Assert.Equal(2, baselines.Count);
        Assert.Equal(
            new HashSet<string> { "future_area_a", "future_area_b" },
            baselines.Select(payload => payload.StateArea).ToHashSet());
        Assert.All(baselines, payload =>
        {
            Assert.Equal(RevisionNumber.Initial.Value, payload.Revision);
            Assert.Equal(JsonValueKind.Null, payload.Data.GetProperty("value").ValueKind);
        });
    }

    /// <summary>Verifies that a return to no play context leaves an unavailable revision-zero baseline available for a later snapshot request.</summary>
    [Fact]
    public void PlayContextTransitioned_ToNullContext_RetainsUnavailableBoundaryBaseline()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId());
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed _) =
            BuildSubscription(["future_area"], playContextTracker: tracker);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-null-context", ["future_area"]);

        tracker.ClearCurrent();

        (byte[] resetBytes, _) = Assert.Single(connectionContext.SentPayloads);
        Assert.True(codec.TryDecode(resetBytes, out PublicEnvelope? resetEnvelope));
        Assert.Null(resetEnvelope!.PlayContextId);
        Assert.True(codec.TryDecodePayload(resetEnvelope, out StateSnapshotPayload? resetPayload));
        Assert.Equal(RevisionNumber.Initial.Value, resetPayload!.Revision);
        Assert.Equal(JsonValueKind.Null, resetPayload.Data.GetProperty("value").ValueKind);

        Assert.True(subscription.HandleSnapshotRequest("future_area", "request-after-menu"));
        (byte[] requestedBytes, _) = connectionContext.SentPayloads[^1];
        Assert.True(codec.TryDecode(requestedBytes, out PublicEnvelope? requestedEnvelope));
        Assert.Equal("request-after-menu", requestedEnvelope!.CorrelationId);
        Assert.Null(requestedEnvelope.PlayContextId);
        Assert.True(codec.TryDecodePayload(requestedEnvelope, out StateSnapshotPayload? requestedPayload));
        Assert.Equal(RevisionNumber.Initial.Value, requestedPayload!.Revision);
        Assert.Equal(JsonValueKind.Null, requestedPayload.Data.GetProperty("value").ValueKind);
    }

    /// <summary>Verifies that an authority rotation establishes a fresh revision-zero unavailable baseline for every accepted area.</summary>
    [Fact]
    public void StateAuthorityRotated_ResetsEveryAcceptedAreaUnderTheNewAuthority()
    {
        var adapterTracker = new FakeAdapterAvailabilityTracker
        {
            Current = AdapterAvailability.Available,
            CurrentConnectionGeneration = 1,
            NeedsResynchronization = false,
        };
        var authorityLifecycle = new StateAuthorityLifecycle(adapterTracker);
        var tracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        tracker.NotifyTransition(context);
        var subscriptionCodec = new PublicEnvelopeCodec(authorityLifecycle);
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(
            ["future_area_a", "future_area_b"],
            playContextTracker: tracker,
            stateAuthorityLifecycle: authorityLifecycle,
            envelopeCodec: subscriptionCodec);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-authority", ["future_area_a", "future_area_b"]);
        StateAuthorityId previousAuthority = authorityLifecycle.Current;

        AdapterAvailabilityTransition transition = adapterTracker.CommitDisconnected(
            adapterTracker.CurrentInstanceId!.Value,
            adapterTracker.CurrentConnectionGeneration)!;
        adapterTracker.PublishTransition(transition);

        Assert.Equal(1, connectionContext.PurgePendingDataCalls);
        Assert.Equal(2, connectionContext.SentPayloads.Count);
        foreach ((byte[] bytes, PublicOutboundLane lane) in connectionContext.SentPayloads)
        {
            Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
            Assert.True(subscriptionCodec.TryDecode(bytes, out PublicEnvelope? envelope));
            Assert.NotEqual(previousAuthority.ToString(), envelope!.StateAuthorityId);
            Assert.Equal(authorityLifecycle.Current.ToString(), envelope.StateAuthorityId);
            Assert.Equal(context.ToString(), envelope.PlayContextId);
            Assert.True(subscriptionCodec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
            Assert.Equal(RevisionNumber.Initial.Value, payload!.Revision);
            Assert.Equal(JsonValueKind.Null, payload.Data.GetProperty("value").ValueKind);
        }

        feed.SetSnapshot(
            new StateAreaId("future_area_a"),
            BuildSnapshot(
                "future_area_a",
                revision: 8,
                playContextId: context,
                playContextGeneration: tracker.TransitionGeneration,
                stateAuthorityId: authorityLifecycle.Current));
        feed.RaiseSnapshotAvailabilityChanged();

        Assert.Single(connectionContext.SentSnapshots);
        byte[] recoveredBytes = connectionContext.SentSnapshots[^1].Payload;
        Assert.True(subscriptionCodec.TryDecode(recoveredBytes, out PublicEnvelope? recoveredEnvelope));
        Assert.True(subscriptionCodec.TryDecodePayload(recoveredEnvelope, out StateSnapshotPayload? recoveredPayload));
        Assert.Equal(8UL, recoveredPayload!.Revision);
    }

    /// <summary>Verifies a rotation in the final publication-encoding window drops the old value and admits the new authority's R0 baseline.</summary>
    [Fact]
    public void TryEstablishBaseline_AuthorityRotatesAfterValidation_DropsOldPublicationThenSendsNewBoundary()
    {
        var authorityLifecycle = new FakeStateAuthorityLifecycle();
        StateAuthorityId oldAuthority = authorityLifecycle.Current;
        var playContextTracker = new FakePlayContextTracker();
        var feed = new FakeStatePublicationFeed
        {
            CurrentStateAuthorityId = oldAuthority,
            CurrentStateAuthorityIdProvider = () => authorityLifecycle.Current,
        };
        feed.SetSnapshot(
            new StateAreaId("area_a"),
            BuildSnapshot("area_a", revision: 7, stateAuthorityId: oldAuthority));
        (PublicStateSubscription subscription, _, _) = BuildSubscription(
            ["area_a"],
            feed: feed,
            playContextTracker: playContextTracker,
            stateAuthorityLifecycle: authorityLifecycle,
            envelopeCodec: new PublicEnvelopeCodec(authorityLifecycle));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        authorityLifecycle.OnCurrentRead = authorityLifecycle.NotifyRotated;

        Subscribe(subscription, "sub-rotation-race", ["area_a"]);

        StateAuthorityId newAuthority = authorityLifecycle.Current;
        Assert.NotEqual(oldAuthority, newAuthority);
        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(newAuthority.ToString(), envelope!.StateAuthorityId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(RevisionNumber.Initial.Value, payload!.Revision);
        Assert.Equal(JsonValueKind.Null, payload.Data.GetProperty("value").ValueKind);
        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies a state Event that becomes stale during its initial freshness check is dropped before the new-authority boundary.</summary>
    [Fact]
    public void OnEventOccurred_AuthorityRotatesAfterInitialValidation_DropsEventBeforeNewBoundary()
    {
        var authorityLifecycle = new FakeStateAuthorityLifecycle();
        StateAuthorityId oldAuthority = authorityLifecycle.Current;
        var feed = new FakeStatePublicationFeed
        {
            CurrentStateAuthorityId = oldAuthority,
            CurrentStateAuthorityIdProvider = () => authorityLifecycle.Current,
        };
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", stateAuthorityId: oldAuthority));
        (PublicStateSubscription subscription, _, _) = BuildSubscription(
            ["area_a"],
            feed: feed,
            stateAuthorityLifecycle: authorityLifecycle,
            envelopeCodec: new PublicEnvelopeCodec(authorityLifecycle));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-event-race", ["area_a"]);
        int sentBeforeRotation = connectionContext.SentPayloads.Count;
        authorityLifecycle.OnCurrentRead = authorityLifecycle.NotifyRotated;

        feed.RaiseEvent(BuildEvent("area_a", 1, 2, stateAuthorityId: oldAuthority));

        StateAuthorityId newAuthority = authorityLifecycle.Current;
        Assert.NotEqual(oldAuthority, newAuthority);
        Assert.Equal(sentBeforeRotation + 1, connectionContext.SentPayloads.Count);
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[^1];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal(newAuthority.ToString(), envelope.StateAuthorityId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(RevisionNumber.Initial.Value, payload!.Revision);
        Assert.Equal(JsonValueKind.Null, payload.Data.GetProperty("value").ValueKind);
    }

    /// <summary>Verifies a replaceable state Snapshot that becomes stale during its initial freshness check is dropped before the new-authority boundary.</summary>
    [Fact]
    public void OnSnapshotChanged_AuthorityRotatesAfterInitialValidation_DropsSnapshotBeforeNewBoundary()
    {
        var authorityLifecycle = new FakeStateAuthorityLifecycle();
        StateAuthorityId oldAuthority = authorityLifecycle.Current;
        var feed = new FakeStatePublicationFeed
        {
            CurrentStateAuthorityId = oldAuthority,
            CurrentStateAuthorityIdProvider = () => authorityLifecycle.Current,
        };
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", stateAuthorityId: oldAuthority));
        (PublicStateSubscription subscription, _, _) = BuildSubscription(
            ["area_a"],
            feed: feed,
            stateAuthorityLifecycle: authorityLifecycle,
            envelopeCodec: new PublicEnvelopeCodec(authorityLifecycle));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-snapshot-race", ["area_a"]);
        int sentBeforeRotation = connectionContext.SentPayloads.Count;
        authorityLifecycle.OnCurrentRead = authorityLifecycle.NotifyRotated;

        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 2, stateAuthorityId: oldAuthority));

        StateAuthorityId newAuthority = authorityLifecycle.Current;
        Assert.NotEqual(oldAuthority, newAuthority);
        Assert.Equal(sentBeforeRotation + 1, connectionContext.SentPayloads.Count);
        (byte[] bytes, PublicOutboundLane lane) = connectionContext.SentPayloads[^1];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal(newAuthority.ToString(), envelope.StateAuthorityId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(RevisionNumber.Initial.Value, payload!.Revision);
        Assert.Equal(JsonValueKind.Null, payload.Data.GetProperty("value").ValueKind);
        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that a declined R0 reset leaves the area gated until a later reset baseline is actually admitted.</summary>
    [Fact]
    public void BoundaryResetDeclined_DoesNotForwardNewStateUntilRetryAdmitsBaseline()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId());
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) =
            BuildSubscription(["future_area"], playContextTracker: tracker);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-declined-reset", ["future_area"]);
        connectionContext.TrySendResult = false;

        tracker.NotifyTransition(PlayContextId.NewId());
        Assert.Equal(1, connectionContext.PurgePendingDataCalls);
        Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, connectionContext.SentPayloads[0].Lane);

        connectionContext.TrySendResult = true;
        feed.SetSnapshot(
            new StateAreaId("future_area"),
            BuildSnapshot(
                "future_area",
                revision: 1,
                playContextId: tracker.Current,
                playContextGeneration: tracker.TransitionGeneration));
        feed.RaiseSnapshotChanged(BuildSnapshot(
            "future_area",
            revision: 1,
            playContextId: tracker.Current,
            playContextGeneration: tracker.TransitionGeneration));

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.Single(connectionContext.SentSnapshots);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, connectionContext.SentPayloads[1].Lane);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[1].Payload, out PublicEnvelope? resetEnvelope));
        Assert.True(codec.TryDecodePayload(resetEnvelope, out StateSnapshotPayload? resetPayload));
        Assert.Equal(RevisionNumber.Initial.Value, resetPayload!.Revision);
        Assert.True(codec.TryDecode(connectionContext.SentSnapshots[0].Payload, out PublicEnvelope? stateEnvelope));
        Assert.True(codec.TryDecodePayload(stateEnvelope, out StateSnapshotPayload? statePayload));
        Assert.Equal(1UL, statePayload!.Revision);
    }

    /// <summary>Verifies that a later Event retries a declined boundary reset and the current Snapshot follows it without forwarding the Event twice.</summary>
    [Fact]
    public void OnEventOccurred_BoundaryResetDeclined_RetriesResetThenCurrentSnapshot()
    {
        var tracker = new FakePlayContextTracker();
        PlayContextId originalContext = PlayContextId.NewId();
        tracker.NotifyTransition(originalContext);
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) =
            BuildSubscription(["area_a"], playContextTracker: tracker);
        var areaId = new StateAreaId("area_a");
        feed.SetSnapshot(
            areaId,
            BuildSnapshot("area_a", playContextId: originalContext, playContextGeneration: tracker.TransitionGeneration));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentBeforeBoundary = connectionContext.SentPayloads.Count;

        PlayContextId currentContext = PlayContextId.NewId();
        connectionContext.TrySendResult = false;
        tracker.NotifyTransition(currentContext);

        Assert.Equal(sentBeforeBoundary + 1, connectionContext.SentPayloads.Count);
        int sentAfterDeclinedBoundary = connectionContext.SentPayloads.Count;
        feed.SetSnapshot(
            areaId,
            BuildSnapshot("area_a", revision: 2, playContextId: currentContext, playContextGeneration: tracker.TransitionGeneration));
        feed.RaiseEvent(BuildEvent(
            "area_a",
            baseRevision: 1,
            revision: 2,
            playContextId: currentContext,
            playContextGeneration: tracker.TransitionGeneration));

        Assert.Equal(sentAfterDeclinedBoundary + 1, connectionContext.SentPayloads.Count);
        Assert.Empty(connectionContext.SentSnapshots);
        feed.SetSnapshot(
            areaId,
            BuildSnapshot("area_a", revision: 3, playContextId: currentContext, playContextGeneration: tracker.TransitionGeneration));
        connectionContext.TrySendResult = true;
        feed.RaiseEvent(BuildEvent(
            "area_a",
            baseRevision: 2,
            revision: 3,
            playContextId: currentContext,
            playContextGeneration: tracker.TransitionGeneration));

        Assert.Equal(sentAfterDeclinedBoundary + 2, connectionContext.SentPayloads.Count);
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, connectionContext.SentPayloads[^1].Lane);
        Assert.True(codec.TryDecode(connectionContext.SentPayloads[^1].Payload, out PublicEnvelope? resetEnvelope));
        Assert.True(codec.TryDecodePayload(resetEnvelope, out StateSnapshotPayload? resetPayload));
        Assert.Equal(RevisionNumber.Initial.Value, resetPayload!.Revision);
        Assert.Single(connectionContext.SentSnapshots);
        Assert.True(codec.TryDecode(connectionContext.SentSnapshots[0].Payload, out PublicEnvelope? stateEnvelope));
        Assert.True(codec.TryDecodePayload(stateEnvelope, out StateSnapshotPayload? statePayload));
        Assert.Equal(3UL, statePayload!.Revision);
    }

    /// <summary>Verifies that a play-context transition invalidates a live baseline without un-accepting the area: a repeat subscribe still reports it accepted.</summary>
    [Fact]
    public void HandleSubscribe_AfterContextTransitioned_StillReportsAreaAccepted()
    {
        var tracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        tracker.NotifyTransition(context); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextId: context, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        tracker.NotifyTransition(PlayContextId.NewId()); // generation 2
        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = Subscribe(subscription, "sub-2", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Empty(rejected);
    }

    /// <summary>
    /// Verifies that a state-authority rotation invalidates a live area's baseline the same way a
    /// play-context transition does: an Event that would otherwise have forwarded stops forwarding
    /// once <see cref="IStateAuthorityLifecycle.Rotated"/> fires, until the area is re-armed.
    /// Incremental continuity from the previous <see cref="StateAuthorityId"/> is invalid until a
    /// fresh baseline is established under the new one.
    /// </summary>
    [Fact]
    public void OnEventOccurred_StateAuthorityRotated_StopsForwardingUntilReArmed()
    {
        var lifecycle = new FakeStateAuthorityLifecycle();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], stateAuthorityLifecycle: lifecycle);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", stateAuthorityId: lifecycle.Current));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentBeforeRotation = connectionContext.SentPayloads.Count;

        lifecycle.NotifyRotated();
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Equal(sentBeforeRotation + 1, connectionContext.SentPayloads.Count); // the authority-boundary R0 reset; the previous-authority Event is rejected
        (byte[] resetBytes, PublicOutboundLane resetLane) = connectionContext.SentPayloads[^1];
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, resetLane);
        Assert.True(codec.TryDecode(resetBytes, out PublicEnvelope? resetEnvelope));
        Assert.Equal(lifecycle.Current.ToString(), resetEnvelope!.StateAuthorityId);
        Assert.True(codec.TryDecodePayload(resetEnvelope, out StateSnapshotPayload? resetPayload));
        Assert.Equal(RevisionNumber.Initial.Value, resetPayload!.Revision);
        Assert.Equal(JsonValueKind.Null, resetPayload.Data.GetProperty("value").ValueKind);

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 2, stateAuthorityId: lifecycle.Current));
        subscription.HandleSnapshotRequest("area_a", "req-1"); // re-arms under the new authority
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 2, revision: 3, stateAuthorityId: lifecycle.Current));

        Assert.Equal(sentBeforeRotation + 3, connectionContext.SentPayloads.Count); // the R0 reset, the re-arm baseline, then the Event
    }

    /// <summary>Verifies that a state-authority rotation invalidates a live baseline without un-accepting the area: a repeat subscribe still reports it accepted.</summary>
    [Fact]
    public void HandleSubscribe_AfterStateAuthorityRotated_StillReportsAreaAccepted()
    {
        var lifecycle = new FakeStateAuthorityLifecycle();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], stateAuthorityLifecycle: lifecycle);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", stateAuthorityId: lifecycle.Current));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        lifecycle.NotifyRotated();
        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = Subscribe(subscription, "sub-2", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Empty(rejected);
    }

    /// <summary>Verifies that calling <see cref="PublicStateSubscription.Unsubscribe"/> more than once does not throw.</summary>
    [Fact]
    public void Unsubscribe_CalledTwice_DoesNotThrow()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription();

        subscription.Unsubscribe();
        subscription.Unsubscribe();
    }

    /// <summary>Verifies that unsubscribing without ever binding or subscribing does not throw.</summary>
    [Fact]
    public void Unsubscribe_NeverBoundOrSubscribed_DoesNotThrow()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription();

        subscription.Unsubscribe();
    }

    /// <summary>
    /// Verifies that constructing a subscription does not itself register any handler on the feed or
    /// the play-context tracker -- a connection whose admission never completes (for example a failed
    /// WebSocket handshake, which never calls <see cref="PublicStateSubscription.Bind"/> or
    /// <see cref="PublicStateSubscription.Unsubscribe"/>) must never be rooted on either long-lived
    /// event source.
    /// </summary>
    [Fact]
    public void Constructor_DoesNotSubscribeToFeedOrTracker()
    {
        var tracker = new FakePlayContextTracker();
        (PublicStateSubscription _, _, FakeStatePublicationFeed feed) = BuildSubscription(playContextTracker: tracker);

        Assert.False(feed.HasSubscribers);
        Assert.False(tracker.HasSubscribers);
    }

    /// <summary>Verifies that <see cref="PublicStateSubscription.Bind"/> registers this subscription on the feed and the play-context tracker.</summary>
    [Fact]
    public void Bind_SubscribesToFeedAndTracker()
    {
        var tracker = new FakePlayContextTracker();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(playContextTracker: tracker);

        subscription.Bind(new FakePublicConnectionContext(), SessionId.NewId());

        Assert.True(feed.HasSubscribers);
        Assert.True(tracker.HasSubscribers);
    }

    /// <summary>Verifies that a second <see cref="PublicStateSubscription.Bind"/> call does not register a second handler, which would otherwise dispatch every later event twice.</summary>
    [Fact]
    public void Bind_CalledTwice_DoesNotDoubleSubscribe()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        subscription.Bind(connectionContext, SessionId.NewId());
        int sentBeforeEvent = connectionContext.SentPayloads.Count;

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Equal(sentBeforeEvent + 1, connectionContext.SentPayloads.Count);
    }

    /// <summary>Verifies that <see cref="PublicStateSubscription.Unsubscribe"/> removes this subscription's registration from the feed, the play-context tracker, and the state-authority lifecycle.</summary>
    [Fact]
    public void Unsubscribe_AfterBind_RemovesSubscriptions()
    {
        var tracker = new FakePlayContextTracker();
        var lifecycle = new FakeStateAuthorityLifecycle();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(playContextTracker: tracker, stateAuthorityLifecycle: lifecycle);
        subscription.Bind(new FakePublicConnectionContext(), SessionId.NewId());

        subscription.Unsubscribe();

        Assert.False(feed.HasSubscribers);
        Assert.False(tracker.HasSubscribers);
        Assert.False(lifecycle.HasSubscribers);
    }

    /// <summary>Verifies that a <see cref="PublicStateSubscription.Bind"/> call after <see cref="PublicStateSubscription.Unsubscribe"/> re-arms the registration and events forward again.</summary>
    [Fact]
    public void Unsubscribe_ThenBindAgain_ResubscribesAndForwardsEvents()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        subscription.Unsubscribe();
        Assert.False(feed.HasSubscribers);

        subscription.Bind(connectionContext, SessionId.NewId());

        Assert.True(feed.HasSubscribers);
        int sentBeforeEvent = connectionContext.SentPayloads.Count;
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));
        Assert.Equal(sentBeforeEvent + 1, connectionContext.SentPayloads.Count);
    }

    /// <summary>Verifies that a Snapshot change for an area still <see cref="AreaDeliveryPhase.AwaitingBaseline"/> is discarded rather than sent or buffered.</summary>
    [Fact]
    public void SnapshotChanged_AreaAwaitingBaseline_Discarded()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        // HandleSubscribe alone accepts the area (so OnSnapshotChanged's acceptedAreas gate passes)
        // without ever establishing a baseline, leaving it at the default AwaitingBaseline phase.
        subscription.HandleSubscribe(["area_a"], reservedControlCapacity: 0);

        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 1));

        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that a Snapshot change for a Live area is sent immediately on the Data lane's keyed-replaceable slot.</summary>
    [Fact]
    public void SnapshotChanged_AreaLive_SendsOnDataLaneKeyedSlot()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 2));

        (StateAreaId areaId, byte[] bytes) = Assert.Single(connectionContext.SentSnapshots);
        Assert.Equal("area_a", areaId.Value);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(2UL, payload!.Revision);
    }

    /// <summary>
    /// Verifies that Snapshot changes arriving while the establishing baseline's own fetch is still in
    /// flight -- the same reentrancy window <see cref="OnEventOccurred_DuringSnapshotFetch_EventIsHeldNotDiscarded"/>
    /// exercises for Events -- are buffered as a single replaceable slot rather than queued: a later
    /// value replaces an earlier one, and only the newest is sent once the baseline commits Live.
    /// </summary>
    [Fact]
    public void SnapshotChanged_DuringRecovering_BuffersOnlyLatestRevision()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 11));
            feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 12));
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        // The baseline itself (revision 10) goes through TrySend/SentPayloads; only the flushed
        // pending value goes through TrySendSnapshot/SentSnapshots, so exactly one entry here proves
        // both that buffering replaced rather than queued, and that only the newest was flushed.
        (StateAreaId areaId, byte[] bytes) = Assert.Single(connectionContext.SentSnapshots);
        Assert.Equal("area_a", areaId.Value);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.True(codec.TryDecodePayload(envelope!, out StateSnapshotPayload? payload));
        Assert.Equal(12UL, payload!.Revision);
    }

    /// <summary>Verifies that a Snapshot buffered during recovery is discarded, not resent, when the establishing baseline is already newer than it.</summary>
    [Fact]
    public void SnapshotChanged_PendingSnapshotSupersededByFreshBaseline_Discarded()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 5)); // older than the baseline about to be admitted
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Empty(connectionContext.SentSnapshots); // discarded as superseded, not resent
    }

    /// <summary>
    /// Verifies that a play-context transition landing while a Snapshot is buffered during the
    /// establishing baseline's own fetch -- mirroring
    /// <see cref="TryEstablishBaseline_EpochSupersededDuringSnapshotFetch_SendsBoundaryResetThenFreshBaseline"/>
    /// -- abandons that attempt (and its buffered value) entirely, rather than flushing the stale
    /// generation's pending value once a later, legitimate baseline commits.
    /// </summary>
    [Fact]
    public void SnapshotChanged_PlayContextTransitionDuringRecovering_ClearsPendingSnapshot()
    {
        var tracker = new FakePlayContextTracker();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10, playContextGeneration: 0));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        PlayContextId newContext = PlayContextId.NewId();
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 11, playContextGeneration: 0)); // buffered
            tracker.NotifyTransition(newContext); // generation 1, mid-fetch: supersedes this attempt's epoch
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Single(connectionContext.SentPayloads); // only the new generation's R0 reset is sent
        Assert.Empty(connectionContext.SentSnapshots); // and its buffered generation-0 pending value never flushes

        // A later, legitimate attempt under the new generation sends only its own baseline.
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1, playContextId: newContext, playContextGeneration: 1));
        subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that a Snapshot change for an area this connection never accepted is discarded, symmetric with <see cref="OnEventOccurred_UnacceptedArea_DoesNotForward"/>.</summary>
    [Fact]
    public void SnapshotChanged_UnacceptedArea_DoesNotForward()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 1));

        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that a Snapshot change arriving after this connection has unsubscribed is not forwarded, symmetric with <see cref="OnEventOccurred_AfterUnsubscribe_DoesNotForward"/>.</summary>
    [Fact]
    public void SnapshotChanged_AfterUnsubscribe_DoesNotForward()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        subscription.Unsubscribe();
        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 2));

        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that a live area's Snapshot change is discarded, not forwarded, when its own captured play-context generation is already stale relative to the tracker's current one, symmetric with <see cref="OnEventOccurred_StaleEventGeneration_DoesNotForward"/>.</summary>
    [Fact]
    public void SnapshotChanged_StaleGeneration_DoesNotForward()
    {
        var tracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        tracker.NotifyTransition(context); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextId: context, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 2, playContextId: context, playContextGeneration: 0)); // stale generation

        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>
    /// Verifies that a Snapshot change arriving while the baseline is still being admitted (the same
    /// still-<see cref="AreaDeliveryPhase.Recovering"/> window <see cref="OnEventOccurred_NewEventArrivesWhileHeldEventsAreDraining_DeliveredAfterAllPreviouslyHeldEventsInOrder"/>
    /// exercises for Events) is buffered rather than sent immediately, and is flushed once the area
    /// commits Live.
    /// </summary>
    [Fact]
    public void SnapshotChanged_ArrivesWhileBaselineIsBeingAdmitted_BufferedAndFlushedAfterCommit()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null; // fires once, during the baseline's own admission -- still Recovering
            feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 20));
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Single(connectionContext.SentPayloads); // the baseline only -- the buffered value did not race ahead of it
        (StateAreaId areaId, byte[] bytes) = Assert.Single(connectionContext.SentSnapshots);
        Assert.Equal("area_a", areaId.Value);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.True(codec.TryDecodePayload(envelope!, out StateSnapshotPayload? payload));
        Assert.Equal(20UL, payload!.Revision);
    }

    /// <summary>Polls a condition until it becomes true, failing the test if it never does within a bounded time.</summary>
    /// <param name="condition">The condition to poll.</param>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met within the expected time.");
            await Task.Delay(2);
        }
    }
}
