using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Composition;
using DovahLink.Host.Process;
using DovahLink.Host.Security;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Tests.Composition;

/// <summary>
/// R2.9 lifetime/isolation proofs for the public client boundary, against the real composed
/// <see cref="IPublicWebSocketListener"/> -- not a synthetic factory-only seam. See
/// <see cref="Client.Transport.PublicConnectionFactoryTests"/> for the analogous factory-level proof.
/// </summary>
[Collection(RealSocketAndProcessTestCollection.Name)]
public class PublicClientConnectionLifetimeTests
{
    /// <summary>
    /// Verifies that two simultaneously accepted public connections are distinct, connection-owned
    /// instances whose outbound state is isolated from each other -- not two references into shared
    /// Host-lifetime state -- by exhausting one connection's outbound capacity through a real accepted
    /// socket and observing the other, separately accepted connection's own capacity is untouched.
    /// </summary>
    [Fact]
    public async Task CurrentConnections_TwoAcceptedConnections_OutboundStateIsIsolated()
    {
        using var shutdown = new CancellationTokenSource();
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());
        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown, new FakeHostSettingsProvider { Settings = new HostSettings(2) });
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort: 0, ownerLifetimeId: default);
        services.AddPublicClientServices(publicListenerPort: 0);
        using ServiceProvider provider = services.BuildServiceProvider();
        IPublicWebSocketListener listener = provider.GetRequiredService<IPublicWebSocketListener>();
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using var firstSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await firstSocket.ConnectAsync(IPAddress.Loopback, listener.BoundPort);
        using var secondSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await secondSocket.ConnectAsync(IPAddress.Loopback, listener.BoundPort);

        IReadOnlyCollection<IPublicWebSocketConnection> connections = await WaitForConnectionCountAsync(listener, expected: 2);
        IPublicWebSocketConnection[] connectionArray = [.. connections];
        IPublicWebSocketConnection first = connectionArray[0];
        IPublicWebSocketConnection second = connectionArray[1];
        Assert.NotSame(first, second);

        int secondCapacityBefore = second.RemainingOutboundCapacity(PublicOutboundLane.Data);
        Assert.True(first.TrySend(new byte[] { 1, 2, 3 }, PublicOutboundLane.Data));

        Assert.True(first.RemainingOutboundCapacity(PublicOutboundLane.Data) < secondCapacityBefore);
        Assert.Equal(secondCapacityBefore, second.RemainingOutboundCapacity(PublicOutboundLane.Data));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Polls <see cref="IPublicWebSocketListener.CurrentConnections"/> until it reports <paramref name="expected"/> connections.</summary>
    /// <param name="listener">The listener to poll.</param>
    /// <param name="expected">The connection count to wait for.</param>
    private static async Task<IReadOnlyCollection<IPublicWebSocketConnection>> WaitForConnectionCountAsync(IPublicWebSocketListener listener, int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            IReadOnlyCollection<IPublicWebSocketConnection> connections = listener.CurrentConnections;
            if (connections.Count == expected)
            {
                return connections;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {expected} accepted connections.");
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
