using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Authentication;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Composition;
using DovahLink.Host.Identity;
using DovahLink.Host.Process;
using DovahLink.Host.Client.Readiness;
using DovahLink.Host.Security;
using DovahLink.Host.State;
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
    /// <summary>Verifies that every public-client service resolves to a non-null instance, not left unregistered.</summary>
    [Fact]
    public async Task AddPublicClientServices_ResolvesNonNullInstanceForEveryService()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);

        Assert.NotNull(provider.GetRequiredService<IRegisteredStateAreaPolicy>());
        Assert.NotNull(provider.GetRequiredService<LiveStateCatalog>());
        Assert.NotNull(provider.GetRequiredService<IStatePublicationFeed>());
        Assert.NotNull(provider.GetRequiredService<IAuthoritativeStateStore>());
        Assert.NotNull(provider.GetRequiredService<IPublicWebSocketTransportDiagnostics>());
        Assert.NotNull(provider.GetRequiredService<ILocalConnectionTokenAuthenticator>());
        Assert.NotNull(provider.GetRequiredService<ITrustedCredentialFailureThrottle>());
        Assert.NotNull(provider.GetRequiredService<IClientMessageDispatcher>());
        Assert.NotNull(provider.GetRequiredService<IPublicConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<PublicListenerOptions>());
        Assert.NotNull(provider.GetRequiredService<IPublicWebSocketListener>());
    }

    /// <summary>Verifies that every state area the default live-state catalog defines is registered at composition.</summary>
    [Fact]
    public async Task AddPublicClientServices_RegistersEveryDefaultCatalogStateArea()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);
        var registeredAreaPolicy = provider.GetRequiredService<IRegisteredStateAreaPolicy>();

        foreach (StateAreaDefinition area in LiveStateCatalog.Default.StateAreas)
        {
            Assert.True(registeredAreaPolicy.IsRegistered(area.Id));
        }

        Assert.Equal(LiveStateCatalog.Default.StateAreas.Count, registeredAreaPolicy.Count);
    }

    /// <summary>
    /// Verifies that the container composes exactly one authoritative store and that the capture
    /// application service and the publication feed both operate on it: a value applied through
    /// <see cref="ILiveStateApplication"/> is what the feed replays, so no second owner exists.
    /// </summary>
    [Fact]
    public async Task AddPublicClientServices_ApplicationAndFeed_ShareTheOneAuthoritativeStore()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);
        var store = provider.GetRequiredService<IAuthoritativeStateStore>();
        var tracker = provider.GetRequiredService<DovahLink.Host.Adapter.IAdapterAvailabilityTracker>();
        var playContextTracker = provider.GetRequiredService<DovahLink.Host.PlayContext.IPlayContextTracker>();
        var area = new StateAreaId(Constants.CharacterLevelStateArea);
        var instance = AdapterInstanceId.NewId();
        var context = PlayContextId.NewId();
        tracker.CommitConnected(instance, 1);
        playContextTracker.NotifyTransition(context);
        long contextGeneration = playContextTracker.GetSnapshot().TransitionGeneration;
        var token = tracker.TryClaimResynchronizationToken()!;
        Assert.True(store.ApplyResynchronizationBaseline(UpdateMode.Snapshot, token, context, contextGeneration, DateTimeOffset.UtcNow, area, 42).Accepted);
        tracker.NotifyResynchronized(instance, 1, token);

        provider.GetRequiredService<ILiveStateApplication>().Apply(
            UpdateMode.Snapshot, area, 43, isResynchronizationBaseline: false, new AdapterCaptureSource(instance, 1),
            tracker.GetSnapshot(), context, contextGeneration, DateTimeOffset.UtcNow);

        Assert.Same(store, provider.GetRequiredService<IAuthoritativeStateStore>());
        Assert.IsType<AuthoritativeStateStore>(store);
        Assert.IsType<StatePublicationFeed>(provider.GetRequiredService<IStatePublicationFeed>());
        Assert.True(provider.GetRequiredService<IStatePublicationFeed>().TryGetSnapshot(area, out StateSnapshotPublication? replayed));
        Assert.Equal(43, replayed.Data.GetProperty("value").GetInt32());
        Assert.True(store.TryGetCurrentValue(area, out int current));
        Assert.Equal(43, current);
    }

    /// <summary>
    /// Verifies that the real, container-registered <see cref="IPublicWebSocketTransportDiagnostics"/>
    /// placeholder never throws -- the documented contract every implementation of this interface
    /// must honor, since it is called on a connection's own read/write path during teardown.
    /// </summary>
    [Fact]
    public async Task AddPublicClientServices_ReportAbnormalEndOnRegisteredDiagnostics_DoesNotThrow()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);
        IPublicWebSocketTransportDiagnostics diagnostics = provider.GetRequiredService<IPublicWebSocketTransportDiagnostics>();

        Assert.Null(Record.Exception(() => diagnostics.ReportAbnormalEnd(PublicWebSocketConnectionEndReason.HandshakeTimeout)));
    }

    /// <summary>Verifies that omitting the public listener port leaves the listener unregistered rather than defaulting to some bound port.</summary>
    [Fact]
    public async Task AddPublicClientServices_NoPublicListenerPort_ListenerIsNull()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: null);

        Assert.Null(provider.GetService<IPublicWebSocketListener>());
    }

    /// <summary>
    /// Verifies that the composed listener's raw connection cap uses the resolved
    /// <see cref="HostSettings"/> session capacity plus its separate fixed pre-session allowance.
    /// </summary>
    [Fact]
    public async Task AddPublicClientServices_SeparatesResolvedSessionAndRawConnectionCapacities()
    {
        using var shutdown = new CancellationTokenSource();
        var hostSettingsProvider = new FakeHostSettingsProvider { Settings = new HostSettings(2) };
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0, hostSettingsProvider);
        IPublicWebSocketListener listener = provider.GetRequiredService<IPublicWebSocketListener>();
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        int rawCapacity = hostSettingsProvider.Settings.MaxActiveSessions + Constants.MaxPreSessionPublicConnections;
        List<Socket> admittedClients = [];
        using var overflowClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            for (int index = 0; index < rawCapacity; index++)
            {
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await client.ConnectAsync(IPAddress.Loopback, listener.BoundPort);
                admittedClients.Add(client);
            }

            await overflowClient.ConnectAsync(IPAddress.Loopback, listener.BoundPort);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!IsDisconnected(overflowClient))
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the next raw public connection to be rejected.");
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }
        }
        finally
        {
            foreach (Socket client in admittedClients)
            {
                client.Dispose();
            }

            cancellation.Cancel();
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
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
        // Pairing is refused outside gameplay, so this test completes a real resynchronization; that
        // needs the sampling scheduler and the tracked-quest coordinator the host runtime would run.
        Task schedulerRunTask = provider.GetRequiredService<ILiveStateScheduler>().RunAsync(shutdown.Token);
        Task questRunTask = provider.GetRequiredService<ITrackedQuestCaptureCoordinator>().RunAsync(shutdown.Token);
        var ipcCodec = new IpcFrameCodec();
        using Socket adapterSocket = await ConnectClientAsync(adapterListener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        IAdapterPeerProofVerifier verifier = provider.GetRequiredService<IAdapterPeerProofVerifier>();
        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken, ownerLifetimeId: ownerLifetimeId.ToBytes())));
        Assert.True(Assert.IsType<IpcHelloAckMessage>(await ReadOneFrameAsync(adapterStream, ipcCodec)).Accepted);
        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcPlayContextChangedMessage(
            0, new PlayContextId(new Guid("01020304-0506-0708-090a-0b0c0d0e0f10")))));
        var resynchronizeRequest = Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, ipcCodec));
        var adapterDriver = new FakeAdapterResynchronizationDriver(
            adapterStream, ipcCodec, new PlayContextId(new Guid("01020304-0506-0708-090a-0b0c0d0e0f10")));
        await adapterDriver.CompleteResynchronizationAsync(resynchronizeRequest);

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

        IpcPairingDisplayMessage display = await adapterDriver.ReadPairingDisplayAsync();
        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcPairingDisplayAckMessage(display.CorrelationId, Accepted: true)));
        await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        shutdown.Cancel();
        await adapterRunTask.WaitAsync(TimeSpan.FromSeconds(5));
        await publicRunTask.WaitAsync(TimeSpan.FromSeconds(5));
        await schedulerRunTask.WaitAsync(TimeSpan.FromSeconds(5));
        await questRunTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a restricted (unpaired) client of the composed Host receives, after
    /// <c>hello_ack</c> and <c>capabilities</c>, the initial coarse readiness: not ready, sequence 1,
    /// stamped with its own session and the Host's current state authority, and nothing else.
    /// </summary>
    [Fact]
    public async Task AddPublicClientServices_RestrictedAdmission_ReceivesInitialReadinessAfterHelloAckAndCapabilities()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);
        Task publicRunTask = provider.GetRequiredService<IPublicWebSocketListener>().RunAsync(shutdown.Token);
        await using ReadinessClient client = await ReadinessClient.ConnectAsync(provider, HelloAuthMethod.Unpaired);

        PublicEnvelope readiness = await client.ReceiveAsync(PublicMessageType.GameplayReadiness);

        Assert.Equal(client.SessionId, readiness.SessionId);
        Assert.Null(readiness.CorrelationId);
        Assert.Null(readiness.ClientId);
        Assert.Equal(provider.GetRequiredService<IStateAuthorityLifecycle>().Current.ToString(), readiness.StateAuthorityId);
        Assert.False(client.ReadinessPayload(readiness).Ready);
        Assert.Equal(1, client.ReadinessPayload(readiness).Sequence);
        Assert.Equal(PublicMessageType.Pong, (await client.PingAsync()).MessageType);

        shutdown.Cancel();
        await publicRunTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a full-tier (developer-authenticated) client of the composed Host receives the same initial readiness.</summary>
    [Fact]
    public async Task AddPublicClientServices_FullTierAdmission_ReceivesTheSameInitialReadiness()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);
        Task publicRunTask = provider.GetRequiredService<IPublicWebSocketListener>().RunAsync(shutdown.Token);
        await using ReadinessClient client = await ReadinessClient.ConnectAsync(provider, HelloAuthMethod.OneTimeLocalToken);

        PublicEnvelope readiness = await client.ReceiveAsync(PublicMessageType.GameplayReadiness);

        Assert.Equal(client.SessionId, readiness.SessionId);
        Assert.False(client.ReadinessPayload(readiness).Ready);
        Assert.Equal(1, client.ReadinessPayload(readiness).Sequence);

        shutdown.Cancel();
        await publicRunTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that real Adapter and play-context changes reach an already-admitted session as
    /// further readiness messages with a strictly increasing per-session sequence and no reconnect,
    /// and that a repeated equivalent state publishes nothing new.
    /// </summary>
    [Fact]
    public async Task AddPublicClientServices_ReadinessChanges_ArePublishedOnTheSameSessionInSequenceOrder()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);
        Task publicRunTask = provider.GetRequiredService<IPublicWebSocketListener>().RunAsync(shutdown.Token);
        await using ReadinessClient client = await ReadinessClient.ConnectAsync(provider, HelloAuthMethod.Unpaired);
        await client.ReceiveAsync(PublicMessageType.GameplayReadiness);

        MakeGameplayReady(provider);
        PublicEnvelope ready = await client.ReceiveAsync(PublicMessageType.GameplayReadiness);
        provider.GetRequiredService<DovahLink.Host.PlayContext.IPlayContextTracker>().ClearCurrent();
        PublicEnvelope notReady = await client.ReceiveAsync(PublicMessageType.GameplayReadiness);

        Assert.True(client.ReadinessPayload(ready).Ready);
        Assert.Equal(2, client.ReadinessPayload(ready).Sequence);
        Assert.NotNull(ready.PlayContextId);
        Assert.False(client.ReadinessPayload(notReady).Ready);
        Assert.Equal(3, client.ReadinessPayload(notReady).Sequence);
        Assert.Equal(client.SessionId, notReady.SessionId);
        Assert.Equal(PublicMessageType.Pong, (await client.PingAsync()).MessageType);

        shutdown.Cancel();
        await publicRunTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies each accepted connection gets its own publisher and sequence over the one shared evaluator.</summary>
    [Fact]
    public async Task AddPublicClientServices_TwoConnections_EachGetTheirOwnSequenceFromTheSharedEvaluator()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(
            shutdown,
            new FakeTrustStorePersistence(),
            publicListenerPort: 0,
            new FakeHostSettingsProvider { Settings = new HostSettings(2) });
        Task publicRunTask = provider.GetRequiredService<IPublicWebSocketListener>().RunAsync(shutdown.Token);
        await using ReadinessClient first = await ReadinessClient.ConnectAsync(provider, HelloAuthMethod.Unpaired);
        await first.ReceiveAsync(PublicMessageType.GameplayReadiness);
        MakeGameplayReady(provider);
        await first.ReceiveAsync(PublicMessageType.GameplayReadiness);

        await using ReadinessClient second = await ReadinessClient.ConnectAsync(provider, HelloAuthMethod.Unpaired);
        PublicEnvelope secondInitial = await second.ReceiveAsync(PublicMessageType.GameplayReadiness);
        provider.GetRequiredService<DovahLink.Host.PlayContext.IPlayContextTracker>().ClearCurrent();
        PublicEnvelope firstNext = await first.ReceiveAsync(PublicMessageType.GameplayReadiness);
        PublicEnvelope secondNext = await second.ReceiveAsync(PublicMessageType.GameplayReadiness);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.True(second.ReadinessPayload(secondInitial).Ready);
        Assert.Equal(1, second.ReadinessPayload(secondInitial).Sequence);
        Assert.Equal(first.SessionId, firstNext.SessionId);
        Assert.Equal(3, first.ReadinessPayload(firstNext).Sequence);
        Assert.Equal(second.SessionId, secondNext.SessionId);
        Assert.Equal(2, second.ReadinessPayload(secondNext).Sequence);

        shutdown.Cancel();
        await publicRunTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that over the composed socket a client-originated <c>gameplay_readiness</c> is rejected as malformed.</summary>
    [Fact]
    public async Task AddPublicClientServices_ClientSentReadiness_IsRejectedAsMalformed()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);
        Task publicRunTask = provider.GetRequiredService<IPublicWebSocketListener>().RunAsync(shutdown.Token);
        await using ReadinessClient client = await ReadinessClient.ConnectAsync(provider, HelloAuthMethod.Unpaired);
        await client.ReceiveAsync(PublicMessageType.GameplayReadiness);

        PublicEnvelope error = await client.SendReadinessAndReceiveAsync(provider.GetRequiredService<IStateAuthorityLifecycle>().Current);

        Assert.Equal(PublicMessageType.Error, error.MessageType);
        Assert.True(new PublicEnvelopeCodec().TryDecodePayload(error, out ErrorPayload? payload));
        Assert.Equal(PublicProtocolErrorCode.MalformedMessage, payload!.Code);

        shutdown.Cancel();
        await publicRunTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies a connection that fails authentication is told why and is never sent readiness.</summary>
    [Fact]
    public async Task AddPublicClientServices_RejectedHello_NeverReceivesReadiness()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, new FakeTrustStorePersistence(), publicListenerPort: 0);
        IPublicWebSocketListener listener = provider.GetRequiredService<IPublicWebSocketListener>();
        Task publicRunTask = listener.RunAsync(shutdown.Token);
        var codec = new PublicEnvelopeCodec();
        using var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{listener.BoundPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        byte[] hello = codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, null, null,
            new HelloPayload
            {
                Endpoint = "client",
                ClientId = Guid.NewGuid().ToString(),
                Auth = new HelloAuthPayload { Method = HelloAuthMethod.OneTimeLocalToken, Token = "not-a-valid-token" },
            });
        await webSocket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[4096];
        WebSocketReceiveResult first = await webSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(codec.TryDecode(buffer.AsMemory(0, first.Count), out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.Error, envelope!.MessageType);

        shutdown.Cancel();
        await publicRunTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Drives the composed Adapter and play-context trackers to the state the readiness evaluator reports as ready.</summary>
    private static void MakeGameplayReady(ServiceProvider provider)
    {
        var store = provider.GetRequiredService<IAuthoritativeStateStore>();
        var tracker = provider.GetRequiredService<DovahLink.Host.Adapter.IAdapterAvailabilityTracker>();
        var playContextTracker = provider.GetRequiredService<DovahLink.Host.PlayContext.IPlayContextTracker>();
        var area = new StateAreaId(Constants.CharacterLevelStateArea);
        var instance = AdapterInstanceId.NewId();
        var context = PlayContextId.NewId();
        tracker.CommitConnected(instance, 1);
        playContextTracker.NotifyTransition(context);
        long contextGeneration = playContextTracker.GetSnapshot().TransitionGeneration;
        var token = tracker.TryClaimResynchronizationToken()!;
        Assert.True(store.ApplyResynchronizationBaseline(UpdateMode.Snapshot, token, context, contextGeneration, DateTimeOffset.UtcNow, area, 42).Accepted);
        tracker.NotifyResynchronized(instance, 1, token);
    }

    /// <summary>A real WebSocket client of the composed Host that has completed <c>hello</c> and read the first two frames.</summary>
    private sealed class ReadinessClient : IAsyncDisposable
    {
        private readonly PublicEnvelopeCodec codec = new();

        private readonly ClientWebSocket webSocket = new();

        private readonly byte[] buffer = new byte[4096];

        private string clientId = string.Empty;

        /// <summary>The admitted session identity the Host reported.</summary>
        public string SessionId { get; private set; } = string.Empty;

        /// <summary>Connects to the composed listener, authenticates with <paramref name="method"/>, and consumes <c>hello_ack</c> and <c>capabilities</c> in order.</summary>
        public static async Task<ReadinessClient> ConnectAsync(ServiceProvider provider, HelloAuthMethod method)
        {
            IPublicWebSocketListener listener = provider.GetRequiredService<IPublicWebSocketListener>();
            var client = new ReadinessClient { clientId = Guid.NewGuid().ToString() };
            await client.webSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{listener.BoundPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            string? token = method == HelloAuthMethod.OneTimeLocalToken
                ? provider.GetRequiredService<ILocalConnectionTokenAuthenticator>().IssueToken()
                : null;
            byte[] hello = client.codec.Encode(
                PublicMessageType.Hello, "hello-1", null, null, null, null,
                new HelloPayload { Endpoint = "client", ClientId = client.clientId, Auth = new HelloAuthPayload { Method = method, Token = token } });
            await client.webSocket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

            PublicEnvelope helloAck = await client.ReceiveAsync(PublicMessageType.HelloAck);
            client.SessionId = helloAck.SessionId!;
            await client.ReceiveAsync(PublicMessageType.Capabilities);
            return client;
        }

        /// <summary>Receives the next frame and asserts it is a <paramref name="expected"/> message.</summary>
        public async Task<PublicEnvelope> ReceiveAsync(PublicMessageType expected)
        {
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? envelope));
            Assert.Equal(expected, envelope!.MessageType);
            return envelope;
        }

        /// <summary>Decodes the readiness payload of <paramref name="envelope"/>.</summary>
        public GameplayReadinessPayload ReadinessPayload(PublicEnvelope envelope)
        {
            Assert.True(codec.TryDecodePayload(envelope, out GameplayReadinessPayload? payload));
            return payload!;
        }

        /// <summary>Sends a client-originated <c>gameplay_readiness</c> message and returns the next frame the Host sends.</summary>
        /// <param name="authority">The state authority the forged message claims.</param>
        public async Task<PublicEnvelope> SendReadinessAndReceiveAsync(StateAuthorityId authority)
        {
            byte[] message = codec.EncodeStatePublication(
                PublicMessageType.GameplayReadiness, Guid.NewGuid().ToString(), SessionId, null, null, null, authority,
                new GameplayReadinessPayload { Ready = true, Sequence = 1 });
            await webSocket.SendAsync(message, WebSocketMessageType.Text, true, CancellationToken.None);
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? envelope));
            return envelope!;
        }

        /// <summary>Sends a ping and returns the next frame, proving no other message is queued ahead of the reply.</summary>
        public async Task<PublicEnvelope> PingAsync()
        {
            byte[] ping = codec.Encode(PublicMessageType.Ping, Guid.NewGuid().ToString(), SessionId, null, null, clientId, new EmptyPayload());
            await webSocket.SendAsync(ping, WebSocketMessageType.Text, true, CancellationToken.None);
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? envelope));
            return envelope!;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            webSocket.Dispose();
            return ValueTask.CompletedTask;
        }
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
        services.AddSingleton(Fixtures.BuildHostIdentity());
        services.AddCoreServices(clock, securityGate, shutdown, hostSettingsProvider ?? new FakeHostSettingsProvider());
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort: 0, ownerLifetimeId);
        services.AddPublicClientServices(publicListenerPort);

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
}
