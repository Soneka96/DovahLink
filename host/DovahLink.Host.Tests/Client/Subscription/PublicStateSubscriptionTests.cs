using System.Text.Json;
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
        tracker.NotifyTransition(PlayContextId.NewId());
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);
        int sentBeforeTransition = connectionContext.SentPayloads.Count;

        tracker.NotifyTransition(PlayContextId.NewId());
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Equal(sentBeforeTransition, connectionContext.SentPayloads.Count);

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 2));
        subscription.HandleSnapshotRequest("area_a", "req-1"); // re-arms under the new context
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 2, revision: 3));

        Assert.Equal(sentBeforeTransition + 2, connectionContext.SentPayloads.Count); // the re-arm baseline, then the event
    }

    /// <summary>Verifies that a play-context transition invalidates a live baseline without un-accepting the area: a repeat subscribe still reports it accepted.</summary>
    [Fact]
    public void HandleSubscribe_AfterContextTransitioned_StillReportsAreaAccepted()
    {
        var tracker = new FakePlayContextTracker();
        tracker.NotifyTransition(PlayContextId.NewId());
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"], playContextTracker: tracker);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        Subscribe(subscription, "sub-1", ["area_a"]);

        tracker.NotifyTransition(PlayContextId.NewId());
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
}
