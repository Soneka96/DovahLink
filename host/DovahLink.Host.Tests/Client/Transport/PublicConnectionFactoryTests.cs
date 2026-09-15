using DovahLink.Host.Client.Transport;
using DovahLink.Host.Composition;
using DovahLink.Host.Security;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Tests.Client.Transport;

/// <summary>Tests for <see cref="PublicConnectionFactory"/>.</summary>
public class PublicConnectionFactoryTests
{
    /// <summary>Verifies that two calls to <see cref="PublicConnectionFactory.Create"/> return distinct connection instances.</summary>
    [Fact]
    public async Task Create_CalledTwice_ReturnsDistinctConnections()
    {
        IPublicConnectionFactory factory = await BuildFactoryAsync();

        IPublicWebSocketConnection first = factory.Create(new MemoryStream());
        IPublicWebSocketConnection second = factory.Create(new MemoryStream());

        Assert.NotSame(first, second);
    }

    /// <summary>
    /// Verifies that each connection's outbound state is its own -- not shared through the factory's
    /// Host-lifetime collaborators -- by exhausting one connection's outbound capacity and observing
    /// the other connection's own capacity is untouched.
    /// </summary>
    [Fact]
    public async Task Create_MutatingOneConnectionsOutboundState_LeavesTheOthersUnaffected()
    {
        IPublicConnectionFactory factory = await BuildFactoryAsync();
        IPublicWebSocketConnection first = factory.Create(new MemoryStream());
        IPublicWebSocketConnection second = factory.Create(new MemoryStream());
        int secondCapacityBefore = second.RemainingOutboundCapacity(PublicOutboundLane.Data);

        Assert.True(first.TrySend(new byte[] { 1, 2, 3 }, PublicOutboundLane.Data));

        Assert.True(first.RemainingOutboundCapacity(PublicOutboundLane.Data) < secondCapacityBefore);
        Assert.Equal(secondCapacityBefore, second.RemainingOutboundCapacity(PublicOutboundLane.Data));
    }

    /// <summary>
    /// Resolves the real, container-registered <see cref="IPublicConnectionFactory"/> from a freshly
    /// composed Core/Trust/AdapterIpc/PublicClient graph -- the same registrations production
    /// composes it with, minus a bound public listener (unneeded for these tests).
    /// </summary>
    private static async Task<IPublicConnectionFactory> BuildFactoryAsync()
    {
        using var shutdown = new CancellationTokenSource();
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());

        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown, new FakeHostSettingsProvider());
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort: 0, ownerLifetimeId: default);
        services.AddPublicClientServices(publicListenerPort: null);

        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IPublicConnectionFactory>();
    }
}
