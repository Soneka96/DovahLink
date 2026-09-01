using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Tests.Client.Transport;

/// <summary>Tests for <see cref="PublicWebSocketTransportOptions"/>.</summary>
public class PublicWebSocketTransportOptionsTests
{
    /// <summary>Verifies that every default option value matches the corresponding approved constant.</summary>
    [Fact]
    public void DefaultConstructor_UsesApprovedConstants()
    {
        var options = Fixtures.BuildPublicWebSocketTransportOptions();

        Assert.Equal(Constants.PublicWebSocketHandshakeTimeout, options.HandshakeTimeout);
        Assert.Equal(Constants.PublicWebSocketKeepAliveInterval, options.KeepAliveInterval);
        Assert.Equal(Constants.PublicWebSocketKeepAlivePongTimeout, options.KeepAlivePongTimeout);
        Assert.Equal(Constants.PublicWebSocketMaxMessageBytes, options.MaxMessageBytes);
        Assert.Equal(Constants.PublicWebSocketMaxMessagesPerSecond, options.MaxInboundMessagesPerSecond);
        Assert.Equal(Constants.PublicWebSocketMessageRateWindow, options.InboundMessageRateWindow);
        Assert.Equal(Constants.PublicWebSocketOutboundQueueMaxMessages, options.OutboundQueueMaxMessages);
        Assert.Equal(Constants.PublicWebSocketOutboundQueueMaxBytes, options.OutboundQueueMaxBytes);
        Assert.Equal(Constants.PublicWebSocketGracefulCloseTimeout, options.GracefulCloseTimeout);
        Assert.Equal(Constants.PublicWebSocketMaxHandshakeRequestBytes, options.MaxHandshakeRequestBytes);
        Assert.Equal(Constants.PublicWebSocketDisconnectNotificationTimeout, options.DisconnectNotificationTimeout);
    }

    /// <summary>Verifies that a single overridden property leaves every other default untouched.</summary>
    [Fact]
    public void Build_WithOneOverride_OverridesOnlyTheGivenParameter()
    {
        PublicWebSocketTransportOptions options = Fixtures.BuildPublicWebSocketTransportOptions(keepAliveInterval: TimeSpan.FromMilliseconds(50));

        Assert.Equal(TimeSpan.FromMilliseconds(50), options.KeepAliveInterval);
        Assert.Equal(Constants.PublicWebSocketHandshakeTimeout, options.HandshakeTimeout);
        Assert.Equal(Constants.PublicWebSocketKeepAlivePongTimeout, options.KeepAlivePongTimeout);
    }

    /// <summary>
    /// Verifies that the approved keep-alive interval and pong-timeout constants sum to exactly the
    /// approved 60-second total liveness deadline, per <c>ai/context/protocol/security.md</c>'s "idle
    /// connection timeout: 60 seconds without a valid heartbeat or message" -- not less, and critically
    /// not more, since .NET's managed WebSocket keep-alive treats the two bounds as additive.
    /// </summary>
    [Fact]
    public void LivenessBudget_KeepAliveIntervalPlusPongTimeout_EqualsSixtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Constants.PublicWebSocketLivenessTimeout);
        Assert.Equal(
            Constants.PublicWebSocketLivenessTimeout,
            Constants.PublicWebSocketKeepAliveInterval + Constants.PublicWebSocketKeepAlivePongTimeout);
    }
}
