using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Composition;
using DovahLink.Host.Identity;
using DovahLink.Host.Process;
using DovahLink.Host.State;
using DovahLink.Host.Security;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Tests.Composition;

/// <summary>
/// Tests for <see cref="AdapterIpcServiceExtensions.AddAdapterIpcServices"/>. Adapter-IPC protocol
/// behavior (handshake acceptance/rejection, resynchronization, reconnect freshness) is already fully
/// proven against a manually assembled real stack by
/// <see cref="Adapter.Ipc.AdapterIpcChannelIntegrationTests"/>; that file uses a
/// <see cref="FakeAdapterTrustAdminRequestHandler"/>, so it never proves the composed graph's real,
/// <see cref="TrustServiceExtensions"/>-built request handler is actually wired in. These tests cover
/// only that composition-specific gap.
/// </summary>
[Collection(RealSocketAndProcessTestCollection.Name)]
public class AdapterIpcServiceExtensionsTests
{
    /// <summary>Verifies that every adapter-IPC service resolves to a non-null instance, not left unregistered.</summary>
    [Fact]
    public async Task AddAdapterIpcServices_ResolvesNonNullInstanceForEveryService()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), listenerPort: 0, new OwnerLifetimeId(1, 2));

        Assert.NotNull(provider.GetRequiredService<HostInstanceOptions>());
        Assert.NotNull(provider.GetRequiredService<AdapterIpcOptions>());
        Assert.NotNull(provider.GetRequiredService<IAdapterConnectionLifecycle>());
        Assert.NotNull(provider.GetRequiredService<IAdapterPeerProofVerifier>());
        Assert.NotNull(provider.GetRequiredService<IIpcFrameCodec>());
        Assert.NotNull(provider.GetRequiredService<IAdapterContinuityRecovery>());
        ResynchronizationPlan plan = provider.GetRequiredService<ResynchronizationPlan>();
        ResynchronizationPlan expectedPlan = LiveStateCatalog.Default.BuildResynchronizationPlan();
        Assert.Equal(expectedPlan.PersistentEventKeys, plan.PersistentEventKeys);
        Assert.Equal(expectedPlan.BaselineSampleTokens, plan.BaselineSampleTokens);
        Assert.Same(plan, provider.GetRequiredService<ResynchronizationPlan>());
        Assert.NotNull(provider.GetRequiredService<IAdapterConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<IAdapterIpcListener>());
        Assert.NotNull(provider.GetRequiredService<IPairingAdapterNotifier>());
        Assert.IsType<LiveStateApplication>(provider.GetRequiredService<ILiveStateApplication>());
        ILiveCaptureHandler captureHandler = Assert.Single(provider.GetServices<ILiveCaptureHandler>());
        Assert.IsType<CharacterCaptureHandler>(captureHandler);
        Assert.Same(captureHandler, provider.GetRequiredService<CharacterCaptureHandler>());
        Assert.NotNull(provider.GetRequiredService<ILiveCaptureSink>());
        Assert.NotNull(provider.GetRequiredService<LiveStateScheduler>());
    }

    /// <summary>
    /// Verifies that a trust-admin request sent over the composed listener is answered by the real,
    /// <see cref="TrustServiceExtensions"/>-built <see cref="AdapterTrustAdminRequestHandler"/> --
    /// not a stray fake or an unwired default -- by seeding a distinctive known device through the
    /// real trust store and observing it in the wire reply text.
    /// </summary>
    [Fact]
    public async Task AddAdapterIpcServices_TrustAdminListRequest_AnsweredByRealComposedTrustAdminRequestHandler()
    {
        var clientId = ClientId.NewId();
        var record = new TrustRecord(clientId, "54321", "Composition Test Device", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow)
        {
            Incarnation = KnownDeviceIncarnationId.NewId(),
        };
        var persistence = new FakeTrustStorePersistence();
        await persistence.SaveAsync([record]);
        var ownerLifetimeId = new OwnerLifetimeId(1, 2);

        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, persistence, listenerPort: 0, ownerLifetimeId);
        IAdapterIpcListener listener = provider.GetRequiredService<IAdapterIpcListener>();
        Task runTask = listener.RunAsync(shutdown.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        IAdapterPeerProofVerifier verifier = provider.GetRequiredService<IAdapterPeerProofVerifier>();
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken, ownerLifetimeId: ownerLifetimeId.ToBytes())));
        Assert.True(Assert.IsType<IpcHelloAckMessage>(await ReadOneFrameAsync(adapterStream, codec)).Accepted);
        await adapterStream.WriteAsync(codec.Encode(new IpcPlayContextChangedMessage(
            0, new PlayContextId(new Guid("01020304-0506-0708-090a-0b0c0d0e0f10")))));
        var resynchronizeRequest = Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, codec));
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(resynchronizeRequest.CorrelationId, Accepted: true)));

        await adapterStream.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(7, TrustAdminOperation.List, ListScope: TrustAdminListScope.All)));
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(adapterStream, codec));

        Assert.Equal(7UL, result.CorrelationId);
        Assert.Contains("54321", result.ResultText);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that <see cref="AdapterIpcServiceExtensions.AddAdapterIpcServices"/>'s own
    /// <c>ownerLifetimeId</c> parameter is actually threaded into the composed session -- not
    /// silently dropped in favor of a default -- by presenting a correct peer-proof token alongside a
    /// mismatched owner-lifetime-id and observing the handshake rejected for that exact reason.
    /// </summary>
    [Fact]
    public async Task AddAdapterIpcServices_MismatchedOwnerLifetimeId_RejectsHandshakeWithLifetimeMismatch()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), listenerPort: 0, new OwnerLifetimeId(1, 2));
        IAdapterIpcListener listener = provider.GetRequiredService<IAdapterIpcListener>();
        Task runTask = listener.RunAsync(shutdown.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        IAdapterPeerProofVerifier verifier = provider.GetRequiredService<IAdapterPeerProofVerifier>();
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(
            1, AdapterInstanceId.NewId(), verifier.ExpectedToken, ownerLifetimeId: new OwnerLifetimeId(3, 4).ToBytes())));
        var ack = Assert.IsType<IpcHelloAckMessage>(await ReadOneFrameAsync(adapterStream, codec));

        Assert.False(ack.Accepted);
        Assert.Equal(IpcHelloRejectReason.LifetimeMismatch, ack.RejectReason);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that the composed <see cref="IPairingAdapterNotifier"/> actually reaches the same
    /// connection accepted through the composed <see cref="IAdapterIpcListener"/> -- not an
    /// independently wired, disconnected notifier -- by requesting a pairing-code display and
    /// observing the corresponding wire frame on the connected adapter's own socket.
    /// </summary>
    [Fact]
    public async Task AddAdapterIpcServices_NotifierRequestsDisplay_ReachesConnectionAcceptedThroughSameListener()
    {
        var ownerLifetimeId = new OwnerLifetimeId(1, 2);
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), listenerPort: 0, ownerLifetimeId);
        IAdapterIpcListener listener = provider.GetRequiredService<IAdapterIpcListener>();
        Task runTask = listener.RunAsync(shutdown.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        IAdapterPeerProofVerifier verifier = provider.GetRequiredService<IAdapterPeerProofVerifier>();
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken, ownerLifetimeId: ownerLifetimeId.ToBytes())));
        Assert.True(Assert.IsType<IpcHelloAckMessage>(await ReadOneFrameAsync(adapterStream, codec)).Accepted);
        await adapterStream.WriteAsync(codec.Encode(new IpcPlayContextChangedMessage(
            0, new PlayContextId(new Guid("01020304-0506-0708-090a-0b0c0d0e0f10")))));
        var resynchronizeRequest = Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, codec));
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(resynchronizeRequest.CorrelationId, Accepted: true)));

        Task<bool> notifyTask = provider.GetRequiredService<IPairingAdapterNotifier>().TryNotifyCodeAvailableAsync("123456", CancellationToken.None);
        var display = Assert.IsType<IpcPairingDisplayMessage>(await ReadOneFrameAsync(adapterStream, codec));
        Assert.Equal("123456", display.Code);
        await adapterStream.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(display.CorrelationId, Accepted: true)));

        Assert.True(await notifyTask.WaitAsync(TimeSpan.FromSeconds(5)));

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Builds a real Core/Trust/AdapterIpc/PublicClient container -- the same registrations
    /// production composes it with. PublicClient is included with no bound listener port because
    /// <see cref="ILiveCaptureSink"/>'s real composed implementation resolves
    /// <see cref="DovahLink.Host.State.LiveStateCatalog"/> and
    /// <see cref="DovahLink.Host.State.IStatePublicationSink"/> from that graph; these tests exercise
    /// only the AdapterIpc-specific services listed above.
    /// </summary>
    private static async Task<ServiceProvider> BuildProviderAsync(
        CancellationTokenSource shutdown, ITrustStorePersistence persistence, int listenerPort, OwnerLifetimeId ownerLifetimeId)
    {
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, persistence);

        var services = new ServiceCollection();
        services.AddSingleton(Fixtures.BuildHostIdentity());
        services.AddCoreServices(clock, securityGate, shutdown, new FakeHostSettingsProvider());
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort, ownerLifetimeId);
        services.AddPublicClientServices(publicListenerPort: null);

        ServiceProvider provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IPlayContextResynchronizationTrigger>();
        return provider;
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
