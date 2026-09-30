using System.Net;
using System.Net.Sockets;
using System.Text;
using DovahLink.Host.Identity;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Composition;
using DovahLink.Host.Security;
using DovahLink.Host.Sessions;
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

    /// <summary>Verifies the production connection factory serves a sessionless probe without changing an already-full session registry.</summary>
    [Fact]
    public async Task Create_HostProbeWhileSessionRegistryIsFull_LeavesActiveCountUnchanged()
    {
        using ServiceProvider provider = await BuildProviderAsync();
        IPublicConnectionFactory factory = provider.GetRequiredService<IPublicConnectionFactory>();
        ISessionRegistry sessionRegistry = provider.GetRequiredService<ISessionRegistry>();
        bool sessionCreated = sessionRegistry.TryCreate(
            ClientId.NewId(),
            ConnectionId.NewId(),
            SessionAuthenticationSource.TrustedDeviceCredential,
            SessionTrustTier.Full,
            out _);
        Assert.True(sessionCreated);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient server = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        IPublicWebSocketConnection connection = factory.Create(server.GetStream());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            $"GET {PublicWebSocketHandshake.HostProbePath} HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n\r\n"));

        using var response = new MemoryStream();
        byte[] buffer = new byte[512];
        while (true)
        {
            int read = await client.GetStream().ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (read == 0)
            {
                break;
            }

            response.Write(buffer, 0, read);
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", Encoding.UTF8.GetString(response.ToArray()));
        Assert.Equal(1, sessionRegistry.ActiveCount);
    }

    /// <summary>
    /// Resolves the real, container-registered <see cref="IPublicConnectionFactory"/> from a freshly
    /// composed Core/Trust/AdapterIpc/PublicClient graph -- the same registrations production
    /// composes it with, minus a bound public listener (unneeded for these tests).
    /// </summary>
    private static async Task<IPublicConnectionFactory> BuildFactoryAsync()
    {
        ServiceProvider provider = await BuildProviderAsync();
        return provider.GetRequiredService<IPublicConnectionFactory>();
    }

    /// <summary>Builds the production public-client dependency graph without binding its listener.</summary>
    private static async Task<ServiceProvider> BuildProviderAsync()
    {
        using var shutdown = new CancellationTokenSource();
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());

        var services = new ServiceCollection();
        services.AddSingleton(Fixtures.BuildHostIdentity());
        services.AddCoreServices(clock, securityGate, shutdown, new FakeHostSettingsProvider());
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort: 0, ownerLifetimeId: default);
        services.AddPublicClientServices(publicListenerPort: null);

        return services.BuildServiceProvider();
    }
}
