namespace DovahLink.Host.Client.Transport;

/// <summary>
/// Bounded configuration for one public WebSocket connection's deadlines, input limits, and outbound
/// queue capacity. Defaults to the approved values in <see cref="Constants"/>; a test may override
/// individual values to exercise a bound without a real-time wait.
/// </summary>
public sealed record PublicWebSocketTransportOptions
{
    /// <summary>How long a newly accepted connection may take to complete the WebSocket upgrade handshake.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = Constants.PublicWebSocketHandshakeTimeout;

    /// <summary>The interval of silence after which the connection sends a WebSocket-level keep-alive ping.</summary>
    public TimeSpan IdleTimeout { get; init; } = Constants.PublicWebSocketIdleTimeout;

    /// <summary>How long the connection waits for a keep-alive ping's pong reply before it is treated as unresponsive.</summary>
    public TimeSpan KeepAlivePongTimeout { get; init; } = Constants.PublicWebSocketKeepAlivePongTimeout;

    /// <summary>The maximum byte length of one accumulated inbound message.</summary>
    public int MaxMessageBytes { get; init; } = Constants.PublicWebSocketMaxMessageBytes;

    /// <summary>The maximum number of inbound messages accepted per second.</summary>
    public int MaxInboundMessagesPerSecond { get; init; } = Constants.PublicWebSocketMaxMessagesPerSecond;

    /// <summary>The rolling window used for the inbound message-rate limit.</summary>
    public TimeSpan InboundMessageRateWindow { get; init; } = Constants.PublicWebSocketMessageRateWindow;

    /// <summary>
    /// The maximum number of outbound messages queued before
    /// <see cref="IPublicWebSocketConnection.TrySend"/> fails.
    /// </summary>
    public int OutboundQueueMaxMessages { get; init; } = Constants.PublicWebSocketOutboundQueueMaxMessages;

    /// <summary>The maximum total encoded byte size of the outbound queue.</summary>
    public long OutboundQueueMaxBytes { get; init; } = Constants.PublicWebSocketOutboundQueueMaxBytes;

    /// <summary>The maximum time a graceful close handshake may take before falling back to an abort.</summary>
    public TimeSpan GracefulCloseTimeout { get; init; } = Constants.PublicWebSocketGracefulCloseTimeout;

    /// <summary>The maximum byte length of the raw HTTP Upgrade request line and headers buffered during the handshake.</summary>
    public int MaxHandshakeRequestBytes { get; init; } = Constants.PublicWebSocketMaxHandshakeRequestBytes;
}
