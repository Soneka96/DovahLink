using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests;

/// <summary>Builders for representative test values, grouped by area.</summary>
public static class Fixtures
{
    // ---- Client transport ----

    /// <summary>
    /// Builds a <see cref="PublicWebSocketTransportOptions"/> with representative defaults; a test
    /// that wants the default calls this with no arguments, and a test that needs one bound different
    /// overrides only that parameter.
    /// </summary>
    public static PublicWebSocketTransportOptions BuildPublicWebSocketTransportOptions(
        TimeSpan? handshakeTimeout = null,
        TimeSpan? keepAliveInterval = null,
        TimeSpan? keepAlivePongTimeout = null,
        int? maxMessageBytes = null,
        int? maxInboundMessagesPerSecond = null,
        TimeSpan? inboundMessageRateWindow = null,
        int? outboundQueueMaxMessages = null,
        long? outboundQueueMaxBytes = null,
        TimeSpan? gracefulCloseTimeout = null,
        int? maxHandshakeRequestBytes = null,
        TimeSpan? disconnectNotificationTimeout = null,
        TimeSpan? fragmentAssemblyTimeout = null) =>
        new()
        {
            HandshakeTimeout = handshakeTimeout ?? Constants.PublicWebSocketHandshakeTimeout,
            KeepAliveInterval = keepAliveInterval ?? Constants.PublicWebSocketKeepAliveInterval,
            KeepAlivePongTimeout = keepAlivePongTimeout ?? Constants.PublicWebSocketKeepAlivePongTimeout,
            MaxMessageBytes = maxMessageBytes ?? Constants.PublicWebSocketMaxMessageBytes,
            MaxInboundMessagesPerSecond = maxInboundMessagesPerSecond ?? Constants.PublicWebSocketMaxMessagesPerSecond,
            InboundMessageRateWindow = inboundMessageRateWindow ?? Constants.PublicWebSocketMessageRateWindow,
            OutboundQueueMaxMessages = outboundQueueMaxMessages ?? Constants.PublicWebSocketOutboundQueueMaxMessages,
            OutboundQueueMaxBytes = outboundQueueMaxBytes ?? Constants.PublicWebSocketOutboundQueueMaxBytes,
            GracefulCloseTimeout = gracefulCloseTimeout ?? Constants.PublicWebSocketGracefulCloseTimeout,
            MaxHandshakeRequestBytes = maxHandshakeRequestBytes ?? Constants.PublicWebSocketMaxHandshakeRequestBytes,
            DisconnectNotificationTimeout = disconnectNotificationTimeout ?? Constants.PublicWebSocketDisconnectNotificationTimeout,
            FragmentAssemblyTimeout = fragmentAssemblyTimeout ?? Constants.PublicWebSocketFragmentAssemblyTimeout,
        };

    /// <summary>
    /// Builds a <see cref="PublicWebSocketConnection"/> with representative collaborator defaults; a
    /// test that does not care about the clock, options, or diagnostics passes only what it needs to
    /// override.
    /// </summary>
    public static PublicWebSocketConnection BuildPublicWebSocketConnection(
        Stream stream,
        IPublicWebSocketMessageHandler messageHandler,
        IClock? clock = null,
        PublicWebSocketTransportOptions? options = null,
        IPublicWebSocketTransportDiagnostics? diagnostics = null) =>
        new(
            stream,
            messageHandler,
            clock ?? new SystemClock(),
            options ?? BuildPublicWebSocketTransportOptions(),
            diagnostics ?? new FakePublicWebSocketTransportDiagnostics());

    // ---- Client dispatch ----

    /// <summary>
    /// Builds a <see cref="ClientMessageDispatcher"/> with representative collaborator defaults; a
    /// test that wants the default calls this with no arguments, and a test that needs one
    /// collaborator different overrides only that parameter.
    /// </summary>
    public static ClientMessageDispatcher BuildClientMessageDispatcher(
        IPublicEnvelopeCodec? codec = null,
        ITrustAdminService? trustAdminService = null,
        IPairingCoordinator? pairingCoordinator = null,
        IPairingAdapterNotifier? adapterNotifier = null,
        IPlayContextTracker? playContextTracker = null,
        IClock? clock = null,
        ISessionRegistry? sessionRegistry = null) =>
        new(
            codec ?? new PublicEnvelopeCodec(),
            trustAdminService ?? new FakeTrustAdminService(),
            pairingCoordinator ?? new PairingCoordinator(new FakeTrustStore(), clock ?? new FakeClock()),
            adapterNotifier ?? new FakePairingAdapterNotifier(),
            playContextTracker ?? new FakePlayContextTracker(),
            clock ?? new FakeClock(),
            sessionRegistry ?? new FakeSessionRegistry());

    /// <summary>
    /// Builds a <see cref="ClientMessageDispatcher"/> the same way as <see cref="BuildClientMessageDispatcher"/>,
    /// together with one already-active session for <paramref name="clientId"/> on the returned
    /// <see cref="ConnectionId"/>, registered on a fresh <see cref="FakeSessionRegistry"/> the built
    /// dispatcher uses for its own client-bound authorization guard. A test exercising
    /// <c>pairing_request</c>, <c>pairing_cancel</c>, <c>pairing_renotify</c>, <c>pairing_confirm</c>, or
    /// <c>rename_request</c> -- every message type the dispatcher gates on session liveness -- needs a
    /// genuinely registered session rather than an arbitrary unregistered <see cref="SessionId"/>, or
    /// every such dispatch would be rejected as stale before ever reaching the pairing coordinator or
    /// trust admin service.
    /// </summary>
    /// <param name="clientId">The client identity the registered session belongs to.</param>
    /// <param name="codec">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="trustAdminService">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="pairingCoordinator">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="adapterNotifier">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="playContextTracker">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    /// <param name="clock">Forwarded to <see cref="BuildClientMessageDispatcher"/>.</param>
    public static (ClientMessageDispatcher Dispatcher, SessionId SessionId, ConnectionId ConnectionId) BuildClientMessageDispatcherWithActiveSession(
        ClientId clientId,
        IPublicEnvelopeCodec? codec = null,
        ITrustAdminService? trustAdminService = null,
        IPairingCoordinator? pairingCoordinator = null,
        IPairingAdapterNotifier? adapterNotifier = null,
        IPlayContextTracker? playContextTracker = null,
        IClock? clock = null)
    {
        var sessionRegistry = new FakeSessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        SessionId sessionId = sessionRegistry.Create(clientId, connectionId);
        ClientMessageDispatcher dispatcher = BuildClientMessageDispatcher(
            codec, trustAdminService, pairingCoordinator, adapterNotifier, playContextTracker, clock, sessionRegistry);
        return (dispatcher, sessionId, connectionId);
    }
}
