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

    /// <summary>
    /// The interval of inbound silence after which the connection sends a WebSocket-level keep-alive
    /// ping. Configured with intentional headroom below the liveness ceiling rather than derived to
    /// sum to it exactly; see <see cref="Constants.PublicWebSocketLivenessTimeout"/> for why.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; init; } = Constants.PublicWebSocketKeepAliveInterval;

    /// <summary>
    /// How long the connection waits for a keep-alive ping's pong reply before it is treated as
    /// unresponsive. Configured together with <see cref="KeepAliveInterval"/> to stay within the
    /// liveness ceiling in <see cref="Constants.PublicWebSocketLivenessTimeout"/> with headroom, not
    /// as an exact sum.
    /// </summary>
    public TimeSpan KeepAlivePongTimeout { get; init; } = Constants.PublicWebSocketKeepAlivePongTimeout;

    /// <summary>The maximum byte length of one accumulated inbound message.</summary>
    public int MaxMessageBytes { get; init; } = Constants.PublicWebSocketMaxMessageBytes;

    /// <summary>The maximum number of inbound messages accepted per second.</summary>
    public int MaxInboundMessagesPerSecond { get; init; } = Constants.PublicWebSocketMaxMessagesPerSecond;

    /// <summary>The rolling window used for the inbound message-rate limit.</summary>
    public TimeSpan InboundMessageRateWindow { get; init; } = Constants.PublicWebSocketMessageRateWindow;

    /// <summary>
    /// The maximum number of <see cref="PublicOutboundLane.ControlOrRecovery"/>-lane outbound messages
    /// this connection may own at once before <see cref="IPublicWebSocketConnection.TrySend"/> fails
    /// for that lane -- counting both a frame still waiting to be sent and one the writer has already
    /// dequeued but not yet finished sending, not merely how many currently sit in the waiting queue.
    /// Reserved separately from <see cref="DataOutboundQueueMaxMessages"/> so a slow client under
    /// data-publication pressure cannot delay or crowd out timely control-message delivery.
    /// </summary>
    public int ControlOutboundQueueMaxMessages { get; init; } = Constants.PublicWebSocketControlOutboundQueueMaxMessages;

    /// <summary>
    /// The maximum number of <see cref="PublicOutboundLane.Data"/>-lane outbound messages this
    /// connection may own at once before <see cref="IPublicWebSocketConnection.TrySend"/> fails for
    /// that lane, counted the same way as <see cref="ControlOutboundQueueMaxMessages"/>.
    /// </summary>
    public int DataOutboundQueueMaxMessages { get; init; } = Constants.PublicWebSocketDataOutboundQueueMaxMessages;

    /// <summary>
    /// The maximum total encoded byte size of the outbound queue, shared across both lanes in
    /// <see cref="PublicOutboundLane"/> rather than split per lane.
    /// </summary>
    public long OutboundQueueMaxBytes { get; init; } = Constants.PublicWebSocketOutboundQueueMaxBytes;

    /// <summary>The maximum time a graceful close handshake may take before falling back to an abort.</summary>
    public TimeSpan GracefulCloseTimeout { get; init; } = Constants.PublicWebSocketGracefulCloseTimeout;

    /// <summary>The maximum byte length of the raw HTTP Upgrade request line and headers buffered during the handshake.</summary>
    public int MaxHandshakeRequestBytes { get; init; } = Constants.PublicWebSocketMaxHandshakeRequestBytes;

    /// <summary>
    /// The maximum time the connection waits for its injected message handler's disconnect
    /// notification before proceeding with teardown regardless.
    /// </summary>
    public TimeSpan DisconnectNotificationTimeout { get; init; } = Constants.PublicWebSocketDisconnectNotificationTimeout;

    /// <summary>
    /// The maximum time one incomplete fragmented inbound message may remain open, anchored to its
    /// first fragment. Independent of, and never a substitute for, <see cref="MaxInboundMessagesPerSecond"/>
    /// (completed-message rate) or established WebSocket-level liveness (peer/socket aliveness).
    /// </summary>
    public TimeSpan FragmentAssemblyTimeout { get; init; } = Constants.PublicWebSocketFragmentAssemblyTimeout;
}
