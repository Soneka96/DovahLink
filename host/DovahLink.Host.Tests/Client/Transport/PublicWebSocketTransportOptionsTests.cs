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
        Assert.Equal(Constants.PublicWebSocketIdleTimeout, options.IdleTimeout);
        Assert.Equal(Constants.PublicWebSocketKeepAlivePongTimeout, options.KeepAlivePongTimeout);
        Assert.Equal(Constants.PublicWebSocketMaxMessageBytes, options.MaxMessageBytes);
        Assert.Equal(Constants.PublicWebSocketMaxMessagesPerSecond, options.MaxInboundMessagesPerSecond);
        Assert.Equal(Constants.PublicWebSocketMessageRateWindow, options.InboundMessageRateWindow);
        Assert.Equal(Constants.PublicWebSocketOutboundQueueMaxMessages, options.OutboundQueueMaxMessages);
        Assert.Equal(Constants.PublicWebSocketOutboundQueueMaxBytes, options.OutboundQueueMaxBytes);
        Assert.Equal(Constants.PublicWebSocketGracefulCloseTimeout, options.GracefulCloseTimeout);
        Assert.Equal(Constants.PublicWebSocketMaxHandshakeRequestBytes, options.MaxHandshakeRequestBytes);
    }

    /// <summary>Verifies that a single overridden property leaves every other default untouched.</summary>
    [Fact]
    public void Build_WithOneOverride_OverridesOnlyTheGivenParameter()
    {
        PublicWebSocketTransportOptions options = Fixtures.BuildPublicWebSocketTransportOptions(idleTimeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(TimeSpan.FromMilliseconds(50), options.IdleTimeout);
        Assert.Equal(Constants.PublicWebSocketHandshakeTimeout, options.HandshakeTimeout);
        Assert.Equal(Constants.PublicWebSocketKeepAlivePongTimeout, options.KeepAlivePongTimeout);
    }
}
