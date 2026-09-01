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
    /// Verifies that the approved keep-alive interval and pong-timeout constants stay strictly below
    /// the approved 60-second liveness ceiling (<c>ai/context/protocol/security.md</c>'s "idle
    /// connection timeout: 60 seconds without a valid heartbeat or message"), leaving intentional
    /// headroom rather than summing to it exactly -- .NET's managed WebSocket keep-alive scheduler
    /// polls rather than firing at an exact instant, so an exact sum could overshoot the ceiling.
    /// </summary>
    [Fact]
    public void LivenessBudget_KeepAliveIntervalPlusPongTimeout_StaysBelowSixtySecondsWithHeadroom()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Constants.PublicWebSocketLivenessTimeout);
        Assert.True(
            Constants.PublicWebSocketKeepAliveInterval + Constants.PublicWebSocketKeepAlivePongTimeout
                < Constants.PublicWebSocketLivenessTimeout,
            "The configured keep-alive budget must leave headroom below the liveness ceiling.");
    }

    /// <summary>
    /// Pins the configured native keep-alive budget to a ceiling below 60 seconds, so a future edit
    /// cannot silently drift <see cref="Constants.PublicWebSocketKeepAliveInterval"/> back up toward
    /// a value derived by subtracting the pong timeout from the full liveness deadline.
    /// </summary>
    [Fact]
    public void LivenessBudget_ConfiguredNativeBudget_DoesNotExceedFiftyFiveSeconds()
    {
        TimeSpan configuredBudget =
            Constants.PublicWebSocketKeepAliveInterval + Constants.PublicWebSocketKeepAlivePongTimeout;

        Assert.True(
            configuredBudget <= TimeSpan.FromSeconds(55),
            $"Expected the configured keep-alive budget to stay at or below 55 seconds, was {configuredBudget}.");
    }

    /// <summary>
    /// Pins the exact configured keep-alive split -- a 50-second idle-before-probe interval and a
    /// 5-second pong grace period -- so the budget tests above (which only bound the sum) cannot pass
    /// against a differently apportioned split that happens to add up the same way.
    /// </summary>
    [Fact]
    public void LivenessBudget_KeepAliveIntervalAndPongTimeout_MatchApprovedSplit()
    {
        Assert.Equal(TimeSpan.FromSeconds(50), Constants.PublicWebSocketKeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), Constants.PublicWebSocketKeepAlivePongTimeout);
    }
}
