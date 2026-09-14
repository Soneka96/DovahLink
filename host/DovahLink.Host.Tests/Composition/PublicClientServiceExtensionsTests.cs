using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Composition;
using DovahLink.Host.Identity;
using DovahLink.Host.Process;
using DovahLink.Host.Security;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Tests.Composition;

/// <summary>
/// Tests for <see cref="PublicClientServiceExtensions.AddPublicClientServices"/>. The generic
/// public-client protocol exchange (hello/hello_ack, admission, device-cap enforcement) is already
/// fully proven end to end by <see cref="ProgramCompositionTests"/>, which drives it through
/// <see cref="global::Program.ComposeAndRunAsync"/>; these tests cover only what is specific to this
/// module's own composition responsibility.
/// </summary>
[Collection(RealSocketAndProcessTestCollection.Name)]
public class PublicClientServiceExtensionsTests
{
    /// <summary>Verifies that omitting the public listener port leaves the listener unregistered rather than defaulting to some bound port.</summary>
    [Fact]
    public async Task AddPublicClientServices_NoPublicListenerPort_ListenerIsNull()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: null);

        Assert.Null(provider.GetService<IPublicWebSocketListener>());
    }

    /// <summary>
    /// Verifies that the composed listener's own connection cap comes from the same resolved
    /// <see cref="HostSettings"/> the session registry uses -- not an independent default -- by
    /// admitting exactly that many concurrent raw connections and rejecting the next.
    /// </summary>
    [Fact]
    public async Task AddPublicClientServices_UsesResolvedCapFromCoreServicesForListenerAdmission()
    {
        using var shutdown = new CancellationTokenSource();
        var hostSettingsProvider = new FakeHostSettingsProvider { Settings = new HostSettings(2) };
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0, hostSettingsProvider);
        IPublicWebSocketListener listener = provider.GetRequiredService<IPublicWebSocketListener>();
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using var firstClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await firstClient.ConnectAsync(IPAddress.Loopback, listener.BoundPort);
        using var secondClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await secondClient.ConnectAsync(IPAddress.Loopback, listener.BoundPort);
        using var thirdClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await thirdClient.ConnectAsync(IPAddress.Loopback, listener.BoundPort);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!IsDisconnected(thirdClient))
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the third connection to be rejected.");
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Whether a connected socket has since been closed by the remote peer.</summary>
    /// <param name="socket">The socket to probe.</param>
    private static bool IsDisconnected(Socket socket)
    {
        try
        {
            return socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
        }
        catch (SocketException)
        {
            return true;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    /// <summary>
    /// Verifies that a client's <c>pairing_request</c> reaches the exact <see cref="IPairingAdapterNotifier"/>
    /// singleton the composed adapter-IPC listener also uses -- not an independently constructed,
    /// disconnected notifier -- by driving a real accepted adapter connection alongside the public
    /// client exchange and observing the pairing-display frame arrive on that adapter connection's own
    /// socket.
    /// </summary>
    [Fact]
    public async Task AddPublicClientServices_PairingRequest_ReachesConnectionAcceptedThroughAdapterIpcListener()
    {
        using var shutdown = new CancellationTokenSource();
        var ownerLifetimeId = new OwnerLifetimeId(1, 2);
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0, ownerLifetimeId: ownerLifetimeId);

        IAdapterIpcListener adapterListener = provider.GetRequiredService<IAdapterIpcListener>();
        Task adapterRunTask = adapterListener.RunAsync(shutdown.Token);
        var ipcCodec = new IpcFrameCodec();
        using Socket adapterSocket = await ConnectClientAsync(adapterListener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        IAdapterPeerProofVerifier verifier = provider.GetRequiredService<IAdapterPeerProofVerifier>();
        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken, ownerLifetimeId: ownerLifetimeId.ToBytes())));
        Assert.True(Assert.IsType<IpcHelloAckMessage>(await ReadOneFrameAsync(adapterStream, ipcCodec)).Accepted);
        Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, ipcCodec));

        IPublicWebSocketListener listener = provider.GetRequiredService<IPublicWebSocketListener>();
        Task publicRunTask = listener.RunAsync(shutdown.Token);
        var codec = new PublicEnvelopeCodec();
        using var clientWebSocket = new ClientWebSocket();
        await clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{listener.BoundPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        var clientId = Guid.NewGuid().ToString();
        byte[] hello = codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, null, null,
            new HelloPayload { Endpoint = "client", ClientId = clientId, Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });
        await clientWebSocket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[4096];
        WebSocketReceiveResult helloAckResult = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, helloAckResult.Count), out PublicEnvelope? helloAckEnvelope));
        string sessionId = helloAckEnvelope!.SessionId!;

        WebSocketReceiveResult capabilitiesResult = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, capabilitiesResult.Count), out PublicEnvelope? capabilitiesEnvelope));
        Assert.Equal(PublicMessageType.Capabilities, capabilitiesEnvelope!.MessageType);

        byte[] pairingRequest = codec.Encode(PublicMessageType.PairingRequest, "pairing-1", sessionId, null, null, clientId, new EmptyPayload());
        await clientWebSocket.SendAsync(pairingRequest, WebSocketMessageType.Text, true, CancellationToken.None);

        var display = Assert.IsType<IpcPairingDisplayMessage>(await ReadOneFrameAsync(adapterStream, ipcCodec));
        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcPairingDisplayAckMessage(display.CorrelationId, Accepted: true)));
        await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        shutdown.Cancel();
        await adapterRunTask.WaitAsync(TimeSpan.FromSeconds(5));
        await publicRunTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Builds a real Core/Trust/AdapterIpc/PublicClient container -- the same registrations production composes it with.</summary>
    private static async Task<ServiceProvider> BuildProviderAsync(
        CancellationTokenSource shutdown,
        ITrustStorePersistence persistence,
        int? publicListenerPort,
        IHostSettingsProvider? hostSettingsProvider = null,
        OwnerLifetimeId ownerLifetimeId = default)
    {
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, persistence);

        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown, hostSettingsProvider);
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort: 0, ownerLifetimeId);
        services.AddPublicClientServices(publicListenerPort);

        return services.BuildServiceProvider();
    }

    /// <summary>Connects a plain client socket to the listener's bound loopback port, standing in for the adapter.</summary>
    private static async Task<Socket> ConnectClientAsync(int port)
    {
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(IPAddress.Loopback, port);
        return client;
    }

    /// <summary>Reads and decodes exactly one frame from the fake adapter's side of the connection.</summary>
    private static async Task<IpcMessage> ReadOneFrameAsync(Stream stream, IIpcFrameCodec codec)
    {
        byte[] lengthPrefix = new byte[sizeof(uint)];
        await ReadExactAsync(stream, lengthPrefix);
        Assert.True(codec.TryReadFrameLength(lengthPrefix, out int frameLength));
        byte[] frame = new byte[frameLength];
        await ReadExactAsync(stream, frame);
        IpcDecodeResult result = codec.Decode(frame);
        Assert.Null(result.FailureReason);
        return result.Message!;
    }

    /// <summary>Fills a buffer completely from a raw transport, tolerating partial reads.</summary>
    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead)).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(read > 0, "Unexpected end of stream while reading a test frame.");
            totalRead += read;
        }
    }
}
