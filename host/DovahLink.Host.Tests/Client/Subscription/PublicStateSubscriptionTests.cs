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
    private (PublicStateSubscription Subscription, RegisteredStateAreaPolicy Policy, FakeStatePublicationFeed Feed) BuildSubscription(
        IEnumerable<string>? registeredAreas = null, FakeStatePublicationFeed? feed = null)
    {
        var policy = new RegisteredStateAreaPolicy();
        foreach (string area in registeredAreas ?? [])
        {
            policy.TryRegister(new StateAreaId(area));
        }

        FakeStatePublicationFeed resolvedFeed = feed ?? new FakeStatePublicationFeed();
        var subscription = new PublicStateSubscription(policy, resolvedFeed, codec, new FakePlayContextTracker());
        return (subscription, policy, resolvedFeed);
    }

    /// <summary>Builds a representative snapshot value for the given area.</summary>
    private static StateSnapshotPublication BuildSnapshot(string area, ulong revision = 1) =>
        new(new StateAreaId(area), new RevisionNumber(revision), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { value = 42 }));

    /// <summary>Builds a representative event value for the given area.</summary>
    private static StateEventPublication BuildEvent(string area, ulong baseRevision, ulong revision) =>
        new(new StateAreaId(area), new RevisionNumber(baseRevision), new RevisionNumber(revision), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { value = 99 }));

    /// <summary>Verifies that a requested area which is registered is accepted.</summary>
    [Fact]
    public void HandleSubscribe_RegisteredArea_Accepts()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe("sub-1", ["area_a"]);

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

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe("sub-1", ["area_a"]);

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

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe("sub-1", ["area_a", "area_b"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Equal(["area_b"], rejected);
    }

    /// <summary>Verifies that an accepted area with an available snapshot sends it, correlated to the subscribe message id, before returning.</summary>
    [Fact]
    public void HandleSubscribe_RegisteredAreaWithAvailableSnapshot_SendsSnapshotCorrelatedToSubscribeMessageId()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 7));
        var connectionContext = new FakePublicConnectionContext();
        var sessionId = SessionId.NewId();
        subscription.Bind(connectionContext, sessionId);

        subscription.HandleSubscribe("sub-1", ["area_a"]);

        (StateAreaId areaId, byte[] bytes) = Assert.Single(connectionContext.SentSnapshots);
        Assert.Equal(new StateAreaId("area_a"), areaId);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateSnapshot, envelope!.MessageType);
        Assert.Equal("sub-1", envelope.CorrelationId);
        Assert.Equal(sessionId.ToString(), envelope.SessionId);
        Assert.True(codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload));
        Assert.Equal("area_a", payload!.StateArea);
        Assert.Equal(7UL, payload.Revision);
    }

    /// <summary>Verifies that an accepted area with no available snapshot sends nothing, without failing the subscribe call.</summary>
    [Fact]
    public void HandleSubscribe_RegisteredAreaWithNoSnapshotAvailable_AcceptsButSendsNothing()
    {
        (PublicStateSubscription subscription, _, _) = BuildSubscription(["area_a"]);
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, _) = subscription.HandleSubscribe("sub-1", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Empty(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that subscribing to an already-accepted area again does not resend its snapshot.</summary>
    [Fact]
    public void HandleSubscribe_AlreadyAcceptedArea_DoesNotResendSnapshot()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        subscription.HandleSubscribe("sub-1", ["area_a"]);

        (IReadOnlyList<string> accepted, _) = subscription.HandleSubscribe("sub-2", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Single(connectionContext.SentSnapshots);
    }

    /// <summary>Verifies that the accept/reject decision does not depend on <see cref="PublicStateSubscription.Bind"/> having been called, even though nothing can be sent yet.</summary>
    [Fact]
    public void HandleSubscribe_BeforeBind_StillReportsAcceptedWithoutThrowing()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));

        (IReadOnlyList<string> accepted, IReadOnlyList<string> rejected) = subscription.HandleSubscribe("sub-1", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
        Assert.Empty(rejected);
    }

    /// <summary>Verifies that a snapshot request for a registered area with an available value sends it, correlated to the request's own message id.</summary>
    [Fact]
    public void HandleSnapshotRequest_RegisteredAreaWithSnapshot_SendsSnapshotCorrelatedToRequestId()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a", revision: 3));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());

        bool result = subscription.HandleSnapshotRequest("area_a", "req-1");

        Assert.True(result);
        (StateAreaId areaId, byte[] bytes) = Assert.Single(connectionContext.SentSnapshots);
        Assert.Equal(new StateAreaId("area_a"), areaId);
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
        Assert.Empty(connectionContext.SentSnapshots);
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
        Assert.Empty(connectionContext.SentSnapshots);
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
        subscription.HandleSubscribe("sub-1", ["area_a"]); // accepted, but no snapshot was available yet

        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        subscription.HandleSnapshotRequest("area_a", "req-1");
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
        Assert.Equal(PublicOutboundLane.Data, lane);
        Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.StateEvent, envelope!.MessageType);
    }

    /// <summary>
    /// Verifies that the wrapped connection declining a snapshot -- the deferral contract
    /// <see cref="IPublicConnectionContext.TrySendSnapshot"/> documents -- never throws or otherwise
    /// disrupts the subscribe call that triggered it.
    /// </summary>
    [Fact]
    public void HandleSubscribe_ConnectionDeclinesSnapshot_DoesNotThrow()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext { TrySendSnapshotResult = false };
        subscription.Bind(connectionContext, SessionId.NewId());

        (IReadOnlyList<string> accepted, _) = subscription.HandleSubscribe("sub-1", ["area_a"]);

        Assert.Equal(["area_a"], accepted);
    }

    /// <summary>Verifies that an event for an accepted area whose snapshot has already been sent is forwarded, decoding to the expected content.</summary>
    [Fact]
    public void OnEventOccurred_AcceptedAreaWithSnapshotSent_ForwardsEvent()
    {
        (PublicStateSubscription subscription, _, FakeStatePublicationFeed feed) = BuildSubscription(["area_a"]);
        feed.SetSnapshot(new StateAreaId("area_a"), BuildSnapshot("area_a"));
        var connectionContext = new FakePublicConnectionContext();
        subscription.Bind(connectionContext, SessionId.NewId());
        subscription.HandleSubscribe("sub-1", ["area_a"]);

        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        (byte[] bytes, PublicOutboundLane lane) = Assert.Single(connectionContext.SentPayloads);
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
        subscription.HandleSubscribe("sub-1", ["area_a"]); // accepted, but no snapshot was available to send

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
        subscription.HandleSubscribe("sub-1", ["area_a"]);

        subscription.Unsubscribe();
        feed.RaiseEvent(BuildEvent("area_a", baseRevision: 1, revision: 2));

        Assert.Empty(connectionContext.SentPayloads);
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
