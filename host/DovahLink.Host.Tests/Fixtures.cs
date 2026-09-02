using DovahLink.Host.Client.Transport;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;

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
}
