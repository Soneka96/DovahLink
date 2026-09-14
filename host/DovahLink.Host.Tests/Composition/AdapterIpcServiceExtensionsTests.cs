using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Composition;
using DovahLink.Host.Identity;
using DovahLink.Host.Process;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Composition;

/// <summary>
/// Tests for <see cref="AdapterIpcServiceExtensions.ComposeAdapterIpcServices"/>. Adapter-IPC
/// protocol behavior (handshake acceptance/rejection, resynchronization, reconnect freshness) is
/// already fully proven against a manually assembled real stack by
/// <see cref="Adapter.Ipc.AdapterIpcChannelIntegrationTests"/>; that file uses a
/// <see cref="FakeAdapterTrustAdminRequestHandler"/>, so it never proves the composed graph's real,
/// <see cref="TrustServiceExtensions"/>-built request handler is actually wired in. These tests cover
/// only that composition-specific gap.
/// </summary>
[Collection(RealSocketAndProcessTestCollection.Name)]
public class AdapterIpcServiceExtensionsTests
{
    /// <summary>
    /// Verifies that a trust-admin request sent over the composed listener is answered by the real,
    /// <see cref="TrustServiceExtensions"/>-built <see cref="AdapterTrustAdminRequestHandler"/> --
    /// not a stray fake or an unwired default -- by seeding a distinctive known device through the
    /// real trust store and observing it in the wire reply text.
    /// </summary>
    [Fact]
    public async Task ComposeAdapterIpcServices_TrustAdminListRequest_AnsweredByRealComposedTrustAdminRequestHandler()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        var clientId = ClientId.NewId();
        var record = new TrustRecord(clientId, "54321", "Composition Test Device", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow)
        {
            Incarnation = KnownDeviceIncarnationId.NewId(),
        };
        var persistence = new FakeTrustStorePersistence();
        await persistence.SaveAsync([record]);
        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, persistence);
        var ownerLifetimeId = new OwnerLifetimeId(1, 2);

        AdapterIpcServices ipc = AdapterIpcServiceExtensions.ComposeAdapterIpcServices(core, trust, listenerPort: 0, ownerLifetimeId);
        using IAdapterIpcListener ownedListener = ipc.Listener;
        Task runTask = ownedListener.RunAsync(shutdown.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(ipc.Listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), ipc.Verifier.ExpectedToken, ownerLifetimeId: ownerLifetimeId.ToBytes())));
        Assert.True(Assert.IsType<IpcHelloAckMessage>(await ReadOneFrameAsync(adapterStream, codec)).Accepted);
        Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, codec));

        await adapterStream.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(7, TrustAdminOperation.List, ListScope: TrustAdminListScope.All)));
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(adapterStream, codec));

        Assert.Equal(7UL, result.CorrelationId);
        Assert.Contains("54321", result.ResultText);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that <see cref="AdapterIpcServiceExtensions.ComposeAdapterIpcServices"/>'s own
    /// <c>ownerLifetimeId</c> parameter is actually threaded into the composed session -- not
    /// silently dropped in favor of a default -- by presenting a correct peer-proof token alongside a
    /// mismatched owner-lifetime-id and observing the handshake rejected for that exact reason.
    /// </summary>
    [Fact]
    public async Task ComposeAdapterIpcServices_MismatchedOwnerLifetimeId_RejectsHandshakeWithLifetimeMismatch()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, new FakeTrustStorePersistence());

        AdapterIpcServices ipc = AdapterIpcServiceExtensions.ComposeAdapterIpcServices(core, trust, listenerPort: 0, new OwnerLifetimeId(1, 2));
        using IAdapterIpcListener ownedListener = ipc.Listener;
        Task runTask = ownedListener.RunAsync(shutdown.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(ipc.Listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(
            1, AdapterInstanceId.NewId(), ipc.Verifier.ExpectedToken, ownerLifetimeId: new OwnerLifetimeId(3, 4).ToBytes())));
        var ack = Assert.IsType<IpcHelloAckMessage>(await ReadOneFrameAsync(adapterStream, codec));

        Assert.False(ack.Accepted);
        Assert.Equal(IpcHelloRejectReason.LifetimeMismatch, ack.RejectReason);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that the composed <see cref="AdapterIpcServices.Notifier"/> actually reaches the same
    /// connection accepted through the composed <see cref="AdapterIpcServices.Listener"/> -- not an
    /// independently wired, disconnected notifier -- by requesting a pairing-code display and
    /// observing the corresponding wire frame on the connected adapter's own socket.
    /// </summary>
    [Fact]
    public async Task ComposeAdapterIpcServices_NotifierRequestsDisplay_ReachesConnectionAcceptedThroughSameListener()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, new FakeTrustStorePersistence());
        var ownerLifetimeId = new OwnerLifetimeId(1, 2);

        AdapterIpcServices ipc = AdapterIpcServiceExtensions.ComposeAdapterIpcServices(core, trust, listenerPort: 0, ownerLifetimeId);
        using IAdapterIpcListener ownedListener = ipc.Listener;
        Task runTask = ownedListener.RunAsync(shutdown.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(ipc.Listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), ipc.Verifier.ExpectedToken, ownerLifetimeId: ownerLifetimeId.ToBytes())));
        Assert.True(Assert.IsType<IpcHelloAckMessage>(await ReadOneFrameAsync(adapterStream, codec)).Accepted);
        Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, codec));

        Task<bool> notifyTask = ipc.Notifier.TryNotifyCodeAvailableAsync("123456", CancellationToken.None);
        var display = Assert.IsType<IpcPairingDisplayMessage>(await ReadOneFrameAsync(adapterStream, codec));
        Assert.Equal("123456", display.Code);
        await adapterStream.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(display.CorrelationId, Accepted: true)));

        Assert.True(await notifyTask.WaitAsync(TimeSpan.FromSeconds(5)));

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
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
