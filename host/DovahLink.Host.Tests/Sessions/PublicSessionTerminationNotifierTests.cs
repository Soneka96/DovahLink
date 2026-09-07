using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Sessions;

/// <summary>Tests for <see cref="PublicSessionTerminationNotifier"/>.</summary>
public class PublicSessionTerminationNotifierTests
{
    /// <summary>Verifies that a registered matching target receives a best-effort <c>session_invalidated</c> then is closed.</summary>
    [Fact]
    public async Task NotifyAndCloseAsync_RegisteredMatchingTarget_SendsSessionInvalidatedThenCloses()
    {
        var registry = new PublicSessionConnectionRegistry();
        var codec = new PublicEnvelopeCodec();
        var notifier = new PublicSessionTerminationNotifier(registry, codec, new FakePlayContextTracker());
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        var connection = new PublicConnectionContext(fakeConnection);
        SessionInvalidationTarget target = Fixtures.BuildSessionInvalidationTarget(reason: SessionInvalidationReason.Revoked);
        registry.Register(target.SessionId, target.ConnectionId, connection);

        await notifier.NotifyAndCloseAsync(target);

        byte[] sent = Assert.Single(fakeConnection.SentPayloads);
        Assert.True(codec.TryDecode(sent, out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.SessionInvalidated, envelope!.MessageType);
        Assert.Equal(target.SessionId.ToString(), envelope.SessionId);
        Assert.Null(envelope.CorrelationId);
        Assert.True(codec.TryDecodePayload(envelope, out SessionInvalidatedPayload? payload));
        Assert.Equal(SessionInvalidationReason.Revoked, payload!.Reason);
        Assert.Equal(1, fakeConnection.RequestCloseCalls);
    }

    /// <summary>Verifies that every wire reason value round-trips through the encoded payload.</summary>
    [Theory]
    [InlineData(SessionInvalidationReason.Revoked)]
    [InlineData(SessionInvalidationReason.Blocked)]
    [InlineData(SessionInvalidationReason.TrustReset)]
    [InlineData(SessionInvalidationReason.FactoryReset)]
    public async Task NotifyAndCloseAsync_EveryReason_EncodesItOnTheWire(SessionInvalidationReason reason)
    {
        var registry = new PublicSessionConnectionRegistry();
        var codec = new PublicEnvelopeCodec();
        var notifier = new PublicSessionTerminationNotifier(registry, codec, new FakePlayContextTracker());
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        SessionInvalidationTarget target = Fixtures.BuildSessionInvalidationTarget(reason: reason);
        registry.Register(target.SessionId, target.ConnectionId, new PublicConnectionContext(fakeConnection));

        await notifier.NotifyAndCloseAsync(target);

        byte[] sent = Assert.Single(fakeConnection.SentPayloads);
        Assert.True(codec.TryDecode(sent, out PublicEnvelope? envelope));
        Assert.True(codec.TryDecodePayload(envelope!, out SessionInvalidatedPayload? payload));
        Assert.Equal(reason, payload!.Reason);
    }

    /// <summary>Verifies that the notification carries the tracker's current play-context snapshot.</summary>
    [Fact]
    public async Task NotifyAndCloseAsync_StampsTheCurrentPlayContextSnapshot()
    {
        var registry = new PublicSessionConnectionRegistry();
        var codec = new PublicEnvelopeCodec();
        var playContextTracker = new FakePlayContextTracker();
        PlayContextId playContextId = PlayContextId.NewId();
        playContextTracker.NotifyTransition(playContextId);
        var notifier = new PublicSessionTerminationNotifier(registry, codec, playContextTracker);
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        SessionInvalidationTarget target = Fixtures.BuildSessionInvalidationTarget();
        registry.Register(target.SessionId, target.ConnectionId, new PublicConnectionContext(fakeConnection));

        await notifier.NotifyAndCloseAsync(target);

        byte[] sent = Assert.Single(fakeConnection.SentPayloads);
        Assert.True(codec.TryDecode(sent, out PublicEnvelope? envelope));
        Assert.Equal(playContextId.ToString(), envelope!.PlayContextId);
    }

    /// <summary>Verifies that a target with no registered connection (already disconnected on its own) is a no-op.</summary>
    [Fact]
    public async Task NotifyAndCloseAsync_UnregisteredTarget_DoesNothing()
    {
        var registry = new PublicSessionConnectionRegistry();
        var notifier = new PublicSessionTerminationNotifier(registry, new PublicEnvelopeCodec(), new FakePlayContextTracker());
        SessionInvalidationTarget target = Fixtures.BuildSessionInvalidationTarget();

        await notifier.NotifyAndCloseAsync(target);
    }

    /// <summary>
    /// Verifies that a target registered under a different session than the one currently occupying
    /// its connection identity is never notified or closed -- the exact guarantee that a stale
    /// target can never reach a different, newer connection that reused the single admission slot.
    /// </summary>
    [Fact]
    public async Task NotifyAndCloseAsync_TargetSessionDoesNotMatchCurrentRegistration_NeverTouchesTheConnection()
    {
        var registry = new PublicSessionConnectionRegistry();
        var notifier = new PublicSessionTerminationNotifier(registry, new PublicEnvelopeCodec(), new FakePlayContextTracker());
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = true };
        ConnectionId connectionId = ConnectionId.NewId();
        registry.Register(SessionId.NewId(), connectionId, new PublicConnectionContext(fakeConnection));
        SessionInvalidationTarget staleTarget = Fixtures.BuildSessionInvalidationTarget(connectionId: connectionId);

        await notifier.NotifyAndCloseAsync(staleTarget);

        Assert.Empty(fakeConnection.SentPayloads);
        Assert.Equal(0, fakeConnection.RequestCloseCalls);
    }

    /// <summary>Verifies that the connection is still closed even when the best-effort notification send fails.</summary>
    [Fact]
    public async Task NotifyAndCloseAsync_SendFails_StillCloses()
    {
        var registry = new PublicSessionConnectionRegistry();
        var notifier = new PublicSessionTerminationNotifier(registry, new PublicEnvelopeCodec(), new FakePlayContextTracker());
        var fakeConnection = new FakePublicWebSocketConnection(Stream.Null) { TrySendResult = false };
        SessionInvalidationTarget target = Fixtures.BuildSessionInvalidationTarget();
        registry.Register(target.SessionId, target.ConnectionId, new PublicConnectionContext(fakeConnection));

        await notifier.NotifyAndCloseAsync(target);

        Assert.Equal(1, fakeConnection.RequestCloseCalls);
    }
}
