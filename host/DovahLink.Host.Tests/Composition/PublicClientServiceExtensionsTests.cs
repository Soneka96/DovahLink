using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Composition;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Composition;

/// <summary>
/// Tests for <see cref="PublicClientServiceExtensions.ComposePublicClientServices"/>. The generic
/// public-client protocol exchange (hello/hello_ack, admission, device-cap enforcement) is already
/// fully proven end to end by <see cref="ProgramCompositionTests"/>, which drives it through
/// <see cref="global::Program.ComposeAndRunAsync"/>; these tests cover only what is specific to this
/// module's own composition responsibility.
/// </summary>
[Collection(RealSocketAndProcessTestCollection.Name)]
public class PublicClientServiceExtensionsTests
{
    /// <summary>Verifies that omitting the public listener port leaves the listener uncomposed rather than defaulting to some bound port.</summary>
    [Fact]
    public async Task ComposePublicClientServices_NoPublicListenerPort_ListenerIsNull()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, new FakeTrustStorePersistence());

        PublicClientServices publicClient = PublicClientServiceExtensions.ComposePublicClientServices(core, trust, new FakePairingAdapterNotifier(), publicListenerPort: null);

        Assert.Null(publicClient.Listener);
    }

    /// <summary>
    /// Verifies that the composed listener's own connection cap comes from
    /// <see cref="CoreServices.Settings"/> -- the same resolved value <see cref="TrustServices.SessionRegistry"/>
    /// uses -- not an independent default, by admitting exactly that many concurrent raw connections
    /// and rejecting the next.
    /// </summary>
    [Fact]
    public async Task ComposePublicClientServices_UsesResolvedCapFromCoreServicesForListenerAdmission()
    {
        using var shutdown = new CancellationTokenSource();
        var hostSettingsProvider = new FakeHostSettingsProvider { Settings = new HostSettings(2) };
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown, hostSettingsProvider);
        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, new FakeTrustStorePersistence());

        PublicClientServices publicClient = PublicClientServiceExtensions.ComposePublicClientServices(core, trust, new FakePairingAdapterNotifier(), publicListenerPort: 0);
        using IPublicWebSocketListener listener = publicClient.Listener!;
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
    /// Verifies that a client's <c>pairing_request</c> is notified through the exact
    /// <see cref="IPairingAdapterNotifier"/> instance supplied to
    /// <see cref="PublicClientServiceExtensions.ComposePublicClientServices"/> -- not an
    /// independently constructed, unwired notifier.
    /// </summary>
    [Fact]
    public async Task ComposePublicClientServices_PairingRequest_NotifiesThroughSuppliedAdapterNotifier()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, new FakeTrustStorePersistence());
        var adapterNotifier = new FakePairingAdapterNotifier();

        PublicClientServices publicClient = PublicClientServiceExtensions.ComposePublicClientServices(core, trust, adapterNotifier, publicListenerPort: 0);
        using IPublicWebSocketListener listener = publicClient.Listener!;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

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
        await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(adapterNotifier.DisplayedCodes);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
