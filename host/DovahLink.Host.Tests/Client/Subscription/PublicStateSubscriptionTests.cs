using System.Text.Json;
using DovahLink.Host;
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
    /// <summary>The envelope codec used to decode every sent message for content assertions.</summary>
    private readonly PublicEnvelopeCodec codec = new();

    /// <summary>Builds a subscription over a fresh policy, feed, and play-context tracker, with the given areas pre-registered.</summary>
    /// <param name="registeredAreas">The state areas to register before the test runs.</param>
    /// <param name="feed">The feed the subscription reads from; a fresh <see cref="FakeStatePublicationFeed"/> when omitted.</param>
    /// <param name="playContextTracker">The tracker the subscription reads and listens to; a fresh <see cref="FakePlayContextTracker"/> when omitted.</param>
    private (PublicStateSubscription Subscription, RegisteredStateAreaPolicy Policy, FakeStatePublicationFeed Feed) BuildSubscription(
        IEnumerable<string>? registeredAreas = null, FakeStatePublicationFeed? feed = null, IPlayContextTracker? playContextTracker = null)
    {
        var policy = new RegisteredStateAreaPolicy();
        foreach (string area in registeredAreas ?? [])
        {
            policy.TryRegister(new StateAreaId(area));
        }

        FakeStatePublicationFeed resolvedFeed = feed ?? new FakeStatePublicationFeed();
        var subscription = new PublicStateSubscription(policy, resolvedFeed, codec, playContextTracker ?? new FakePlayContextTracker());
        return (subscription, policy, resolvedFeed);
    }

    /// <summary>
    /// Drives a full <c>subscribe</c> exchange the way a caller with no competing Control/Recovery
    /// lane send of its own would: the decision-only <see cref="PublicStateSubscription.HandleSubscribe"/>
    /// immediately followed by <see cref="PublicStateSubscription.EstablishAcceptedBaselines"/> for
    /// whatever it accepted. Most tests care about the combined outcome, not the two-call split
    /// itself -- that split is exercised directly by the tests that name it.
    /// </summary>
    /// <param name="subscription">The subscription under test.</param>
    /// <param name="subscribeMessageId">The <c>subscribe</c> message's own id.</param>
    /// <param name="requestedStateAreas">The state areas the client requested.</param>
    /// <param name="reservedControlCapacity">Forwarded to <see cref="PublicStateSubscription.HandleSubscribe"/>; zero when omitted, since these tests send no competing message of their own.</param>
    private static (IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected) Subscribe(
        PublicStateSubscription subscription, string subscribeMessageId, IReadOnlyList<string> requestedStateAreas, int reservedControlCapacity = 0)
    {
        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe(requestedStateAreas, reservedControlCapacity);
        subscription.EstablishAcceptedBaselines(accepted, subscribeMessageId);
        return (accepted, rejected);
    }

    /// <summary>Builds a representative snapshot value for the given area.</summary>
    private static StateSnapshotPublication BuildSnapshot(
        string area, ulong revision = 1, PlayContextId? playContextId = null, long playContextGeneration = 0) =>
        new(new StateAreaId(area), new RevisionNumber(revision), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { value = 42 }), playContextId, playContextGeneration);

    /// <summary>Builds a representative event value for the given area.</summary>
    private static StateEventPublication BuildEvent(
        string area, ulong baseRevision, ulong revision, PlayContextId? playContextId = null, long playContextGeneration = 0) =>
        new(new StateAreaId(area), new RevisionNumber(baseRevision), new RevisionNumber(revision), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { value = 99 }), playContextId, playContextGeneration);

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

    /// <summary>Verifies that an accepted area with an available snapshot sends it as a baseline through the Control/Recovery lane, correlated to the subscribe message id.</summary>
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
    /// lane never throws or otherwise disrupts the subscribe call that triggered it, and -- the
    /// regression this covers -- that the area is never treated as live: a later Event for it must
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
    /// Verifies the same regression as <see cref="HandleSubscribe_ConnectionDeclinesControlAdmission_AreaStaysNotLive_LaterEventNotForwarded"/>,
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
    /// Verifies the fix for the earlier window of the same event-loss race: an Event raised while the
    /// baseline's own snapshot fetch is still in flight -- before the barrier revision is even known --
    /// is held rather than discarded, since <see cref="AreaDeliveryPhase.Recovering"/> with an unknown
    /// barrier still means "hold," not "not live yet." This is the window that remained open after the
    /// first barrier implementation moved <c>Phase = Recovering</c> to after the fetch instead of
    /// before it.
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
    /// Verifies that a play-context transition landing while the snapshot fetch is still in flight --
    /// bumping the recovery epoch before the second lock block re-validates it -- abandons the attempt
    /// entirely: no baseline is sent under the now-superseded epoch at all, not merely one that fails
    /// to commit live.
    /// </summary>
    [Fact]
    public void TryEstablishBaseline_EpochSupersededDuringSnapshotFetch_AbandonsAttemptWithoutSending()
    {
        var tracker = new FakePlayContextTracker();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10, playContextGeneration: 0));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            tracker.NotifyTransition(PlayContextId.NewId()); // generation 1, mid-fetch: supersedes this attempt's epoch
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Empty(connectionContext.SentPayloads); // abandoned before ever reaching admission

        // A later, legitimate attempt under the new generation still works normally.
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1, playContextGeneration: 1));
        subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.Single(connectionContext.SentPayloads);
    }

    /// <summary>
    /// Verifies the fix for the second race: a brand-new Event arriving while previously-held Events
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
        tracker.NotifyTransition(PlayContextId.NewId()); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11, playContextGeneration: 1)); // held
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 11, revision: 12, playContextGeneration: 1)); // held
        };
        int trySendCount = 0;
        connectionContext.OnTrySend = () =>
        {
            trySendCount++;
            if (trySendCount == 2) // draining the first held Event, with the second still queued behind it
            {
                connectionContext.OnTrySend = null;
                tracker.NotifyTransition(PlayContextId.NewId()); // generation 2: clears the queue, abandons this attempt
            }
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        // The baseline, then only the one held Event whose send was already in flight when the
        // transition landed -- the still-queued second Event must never be sent under the old context.
        Assert.Equal(2, connectionContext.SentPayloads.Count);
        Assert.DoesNotContain(
            connectionContext.SentPayloads.Skip(1),
            sent => codec.TryDecode(sent.Payload, out PublicEnvelope? envelope)
                && codec.TryDecodePayload(envelope, out StateEventPayload? payload)
                && payload!.Revision == 12UL);

        // Re-arm under the new generation and confirm a fresh Event forwards -- proving the area
        // recovered cleanly (AwaitingBaseline, not incorrectly left Live under the old generation).
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1, playContextGeneration: 2));
        subscription.HandleSnapshotRequest("area_a", "req-1");
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2, playContextGeneration: 2));

        Assert.Contains(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);
    }

    /// <summary>
    /// Verifies the fix for the original event-loss race: an Event above the baseline's own revision,
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
    /// is in flight must not let that send's eventual (successful) completion incorrectly commit the
    /// area live under the now-superseded generation.
    /// </summary>
    [Fact]
    public void HandleSubscribe_ContextTransitionedDuringSend_CompletionIgnoredAreaStaysNotLive()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId()); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            tracker.NotifyTransition(PlayContextId.NewId()); // generation 2, mid-send
        };

        Subscribe(subscription, "sub-1", ["area_a"]);
        Assert.Single(connectionContext.SentPayloads); // the stale-by-the-time-it-lands baseline still gets sent

        // A fresh Event under the new generation must not forward: the superseded completion must not
        // have marked the area live.
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2, playContextGeneration: 2));

        Assert.Single(connectionContext.SentPayloads); // unchanged; the Event never forwards
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
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal(99UL, payload!.Revision);
    }

    /// <summary>Verifies that a live area's Event is discarded, not forwarded, when its own captured play-context generation is already stale relative to the tracker's current one -- symmetric with the same check on a baseline snapshot.</summary>
    [Fact]
    public void OnEventOccurred_StaleEventGeneration_DoesNotForward()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId()); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentAfterBaseline = connectionContext.SentPayloads.Count;

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2, playContextGeneration: 0)); // stale generation

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
        tracker.NotifyTransition(PlayContextId.NewId()); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 10, playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        connectionContext.OnTrySend = () =>
        {
            connectionContext.OnTrySend = null;
            feed.RaiseEvent(BuildEvent("area_a", baseRevision: 10, revision: 11, playContextGeneration: 1)); // held while Recovering
            tracker.NotifyTransition(PlayContextId.NewId()); // generation 2; must discard the held Event above, not release it
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.DoesNotContain(connectionContext.SentPayloads, sent => sent.Lane == PublicOutboundLane.Data);

        // Re-arm under the new generation and confirm a fresh Event forwards normally -- proving the
        // area recovered cleanly rather than being left in a broken state by the discard.
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1, playContextGeneration: 2));
        subscription.HandleSnapshotRequest("area_a", "req-1");
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2, playContextGeneration: 2));

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
    /// Verifies that a play-context transition invalidates a live area's baseline: an Event that
    /// would otherwise have forwarded stops forwarding once the transition commits, until the area is
    /// re-armed.
    /// </summary>
    [Fact]
    public void OnEventOccurred_ContextTransitioned_StopsForwardingUntilReArmed()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId()); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentBeforeTransition = connectionContext.SentPayloads.Count;

        tracker.NotifyTransition(PlayContextId.NewId()); // generation 2
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2, playContextGeneration: 2));

        Assert.Equal(sentBeforeTransition, connectionContext.SentPayloads.Count);

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 2, playContextGeneration: 2));
        subscription.HandleSnapshotRequest("area_a", "req-1"); // re-arms under the new context
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 2, revision: 3, playContextGeneration: 2));

        Assert.Equal(sentBeforeTransition + 2, connectionContext.SentPayloads.Count); // the re-arm baseline, then the event
    }

    /// <summary>Verifies that a play-context transition invalidates a live baseline without un-accepting the area: a repeat subscribe still reports it accepted.</summary>
    [Fact]
    public void HandleSubscribe_AfterContextTransitioned_StillReportsAreaAccepted()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId()); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        tracker.NotifyTransition(PlayContextId.NewId()); // generation 2
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

    /// <summary>Verifies that <see cref="PublicStateSubscription.Unsubscribe"/> removes this subscription's registration from the feed and the play-context tracker.</summary>
    [Fact]
    public void Unsubscribe_AfterBind_RemovesSubscriptions()
    {
        var tracker = new FakePlayContextTracker();
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(playContextTracker: tracker);
        subscription.Bind(new FakePublicConnectionContext(), SessionId.NewId());

        subscription.Unsubscribe();

        Assert.False(feed.HasSubscribers);
        Assert.False(tracker.HasSubscribers);
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
    /// <see cref="TryEstablishBaseline_EpochSupersededDuringSnapshotFetch_AbandonsAttemptWithoutSending"/>
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
        feed.OnTryGetSnapshot = () =>
        {
            feed.OnTryGetSnapshot = null;
            feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 11, playContextGeneration: 0)); // buffered
            tracker.NotifyTransition(PlayContextId.NewId()); // generation 1, mid-fetch: supersedes this attempt's epoch
        };

        Subscribe(subscription, "sub-1", ["area_a"]);

        Assert.Empty(connectionContext.SentPayloads); // the generation-0 baseline itself was abandoned
        Assert.Empty(connectionContext.SentSnapshots); // and its buffered generation-0 pending value never flushes

        // A later, legitimate attempt under the new generation sends only its own baseline.
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 1, playContextGeneration: 1));
        subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.Single(connectionContext.SentPayloads);
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
        tracker.NotifyTransition(PlayContextId.NewId()); // generation 1
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", playContextGeneration: 1));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        feed.RaiseSnapshotChanged(BuildSnapshot("area_a", revision: 2, playContextGeneration: 0)); // stale generation

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
}
