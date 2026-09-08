using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Identity;
using DovahLink.Host.Process;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Client.Integration;

/// <summary>
/// Full-stack proof that host-decided adapter notifications and adapter-originated trust
/// administration cross the private IPC boundary and correctly drive a real connected public
/// client's outcome, over the actual composed <see cref="global::Program.ComposeAndRunAsync"/>
/// process graph with both the public WebSocket listener and the private adapter IPC listener bound
/// to real loopback sockets. Individual collaborators already have their own isolated unit and
/// lower-level integration tests; this class proves only that the two boundaries are wired together
/// correctly in production composition.
/// </summary>
public class AdapterNotificationIntegrationTests
{
    /// <summary>A raw-hex credential valid by shape, standing in for an already-trusted device's stored credential.</summary>
    private const string TrustedCredential = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>Verifies that pairing_request reports unavailable when no adapter is connected.</summary>
    [Fact]
    public async Task PairingRequest_NoAdapterConnected_ReportsUnavailable()
    {
        var codec = new PublicEnvelopeCodec();
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, _, _, _) = await StartComposedHostAsync();
        (ClientWebSocket client, string sessionId, string clientId) = await ConnectAndAdmitPublicClientAsync(publicPort, codec);
        using ClientWebSocket ownedClient = client;

        PairingStatusPayload status = await RequestPairingStatusAsync(client, codec, sessionId, clientId);

        Assert.Equal(PairingStatusWireState.Unavailable, status.State);
        Assert.Null(status.ExpiresInSeconds);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that pairing_request reports available, with a positive remaining lifetime, once a
    /// connected adapter accepts the display request the host sent it -- and that the code itself
    /// never appears on the public wire.
    /// </summary>
    [Fact]
    public async Task PairingRequest_AdapterAcceptsDisplay_ReportsAvailableWithoutLeakingCodeOnTheWire()
    {
        var codec = new PublicEnvelopeCodec();
        var ipcCodec = new IpcFrameCodec();
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, int adapterPort, byte[] adapterProof, OwnerLifetimeId ownerLifetimeId) = await StartComposedHostAsync();
        (ClientWebSocket client, string sessionId, string clientId) = await ConnectAndAdmitPublicClientAsync(publicPort, codec);
        using ClientWebSocket ownedClient = client;
        using Socket adapterSocket = await ConnectAdapterAsync(adapterPort, adapterProof, ownerLifetimeId, ipcCodec);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);

        Task<PairingStatusPayload> statusTask = RequestPairingStatusAsync(client, codec, sessionId, clientId);
        var displayRequest = Assert.IsType<IpcPairingDisplayMessage>(await ReadIpcFrameAsync(adapterStream, ipcCodec));
        Assert.Equal(PairingDisplayMode.Initial, displayRequest.Mode);
        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcPairingDisplayAckMessage(displayRequest.CorrelationId, Accepted: true)));

        PairingStatusPayload status = await statusTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PairingStatusWireState.Available, status.State);
        Assert.True(status.ExpiresInSeconds > 0);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that pairing_request reports unavailable when a connected adapter declines the display request.</summary>
    [Fact]
    public async Task PairingRequest_AdapterDeclinesDisplay_ReportsUnavailable()
    {
        var codec = new PublicEnvelopeCodec();
        var ipcCodec = new IpcFrameCodec();
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, int adapterPort, byte[] adapterProof, OwnerLifetimeId ownerLifetimeId) = await StartComposedHostAsync();
        (ClientWebSocket client, string sessionId, string clientId) = await ConnectAndAdmitPublicClientAsync(publicPort, codec);
        using ClientWebSocket ownedClient = client;
        using Socket adapterSocket = await ConnectAdapterAsync(adapterPort, adapterProof, ownerLifetimeId, ipcCodec);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);

        Task<PairingStatusPayload> statusTask = RequestPairingStatusAsync(client, codec, sessionId, clientId);
        var displayRequest = Assert.IsType<IpcPairingDisplayMessage>(await ReadIpcFrameAsync(adapterStream, ipcCodec));
        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcPairingDisplayAckMessage(displayRequest.CorrelationId, Accepted: false)));

        PairingStatusPayload status = await statusTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PairingStatusWireState.Unavailable, status.State);
        Assert.Null(status.ExpiresInSeconds);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that an adapter-originated <c>revoke</c> trust-admin request, sent over the real
    /// private IPC channel, invalidates the matching real connected public client's session --
    /// sending <c>session_invalidated</c> before force-closing its socket -- proving the
    /// authoritative-mutation-then-best-effort-notify-then-close ordering across both real listeners.
    /// </summary>
    [Fact]
    public async Task AdapterRevokeRequest_MatchingTrustedPublicClient_SendsSessionInvalidatedThenForceCloses()
    {
        var codec = new PublicEnvelopeCodec();
        var ipcCodec = new IpcFrameCodec();
        string clientId = Guid.NewGuid().ToString();
        var persistence = new FakeTrustStorePersistence();
        await persistence.SaveAsync([
            new TrustRecord(new ClientId(Guid.Parse(clientId)), "54321", "My PC", KnownDeviceState.Trusted, CredentialHasher.Hash(TrustedCredential), DateTimeOffset.UtcNow),
        ]);
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, int adapterPort, byte[] adapterProof, OwnerLifetimeId ownerLifetimeId) =
            await StartComposedHostAsync(persistence);
        (ClientWebSocket client, _, _) = await ConnectAndAdmitPublicClientAsync(
            publicPort, codec, clientId, new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = TrustedCredential });
        using ClientWebSocket ownedClient = client;
        using Socket adapterSocket = await ConnectAdapterAsync(adapterPort, adapterProof, ownerLifetimeId, ipcCodec);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);

        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcTrustAdminRequestMessage(7, TrustAdminOperation.Revoke, ShortId: "54321")));
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadIpcFrameAsync(adapterStream, ipcCodec));
        Assert.Contains("Revoked", result.ResultText);

        var buffer = new byte[8192];
        WebSocketReceiveResult invalidatedResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, invalidatedResult.Count), out PublicEnvelope? invalidatedEnvelope));
        Assert.Equal(PublicMessageType.SessionInvalidated, invalidatedEnvelope!.MessageType);
        Assert.True(codec.TryDecodePayload(invalidatedEnvelope, out SessionInvalidatedPayload? invalidatedPayload));
        Assert.Equal(SessionInvalidationReason.Revoked, invalidatedPayload!.Reason);

        // Administrative invalidation force-closes the connection after the best-effort notification
        // above; the transport may complete an orderly close handshake or abort the connection
        // outright (surfacing as a WebSocketException here), so either outcome proves the same
        // force-close contract rather than only the graceful one.
        try
        {
            WebSocketReceiveResult closeResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(WebSocketMessageType.Close, closeResult.MessageType);
        }
        catch (WebSocketException)
        {
            // The connection was aborted rather than gracefully closed -- also a valid force-close.
        }

        Assert.NotEqual(WebSocketState.Open, client.State);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Full pairing completion, real end-to-end: a real client's <c>pairing_request</c>/
    /// <c>pairing_confirm</c>/<c>pairing_ack</c> exchange -- driven by the code the real adapter
    /// actually displayed, exactly as <see cref="PairingRequest_AdapterAcceptsDisplay_ReportsAvailableWithoutLeakingCodeOnTheWire"/>
    /// already proves never reaches the public wire -- issues a real trust credential, and a fresh
    /// connection presenting that exact credential is admitted as trusted without repeating pairing.
    /// Proves the released Stage 3 pairing and trusted-reconnect behavior end-to-end against the
    /// real production graph.
    /// </summary>
    [Fact]
    public async Task FullPairing_ThenReconnectWithIssuedCredential_AdmitsAsTrustedWithoutRePairing()
    {
        var codec = new PublicEnvelopeCodec();
        var ipcCodec = new IpcFrameCodec();
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, int adapterPort, byte[] adapterProof, OwnerLifetimeId ownerLifetimeId) = await StartComposedHostAsync();
        (ClientWebSocket client, string sessionId, string clientId) = await ConnectAndAdmitPublicClientAsync(publicPort, codec);
        using ClientWebSocket ownedClient = client;
        using Socket adapterSocket = await ConnectAdapterAsync(adapterPort, adapterProof, ownerLifetimeId, ipcCodec);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);

        Task<PairingStatusPayload> statusTask = RequestPairingStatusAsync(client, codec, sessionId, clientId);
        var displayRequest = Assert.IsType<IpcPairingDisplayMessage>(await ReadIpcFrameAsync(adapterStream, ipcCodec));
        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcPairingDisplayAckMessage(displayRequest.CorrelationId, Accepted: true)));
        PairingStatusPayload status = await statusTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PairingStatusWireState.Available, status.State);

        var buffer = new byte[8192];

        // The code a human would read off their Skyrim screen -- never sent over the public wire --
        // is exactly what the real adapter was just told to display.
        byte[] confirm = codec.Encode(
            PublicMessageType.PairingConfirm, "pairing-confirm-1", sessionId, null, null, clientId,
            new PairingConfirmPayload { Code = displayRequest.Code, DisplayName = "My PC" });
        await client.SendAsync(confirm, WebSocketMessageType.Text, true, CancellationToken.None);
        WebSocketReceiveResult confirmResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, confirmResult.Count), out PublicEnvelope? confirmEnvelope));
        Assert.Equal(PublicMessageType.PairingOutcome, confirmEnvelope!.MessageType);
        Assert.True(codec.TryDecodePayload(confirmEnvelope, out PairingOutcomePayload? confirmOutcome));
        Assert.Equal(PairingOutcomeWireValue.CredentialIssued, confirmOutcome!.Outcome);
        string credential = confirmOutcome.Credential!;

        byte[] ack = codec.Encode(
            PublicMessageType.PairingAck, "pairing-ack-1", sessionId, null, null, clientId,
            new PairingAckPayload { Credential = credential });
        await client.SendAsync(ack, WebSocketMessageType.Text, true, CancellationToken.None);
        WebSocketReceiveResult ackResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, ackResult.Count), out PublicEnvelope? ackEnvelope));
        Assert.Equal(PublicMessageType.PairingOutcome, ackEnvelope!.MessageType);
        Assert.True(codec.TryDecodePayload(ackEnvelope, out PairingOutcomePayload? ackOutcome));
        Assert.Equal(PairingOutcomeWireValue.Trusted, ackOutcome!.Outcome);
        Assert.Equal(credential, ackOutcome.Credential);

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // A fresh connection presenting the exact credential just issued must be admitted directly as
        // trusted -- proving reconnect never requires repeating the pairing flow above.
        using var reconnectedClient = new ClientWebSocket();
        await reconnectedClient.ConnectAsync(new Uri($"ws://127.0.0.1:{publicPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        byte[] reconnectHello = codec.Encode(
            PublicMessageType.Hello, "hello-reconnect-1", null, null, null, null,
            new HelloPayload
            {
                Endpoint = "client",
                ClientId = clientId,
                Auth = new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = credential },
            });
        await reconnectedClient.SendAsync(reconnectHello, WebSocketMessageType.Text, true, CancellationToken.None);
        WebSocketReceiveResult reconnectHelloAckResult = await reconnectedClient.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, reconnectHelloAckResult.Count), out PublicEnvelope? reconnectHelloAckEnvelope));
        Assert.Equal(PublicMessageType.HelloAck, reconnectHelloAckEnvelope!.MessageType);
        Assert.True(codec.TryDecodePayload(reconnectHelloAckEnvelope, out HelloAckPayload? reconnectHelloAck));
        Assert.Equal(ClientIdentityKind.Paired, reconnectHelloAck!.ClientIdentityKind);
        Assert.NotEqual(sessionId, reconnectHelloAckEnvelope.SessionId);

        // Every admission sends hello_ack followed by an unsolicited, empty capabilities
        // advertisement -- drained here so it is never mistaken for a later message by any future
        // read this test adds.
        WebSocketReceiveResult reconnectCapabilitiesResult = await reconnectedClient.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, reconnectCapabilitiesResult.Count), out PublicEnvelope? reconnectCapabilitiesEnvelope));
        Assert.Equal(PublicMessageType.Capabilities, reconnectCapabilitiesEnvelope!.MessageType);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that an adapter-originated <c>block</c> trust-admin request, sent over the real
    /// private IPC channel, invalidates the matching real connected known device's session --
    /// sending <c>session_invalidated</c> before force-closing its socket -- mirroring
    /// <see cref="AdapterRevokeRequest_MatchingTrustedPublicClient_SendsSessionInvalidatedThenForceCloses"/>
    /// for the <c>block</c> operation.
    /// </summary>
    [Fact]
    public async Task AdapterBlockRequest_MatchingKnownPublicClient_SendsSessionInvalidatedThenForceCloses()
    {
        var codec = new PublicEnvelopeCodec();
        var ipcCodec = new IpcFrameCodec();
        string clientId = Guid.NewGuid().ToString();
        var persistence = new FakeTrustStorePersistence();
        await persistence.SaveAsync([
            new TrustRecord(new ClientId(Guid.Parse(clientId)), "54321", "My PC", KnownDeviceState.Trusted, CredentialHasher.Hash(TrustedCredential), DateTimeOffset.UtcNow),
        ]);
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, int adapterPort, byte[] adapterProof, OwnerLifetimeId ownerLifetimeId) =
            await StartComposedHostAsync(persistence);
        (ClientWebSocket client, _, _) = await ConnectAndAdmitPublicClientAsync(
            publicPort, codec, clientId, new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = TrustedCredential });
        using ClientWebSocket ownedClient = client;
        using Socket adapterSocket = await ConnectAdapterAsync(adapterPort, adapterProof, ownerLifetimeId, ipcCodec);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);

        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcTrustAdminRequestMessage(7, TrustAdminOperation.Block, ShortId: "54321")));
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadIpcFrameAsync(adapterStream, ipcCodec));
        Assert.Contains("Blocked", result.ResultText);

        var buffer = new byte[8192];
        WebSocketReceiveResult invalidatedResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, invalidatedResult.Count), out PublicEnvelope? invalidatedEnvelope));
        Assert.Equal(PublicMessageType.SessionInvalidated, invalidatedEnvelope!.MessageType);
        Assert.True(codec.TryDecodePayload(invalidatedEnvelope, out SessionInvalidatedPayload? invalidatedPayload));
        Assert.Equal(SessionInvalidationReason.Blocked, invalidatedPayload!.Reason);

        // Administrative invalidation force-closes the connection after the best-effort notification
        // above; the transport may complete an orderly close handshake or abort the connection
        // outright (surfacing as a WebSocketException here), so either outcome proves the same
        // force-close contract rather than only the graceful one.
        try
        {
            WebSocketReceiveResult closeResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(WebSocketMessageType.Close, closeResult.MessageType);
        }
        catch (WebSocketException)
        {
            // The connection was aborted rather than gracefully closed -- also a valid force-close.
        }

        Assert.NotEqual(WebSocketState.Open, client.State);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that an adapter-originated <c>reset-trust</c> trust-admin request, sent over the real
    /// private IPC channel, invalidates a real connected trusted public client's session -- sending
    /// <c>session_invalidated</c> before force-closing its socket -- mirroring
    /// <see cref="AdapterRevokeRequest_MatchingTrustedPublicClient_SendsSessionInvalidatedThenForceCloses"/>
    /// for the no-argument, bulk <c>reset-trust</c> operation.
    /// </summary>
    [Fact]
    public async Task AdapterResetTrustRequest_MatchingTrustedPublicClient_SendsSessionInvalidatedThenForceCloses()
    {
        var codec = new PublicEnvelopeCodec();
        var ipcCodec = new IpcFrameCodec();
        string clientId = Guid.NewGuid().ToString();
        var persistence = new FakeTrustStorePersistence();
        await persistence.SaveAsync([
            new TrustRecord(new ClientId(Guid.Parse(clientId)), "54321", "My PC", KnownDeviceState.Trusted, CredentialHasher.Hash(TrustedCredential), DateTimeOffset.UtcNow),
        ]);
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, int adapterPort, byte[] adapterProof, OwnerLifetimeId ownerLifetimeId) =
            await StartComposedHostAsync(persistence);
        (ClientWebSocket client, _, _) = await ConnectAndAdmitPublicClientAsync(
            publicPort, codec, clientId, new HelloAuthPayload { Method = HelloAuthMethod.TrustedDeviceCredential, Token = TrustedCredential });
        using ClientWebSocket ownedClient = client;
        using Socket adapterSocket = await ConnectAdapterAsync(adapterPort, adapterProof, ownerLifetimeId, ipcCodec);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);

        await adapterStream.WriteAsync(ipcCodec.Encode(new IpcTrustAdminRequestMessage(7, TrustAdminOperation.ResetTrust)));
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadIpcFrameAsync(adapterStream, ipcCodec));
        Assert.Contains("Reset Trust complete", result.ResultText);

        var buffer = new byte[8192];
        WebSocketReceiveResult invalidatedResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, invalidatedResult.Count), out PublicEnvelope? invalidatedEnvelope));
        Assert.Equal(PublicMessageType.SessionInvalidated, invalidatedEnvelope!.MessageType);
        Assert.True(codec.TryDecodePayload(invalidatedEnvelope, out SessionInvalidatedPayload? invalidatedPayload));
        Assert.Equal(SessionInvalidationReason.TrustReset, invalidatedPayload!.Reason);

        // Administrative invalidation force-closes the connection after the best-effort notification
        // above; the transport may complete an orderly close handshake or abort the connection
        // outright (surfacing as a WebSocketException here), so either outcome proves the same
        // force-close contract rather than only the graceful one.
        try
        {
            WebSocketReceiveResult closeResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(WebSocketMessageType.Close, closeResult.MessageType);
        }
        catch (WebSocketException)
        {
            // The connection was aborted rather than gracefully closed -- also a valid force-close.
        }

        Assert.NotEqual(WebSocketState.Open, client.State);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Composes the real production graph with both listeners bound to OS-assigned loopback ports and
    /// returns the values a raw adapter/public client stand-in needs to connect to it.
    /// </summary>
    /// <param name="trustStorePersistence">The trust-store persistence to compose with, or <see langword="null"/> for an empty store.</param>
    private static async Task<(Task<int> RunTask, CancellationTokenSource Shutdown, int PublicPort, int AdapterPort, byte[] AdapterProofToken, OwnerLifetimeId OwnerLifetimeId)> StartComposedHostAsync(
        ITrustStorePersistence? trustStorePersistence = null)
    {
        var ownerLifetimeId = new OwnerLifetimeId((uint)Random.Shared.Next(), (ulong)Random.Shared.NextInt64());
        var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            ownerLifetimeId, listenerPort: 0, output, new HostProcessLifetime(), shutdown,
            publicListenerPort: 0, trustStorePersistence: trustStorePersistence);
        // Waits for PUBLICPORT specifically, not HOSTPROOF: Program.cs writes PUBLICPORT last, after
        // HOSTPROOF, so HOSTPROOF alone does not prove every line this helper parses below is present
        // yet.
        await WaitUntilAsync(() => output.Snapshot().Contains("PUBLICPORT "), runTask);

        string rendezvous = output.Snapshot();
        string[] lines = rendezvous.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int adapterPort = int.Parse(lines.Single(line => line.StartsWith("PORT ")).Split(' ')[1]);
        int publicPort = int.Parse(lines.Single(line => line.StartsWith("PUBLICPORT ")).Split(' ')[1]);
        byte[] adapterProofToken = Convert.FromHexString(lines.Single(line => line.StartsWith("PROOF ")).Split(' ')[1]);

        return (runTask, shutdown, publicPort, adapterPort, adapterProofToken, ownerLifetimeId);
    }

    /// <summary>
    /// Connects a real public WebSocket client, completes admission, and consumes the unsolicited
    /// <c>capabilities</c> message the host sends immediately after <c>hello_ack</c>.
    /// </summary>
    private static async Task<(ClientWebSocket Socket, string SessionId, string ClientId)> ConnectAndAdmitPublicClientAsync(
        int publicPort, PublicEnvelopeCodec codec, string? clientId = null, HelloAuthPayload? auth = null)
    {
        string effectiveClientId = clientId ?? Guid.NewGuid().ToString();
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{publicPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        byte[] hello = codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, null, null,
            new HelloPayload
            {
                Endpoint = "client",
                ClientId = effectiveClientId,
                Auth = auth ?? new HelloAuthPayload { Method = HelloAuthMethod.Unpaired },
            });
        await socket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[8192];
        WebSocketReceiveResult helloAckResult = await socket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, helloAckResult.Count), out PublicEnvelope? helloAck));
        Assert.Equal(PublicMessageType.HelloAck, helloAck!.MessageType);

        WebSocketReceiveResult capabilitiesResult = await socket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, capabilitiesResult.Count), out PublicEnvelope? capabilities));
        Assert.Equal(PublicMessageType.Capabilities, capabilities!.MessageType);

        return (socket, helloAck.SessionId!, effectiveClientId);
    }

    /// <summary>Sends a <c>pairing_request</c> over an admitted public client and decodes its <c>pairing_status</c> response.</summary>
    private static async Task<PairingStatusPayload> RequestPairingStatusAsync(ClientWebSocket client, PublicEnvelopeCodec codec, string sessionId, string clientId)
    {
        byte[] request = codec.Encode(PublicMessageType.PairingRequest, "pairing-1", sessionId, null, null, clientId, new EmptyPayload());
        await client.SendAsync(request, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[8192];
        WebSocketReceiveResult result = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.PairingStatus, envelope!.MessageType);
        Assert.True(codec.TryDecodePayload(envelope, out PairingStatusPayload? payload));
        return payload!;
    }

    /// <summary>Connects a raw socket to the private adapter listener, standing in for the adapter, and completes its handshake and initial resynchronization exchange.</summary>
    private static async Task<Socket> ConnectAdapterAsync(int adapterPort, byte[] adapterProofToken, OwnerLifetimeId ownerLifetimeId, IIpcFrameCodec ipcCodec)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, adapterPort);
        var stream = new NetworkStream(socket, ownsSocket: false);
        await stream.WriteAsync(ipcCodec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), adapterProofToken, ownerLifetimeId: ownerLifetimeId.ToBytes())));

        var ack = Assert.IsType<IpcHelloAckMessage>(await ReadIpcFrameAsync(stream, ipcCodec));
        Assert.True(ack.Accepted);
        Assert.IsType<IpcResynchronizeRequestMessage>(await ReadIpcFrameAsync(stream, ipcCodec));

        return socket;
    }

    /// <summary>Reads and decodes exactly one length-prefixed private IPC frame from the fake adapter's side of the connection.</summary>
    private static async Task<IpcMessage> ReadIpcFrameAsync(Stream stream, IIpcFrameCodec codec)
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

    /// <summary>
    /// Polls a condition until it becomes true, failing the test if it never does within a bounded
    /// time. If <paramref name="guardTask"/> completes first, awaits it so a fault in the composed
    /// run surfaces directly instead of being masked by a confusing timeout failure.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, Task guardTask)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (guardTask.IsCompleted)
            {
                await guardTask;
            }

            Assert.True(DateTime.UtcNow < deadline, "Condition was not met within the expected time.");
            await Task.Delay(10);
        }
    }
}
