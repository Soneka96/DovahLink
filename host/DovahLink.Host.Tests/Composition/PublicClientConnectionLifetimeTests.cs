using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using DovahLink.Host.Client.Protocol;
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

    /// <summary>
    /// Verifies that a reconnect under the same persistent <c>clientId</c> gets a fresh connection and
    /// a fresh session -- not one that inherited the ended connection's replay/admission state -- by
    /// sending the identical <c>messageId</c> ("hello-1") on both connections. If the first
    /// connection's <see cref="Client.Authentication.PublicHelloAdmissionHandler"/> or its replay-id
    /// set had leaked into the second connection, the second <c>hello</c> would be incorrectly
    /// rejected as replayed instead of admitted with a fresh <c>sessionId</c>. This single test
    /// therefore proves several R2.9 bullets at once: reconnect has fresh connection state, fresh
    /// replay state, and a new session; and persistent <c>clientId</c> does not retain
    /// connection-scoped state.
    /// </summary>
    [Fact]
    public async Task Hello_ReconnectWithSameClientIdAndMessageId_GetsFreshSessionNotRejectedAsReplay()
    {
        using var shutdown = new CancellationTokenSource();
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());
        var services = new ServiceCollection();
        // Constants.MaxActiveSessions (the production shipped default) is 1; this reconnect test needs
        // headroom for a moment where the first connection's admission slot may not have been released
        // yet when the second connects, which is incidental to what this test actually proves.
        services.AddCoreServices(clock, securityGate, shutdown, new FakeHostSettingsProvider { Settings = new HostSettings(2) });
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort: 0, ownerLifetimeId: default);
        services.AddPublicClientServices(publicListenerPort: 0);
        using ServiceProvider provider = services.BuildServiceProvider();
        IPublicWebSocketListener listener = provider.GetRequiredService<IPublicWebSocketListener>();
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new PublicEnvelopeCodec();
        var clientId = Guid.NewGuid().ToString();

        string sessionIdA = await ConnectAndHelloAsync(listener.BoundPort, codec, clientId);
        string sessionIdB = await ConnectAndHelloAsync(listener.BoundPort, codec, clientId);

        Assert.NotEqual(sessionIdA, sessionIdB);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Connects a fresh public client socket, sends a <c>hello</c> with the fixed <c>messageId</c>
    /// <c>"hello-1"</c> for <paramref name="clientId"/>, asserts it is admitted (not rejected as
    /// replayed), and returns the admitted <c>sessionId</c>. Disposes the socket before returning, so
    /// each call is a genuinely separate connection.
    /// </summary>
    /// <param name="port">The listener's bound loopback port.</param>
    /// <param name="codec">The envelope codec to encode the <c>hello</c> and decode its <c>hello_ack</c>.</param>
    /// <param name="clientId">The persistent client identity to hello as.</param>
    private static async Task<string> ConnectAndHelloAsync(int port, IPublicEnvelopeCodec codec, string clientId)
    {
        using var clientWebSocket = new ClientWebSocket();
        await clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        byte[] hello = codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, null, null,
            new HelloPayload { Endpoint = "client", ClientId = clientId, Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });
        await clientWebSocket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[4096];
        WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.HelloAck, envelope!.MessageType);

        return envelope.SessionId!;
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
