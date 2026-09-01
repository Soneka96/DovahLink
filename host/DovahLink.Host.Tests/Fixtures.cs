using DovahLink.Host.Client.Transport;

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
        TimeSpan? disconnectNotificationTimeout = null) =>
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
        };
}
