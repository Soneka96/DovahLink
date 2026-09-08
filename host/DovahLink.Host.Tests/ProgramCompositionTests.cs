using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.Process;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests;

/// <summary>Tests for <see cref="global::Program.ComposeAndRunAsync"/>.</summary>
/// <remarks>
/// Shares <see cref="RealSocketAndProcessTestCollection"/> with
/// <see cref="Client.Transport.PublicWebSocketConnectionTests"/>: see that collection's own
/// documentation for why.
/// </remarks>
[Collection(RealSocketAndProcessTestCollection.Name)]
public class ProgramCompositionTests
{
    /// <summary>Verifies that a missing launch argument falls back to the default owner-lifetime-id.</summary>
    [Fact]
    public void ParseOwnerLifetimeIdArgument_NoArguments_ReturnsDefault()
    {
        OwnerLifetimeId result = global::Program.ParseOwnerLifetimeIdArgument([]);

        Assert.Equal(default, result);
    }

    /// <summary>Verifies that an unparseable first argument falls back to the default owner-lifetime-id.</summary>
    [Fact]
    public void ParseOwnerLifetimeIdArgument_Unparseable_ReturnsDefault()
    {
        OwnerLifetimeId result = global::Program.ParseOwnerLifetimeIdArgument(["not-valid-hex"]);

        Assert.Equal(default, result);
    }

    /// <summary>Verifies that a valid first argument is parsed into the matching owner-lifetime-id.</summary>
    [Fact]
    public void ParseOwnerLifetimeIdArgument_ValidArgument_ReturnsParsedValue()
    {
        var expected = new OwnerLifetimeId(1234, 5678);

        OwnerLifetimeId result = global::Program.ParseOwnerLifetimeIdArgument([expected.Format()]);

        Assert.Equal(expected, result);
    }

    /// <summary>Verifies that an unset environment variable value leaves the public listener disabled.</summary>
    [Fact]
    public void ParseTestPublicListenerPort_NullValue_ReturnsNull()
    {
        int? result = global::Program.ParseTestPublicListenerPort(null);

        Assert.Null(result);
    }

    /// <summary>Verifies that an unparseable environment variable value leaves the public listener disabled.</summary>
    [Fact]
    public void ParseTestPublicListenerPort_Unparseable_ReturnsNull()
    {
        int? result = global::Program.ParseTestPublicListenerPort("not-a-port");

        Assert.Null(result);
    }

    /// <summary>Verifies that a valid environment variable value is parsed into the matching port.</summary>
    [Fact]
    public void ParseTestPublicListenerPort_ValidValue_ReturnsParsedPort()
    {
        int? result = global::Program.ParseTestPublicListenerPort("58426");

        Assert.Equal(58426, result);
    }

    /// <summary>Verifies that <c>0</c> (the OS-assigned ephemeral port request test composition relies on) is preserved rather than rejected.</summary>
    [Fact]
    public void ParseTestPublicListenerPort_ZeroValue_ReturnsParsedPort()
    {
        int? result = global::Program.ParseTestPublicListenerPort("0");

        Assert.Equal(0, result);
    }

    /// <summary>Verifies that the maximum valid TCP port is accepted, not rejected as out of range.</summary>
    [Fact]
    public void ParseTestPublicListenerPort_MaxValidPort_ReturnsParsedPort()
    {
        int? result = global::Program.ParseTestPublicListenerPort("65535");

        Assert.Equal(65535, result);
    }

    /// <summary>Verifies that a negative value leaves the public listener disabled rather than reaching the listener.</summary>
    [Fact]
    public void ParseTestPublicListenerPort_NegativeValue_ReturnsNull()
    {
        int? result = global::Program.ParseTestPublicListenerPort("-1");

        Assert.Null(result);
    }

    /// <summary>Verifies that a value one above the maximum valid TCP port leaves the public listener disabled.</summary>
    [Fact]
    public void ParseTestPublicListenerPort_PortOutOfRange_ReturnsNull()
    {
        int? result = global::Program.ParseTestPublicListenerPort("65536");

        Assert.Null(result);
    }

    /// <summary>Verifies that an unset environment variable value falls back to the production public listener port.</summary>
    [Fact]
    public void ResolvePublicListenerPort_NullValue_ReturnsProductionPort()
    {
        int result = global::Program.ResolvePublicListenerPort(null);

        Assert.Equal(Constants.PublicWebSocketPort, result);
    }

    /// <summary>Verifies that an unparseable environment variable value falls back to the production public listener port.</summary>
    [Fact]
    public void ResolvePublicListenerPort_Unparseable_ReturnsProductionPort()
    {
        int result = global::Program.ResolvePublicListenerPort("not-a-port");

        Assert.Equal(Constants.PublicWebSocketPort, result);
    }

    /// <summary>Verifies that an out-of-range environment variable value falls back to the production public listener port rather than reaching the listener.</summary>
    [Fact]
    public void ResolvePublicListenerPort_PortOutOfRange_ReturnsProductionPort()
    {
        int result = global::Program.ResolvePublicListenerPort("65536");

        Assert.Equal(Constants.PublicWebSocketPort, result);
    }

    /// <summary>Verifies that a valid environment variable value overrides the production public listener port.</summary>
    [Fact]
    public void ResolvePublicListenerPort_ValidValue_ReturnsOverridePort()
    {
        int result = global::Program.ResolvePublicListenerPort("58426");

        Assert.Equal(58426, result);
    }

    /// <summary>Verifies that an unset environment variable value leaves the real production trust store untouched.</summary>
    [Fact]
    public void ResolveTestTrustStorePersistence_NullValue_ReturnsNull()
    {
        ITrustStorePersistence? result = global::Program.ResolveTestTrustStorePersistence(null);

        Assert.Null(result);
    }

    /// <summary>Verifies that an all-whitespace environment variable value leaves the real production trust store untouched.</summary>
    [Fact]
    public void ResolveTestTrustStorePersistence_WhitespaceValue_ReturnsNull()
    {
        ITrustStorePersistence? result = global::Program.ResolveTestTrustStorePersistence("   ");

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that a valid environment variable value resolves to persistence backed by that exact
    /// path, proving a real cross-process test launch's trust state lands in its own private file
    /// rather than the real per-Windows-user DPAPI store.
    /// </summary>
    [Fact]
    public async Task ResolveTestTrustStorePersistence_ValidValue_ReturnsPersistenceBackedByThatExactPath()
    {
        string overridePath = Path.Combine(Path.GetTempPath(), $"dovahlink-trust-store-test-{Guid.NewGuid():N}.dat");
        try
        {
            ITrustStorePersistence? result = global::Program.ResolveTestTrustStorePersistence(overridePath);
            Assert.NotNull(result);
            var record = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };

            await result!.SaveAsync([record]);

            Assert.True(File.Exists(overridePath));
            IReadOnlyList<TrustRecord> loaded = await result.LoadAsync();
            Assert.Equal([record], loaded);
        }
        finally
        {
            if (File.Exists(overridePath))
            {
                File.Delete(overridePath);
            }
        }
    }

    /// <summary>
    /// Verifies that composing and running reports the bound port, peer-proof token, and HostProof
    /// key over the rendezvous output.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_ReportsPortAndProofOverRendezvousOutput()
    {
        var ownerLifetimeId = UniqueOwnerLifetimeId();
        using var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            ownerLifetimeId, listenerPort: 0, output, new HostProcessLifetime(), shutdown);
        await WaitUntilAsync(() => output.Snapshot().Contains("PORT "), runTask);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        string reported = output.Snapshot();
        Assert.Matches(@"PORT \d+", reported);
        Assert.Matches("PROOF [0-9a-f]+", reported);
        Assert.Matches("HOSTPROOF [0-9a-f]+", reported);
    }

    /// <summary>
    /// Verifies that composing and running publishes the rendezvous file for the given
    /// owner-lifetime-id, including the HostProof key.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_PublishesRendezvousFile()
    {
        var ownerLifetimeId = UniqueOwnerLifetimeId();
        using var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();
        string rendezvousPath = Constants.RendezvousFilePath(ownerLifetimeId);

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            ownerLifetimeId, listenerPort: 0, output, new HostProcessLifetime(), shutdown);
        await WaitUntilAsync(() => File.Exists(rendezvousPath), runTask);

        try
        {
            string content = File.ReadAllText(rendezvousPath);
            Assert.Matches(@"PORT \d+", content);
            Assert.Matches("PROOF [0-9a-f]+", content);
            Assert.Matches("HOSTPROOF [0-9a-f]+", content);
        }
        finally
        {
            shutdown.Cancel();
            await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(rendezvousPath);
        }
    }

    /// <summary>Verifies that cancelling the shared shutdown source ends a composed run.</summary>
    [Fact]
    public async Task ComposeAndRunAsync_ShutdownCancelled_Ends()
    {
        using var shutdown = new CancellationTokenSource();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), listenerPort: 0, new SynchronizedTextCapture(), new HostProcessLifetime(), shutdown);
        Assert.False(runTask.IsCompleted);

        shutdown.Cancel();

        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that setting the adapter's named shutdown-request signal ends a composed run, exactly as an orderly Skyrim close would.</summary>
    [Fact]
    public async Task ComposeAndRunAsync_ShutdownSignalSet_Ends()
    {
        var ownerLifetimeId = UniqueOwnerLifetimeId();
        using var shutdown = new CancellationTokenSource();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            ownerLifetimeId, listenerPort: 0, new SynchronizedTextCapture(), new HostProcessLifetime(), shutdown);
        Assert.False(runTask.IsCompleted);

        using var adapterSideHandle = new EventWaitHandle(
            false, EventResetMode.ManualReset, Constants.ShutdownEventName(ownerLifetimeId));
        adapterSideHandle.Set();

        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Verifies that a private-IPC listener bind failure propagates out of composition rather than being swallowed.</summary>
    [Fact]
    public async Task ComposeAndRunAsync_ListenerPortAlreadyBound_Throws()
    {
        using var occupyingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        occupyingSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        occupyingSocket.Listen(1);
        int occupiedPort = ((IPEndPoint)occupyingSocket.LocalEndPoint!).Port;
        using var shutdown = new CancellationTokenSource();

        await Assert.ThrowsAsync<SocketException>(() => global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), occupiedPort, new SynchronizedTextCapture(), new HostProcessLifetime(), shutdown));
    }

    /// <summary>
    /// Verifies that a public listener bind failure propagates out of composition rather than being
    /// swallowed, the same as the private adapter-IPC listener's own bind-failure guarantee above.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_PublicListenerPortAlreadyBound_Throws()
    {
        using var occupyingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        occupyingSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        occupyingSocket.Listen(1);
        int occupiedPort = ((IPEndPoint)occupyingSocket.LocalEndPoint!).Port;
        using var shutdown = new CancellationTokenSource();

        await Assert.ThrowsAsync<SocketException>(() => global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), listenerPort: 0, new SynchronizedTextCapture(), new HostProcessLifetime(), shutdown,
            publicListenerPort: occupiedPort));
    }

    /// <summary>Verifies that omitting the public listener port -- the production <c>Main</c> entry point's own default -- never activates the public listener.</summary>
    [Fact]
    public async Task ComposeAndRunAsync_NoPublicListenerPort_NeverReportsPublicPort()
    {
        using var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), listenerPort: 0, output, new HostProcessLifetime(), shutdown);
        await WaitUntilAsync(() => output.Snapshot().Contains("PORT "), runTask);

        // Known limitation: proving PUBLICPORT is never written is proving a negative, and
        // Program.cs exposes no cheaper completion signal for the omitted-listener path -- unlike
        // the positive case, there is no later text marker to wait on, since starting the public
        // listener (if this branch were ever wrongly taken) involves real async socket setup before
        // it would write anything. This bounded margin gives that setup time to happen before the
        // assertion below, rather than asserting immediately after "PORT " and risking a false pass.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain("PUBLICPORT", output.Snapshot());
    }

    /// <summary>
    /// Verifies that supplying a public listener port composes and runs a real public WebSocket
    /// listener that accepts a loopback client and completes an unpaired <c>hello</c>/<c>hello_ack</c>
    /// exchange -- the composed public boundary actually works, not merely that it compiles.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_PublicListenerPortSupplied_AcceptsClientAndCompletesHelloAck()
    {
        using var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), listenerPort: 0, output, new HostProcessLifetime(), shutdown, publicListenerPort: 0);
        await WaitUntilAsync(() => output.Snapshot().Contains("PUBLICPORT "), runTask);
        string rendezvous = output.Snapshot();
        int publicPort = int.Parse(rendezvous.Split('\n').Single(line => line.StartsWith("PUBLICPORT ")).Split(' ')[1]);

        var codec = new PublicEnvelopeCodec();
        using var clientWebSocket = new ClientWebSocket();
        await clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{publicPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        byte[] hello = codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, null, null,
            new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });
        await clientWebSocket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[4096];
        WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? envelope));
        Assert.Equal(PublicMessageType.HelloAck, envelope!.MessageType);
        Assert.Equal("hello-1", envelope.CorrelationId);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that malformed or undecryptable trust persistence fails the entire composition
    /// closed -- neither listener is ever constructed or reported, rather than silently starting
    /// with a reset or partially loaded trust store.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_MalformedTrustPersistence_FailsClosedWithoutStartingEitherListener()
    {
        using var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();
        var persistence = new FakeTrustStorePersistence { ThrowOnLoad = new InvalidDataException("corrupt") };

        await Assert.ThrowsAsync<InvalidDataException>(() => global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), listenerPort: 0, output, new HostProcessLifetime(), shutdown,
            publicListenerPort: 0, trustStorePersistence: persistence));

        Assert.DoesNotContain("PORT", output.Snapshot());
    }

    /// <summary>
    /// Verifies that neither listener is ever constructed while trust persistence is still loading,
    /// so a slow or malformed load can never race a client's connection attempt against a
    /// not-yet-fully-loaded trust store -- the ordering proof handed off to this concept by
    /// <c>DIVERGENCES.md</c>'s D4.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_TrustPersistenceLoadInProgress_NeitherListenerIsReportedUntilItCompletes()
    {
        using var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();
        var enteredLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistence = new FakeTrustStorePersistence
        {
            BeforeLoad = async () =>
            {
                enteredLoad.SetResult();
                await releaseLoad.Task;
            },
        };

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), listenerPort: 0, output, new HostProcessLifetime(), shutdown,
            publicListenerPort: 0, trustStorePersistence: persistence);
        await enteredLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(string.Empty, output.Snapshot());

        releaseLoad.SetResult();
        await WaitUntilAsync(() => output.Snapshot().Contains("PUBLICPORT "), runTask);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Stress-races a public client's connect/hello/authentication/pairing exactly against
    /// <see cref="global::Program.ComposeAndRunAsync"/>'s own shutdown across many iterations, so
    /// the exact interleaving varies from run to run rather than being fixed by a single coordinated
    /// delay. Proves shutdown never deadlocks regardless of exactly when it lands relative to
    /// admission or pairing, that a racing client always observes one of exactly two well-defined
    /// outcomes -- a completed exchange, or the connection ending -- never a hang, and (once
    /// <see cref="global::Program.ComposeAndRunAsync"/> itself has returned, so its own
    /// deterministic session/private-IPC teardown has already run) that no authoritative session or
    /// active pairing challenge survives for that iteration's client, regardless of exactly where in
    /// the exchange shutdown landed.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_ShutdownRacingPublicHelloAdmission_NeverDeadlocksAndClientNeverHangs()
    {
        const int iterations = 10;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            using var shutdown = new CancellationTokenSource();
            var output = new SynchronizedTextCapture();
            SessionRegistry? sessionRegistry = null;
            PairingCoordinator? pairingCoordinator = null;
            var clientId = new ClientId(Guid.NewGuid());

            Task<int> runTask = global::Program.ComposeAndRunAsync(
                UniqueOwnerLifetimeId(), listenerPort: 0, output, new HostProcessLifetime(), shutdown, publicListenerPort: 0,
                onComposed: (composedSessionRegistry, composedPairingCoordinator) =>
                {
                    sessionRegistry = composedSessionRegistry;
                    pairingCoordinator = composedPairingCoordinator;
                });
            await WaitUntilAsync(() => output.Snapshot().Contains("PUBLICPORT "), runTask);
            string rendezvous = output.Snapshot();
            int publicPort = int.Parse(rendezvous.Split('\n').Single(line => line.StartsWith("PUBLICPORT ")).Split(' ')[1]);

            var codec = new PublicEnvelopeCodec();
            using var clientWebSocket = new ClientWebSocket();
            Task connectHelloAndPairTask = ConnectHelloAndPairAsync(clientWebSocket, publicPort, codec, clientId);

            // No coordinated delay: shutdown fires as soon as the connect/hello/pairing race is
            // launched, so successive iterations naturally vary which side of the admission or
            // pairing window it lands on.
            shutdown.Cancel();

            // Neither side may hang, regardless of how they interleaved on this iteration.
            await connectHelloAndPairTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));

            // ComposeAndRunAsync has now returned, so its own deterministic public-session and
            // private-IPC teardown already ran (see its own await adapterListenerTask/publicListenerTask
            // sequencing) -- the private Adapter IPC listener and public listener being stopped is
            // already proven by that return itself, not re-asserted here. No authoritative public
            // session survives this iteration's client, regardless of whether the race landed before,
            // during, or after admission.
            Assert.NotNull(sessionRegistry);
            Assert.Equal(0, sessionRegistry.ActiveCount);

            // No active (committed/displayed) pairing challenge survives for this iteration's client:
            // a genuinely interrupted pairing_request either never started, or its own rollback path
            // already ran before ComposeAndRunAsync returned. UncommittedDisplayReservation is
            // accepted alongside Idle -- per PairingCoordinator's own documented distinction, it was
            // never actually shown to the client, so it is not an active challenge either.
            Assert.NotNull(pairingCoordinator);
            PairingStatusSnapshot snapshot = pairingCoordinator.GetStatusSnapshot(clientId);
            Assert.True(
                snapshot.Kind is PairingStatusKind.Idle or PairingStatusKind.UncommittedDisplayReservation,
                $"Expected no active pairing challenge to survive shutdown, but observed {snapshot.Kind}.");
        }
    }

    /// <summary>
    /// Verifies that shutdown cleanly closes an already-admitted, steady-state (idle, not
    /// mid-handshake) client connection and still returns a successful exit code -- the one shutdown
    /// scenario <see cref="ComposeAndRunAsync_ShutdownRacingPublicHelloAdmission_NeverDeadlocksAndClientNeverHangs"/>
    /// does not cover, since it always shuts down mid-connect/hello/pairing rather than after a
    /// connection has settled into steady state.
    /// </summary>
    [Fact]
    public async Task ComposeAndRunAsync_ShutdownWithSteadyStateAdmittedClient_ClosesConnectionAndReturnsCleanly()
    {
        using var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            UniqueOwnerLifetimeId(), listenerPort: 0, output, new HostProcessLifetime(), shutdown, publicListenerPort: 0);
        await WaitUntilAsync(() => output.Snapshot().Contains("PUBLICPORT "), runTask);
        string rendezvous = output.Snapshot();
        int publicPort = int.Parse(rendezvous.Split('\n').Single(line => line.StartsWith("PUBLICPORT ")).Split(' ')[1]);

        var codec = new PublicEnvelopeCodec();
        using var clientWebSocket = new ClientWebSocket();
        await clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{publicPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        byte[] hello = codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, null, null,
            new HelloPayload { Endpoint = "client", ClientId = Guid.NewGuid().ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });
        await clientWebSocket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[4096];
        WebSocketReceiveResult helloAckResult = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, helloAckResult.Count), out PublicEnvelope? helloAckEnvelope));
        Assert.Equal(PublicMessageType.HelloAck, helloAckEnvelope!.MessageType);

        // Every admission sends hello_ack followed by an unsolicited, empty capabilities
        // advertisement, drained here so the connection is fully idle -- steady state -- before
        // shutdown fires below.
        WebSocketReceiveResult capabilitiesResult = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, capabilitiesResult.Count), out PublicEnvelope? capabilitiesEnvelope));
        Assert.Equal(PublicMessageType.Capabilities, capabilitiesEnvelope!.MessageType);

        shutdown.Cancel();

        // Shutdown must close the connection in a well-defined way: an orderly close handshake or an
        // abort (surfacing as a WebSocketException here) are both valid outcomes, matching the same
        // force-close contract the administrative-invalidation tests already prove for their own
        // trigger.
        try
        {
            WebSocketReceiveResult closeResult = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(WebSocketMessageType.Close, closeResult.MessageType);
        }
        catch (WebSocketException)
        {
            // The connection was aborted rather than gracefully closed -- also a valid close.
        }

        Assert.NotEqual(WebSocketState.Open, clientWebSocket.State);
        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Connects, sends an unpaired <c>hello</c> for <paramref name="clientId"/>, and -- once
    /// admitted -- also sends a <c>pairing_request</c>, awaiting exactly one well-defined outcome at
    /// each step for
    /// <see cref="ComposeAndRunAsync_ShutdownRacingPublicHelloAdmission_NeverDeadlocksAndClientNeverHangs"/>:
    /// a decoded reply, or the connection ending (refused, closed, or faulted) before one arrives, at
    /// any point from the initial connect onward. Any other observation -- in particular hanging past
    /// the caller's own bounded wait -- fails this method's caller instead.
    /// </summary>
    private static async Task ConnectHelloAndPairAsync(ClientWebSocket clientWebSocket, int port, PublicEnvelopeCodec codec, ClientId clientId)
    {
        try
        {
            await clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            byte[] hello = codec.Encode(
                PublicMessageType.Hello, "hello-1", null, null, null, null,
                new HelloPayload { Endpoint = "client", ClientId = clientId.Value.ToString(), Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });
            await clientWebSocket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new byte[4096];
            WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // The connection closed instead of admitting: also well-defined.
                return;
            }

            Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? helloAckEnvelope));
            Assert.Equal(PublicMessageType.HelloAck, helloAckEnvelope!.MessageType);
            string sessionId = helloAckEnvelope.SessionId!;

            // Every admission sends hello_ack followed by an unsolicited, empty capabilities
            // advertisement -- consumed here so it is never mistaken for a pairing_status reply.
            result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? capabilitiesEnvelope));
            Assert.Equal(PublicMessageType.Capabilities, capabilitiesEnvelope!.MessageType);

            // A post-admission client message must carry both the socket-bound sessionId and the
            // envelope-level clientId it declared in hello.
            byte[] pairingRequest = codec.Encode(
                PublicMessageType.PairingRequest, "pairing-1", sessionId, null, null, clientId.Value.ToString(), new EmptyPayload());
            await clientWebSocket.SendAsync(pairingRequest, WebSocketMessageType.Text, true, CancellationToken.None);

            result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // The connection closed instead of answering pairing_request: also well-defined.
                return;
            }

            Assert.True(codec.TryDecode(buffer.AsMemory(0, result.Count), out PublicEnvelope? pairingEnvelope));
            // pairing_status (accepted or unavailable) and error (for example a stale session raced
            // by shutdown between hello_ack and this send) are both well-defined outcomes here; only
            // an undecodable or unrelated reply would indicate a genuine protocol violation.
            Assert.True(pairingEnvelope!.MessageType is PublicMessageType.PairingStatus or PublicMessageType.Error);
        }
        catch (Exception exception) when (exception is WebSocketException or SocketException or IOException or OperationCanceledException)
        {
            // The listener or connection ended because the racing shutdown landed first, at whatever
            // point in the exchange it happened to land: also well-defined.
        }
    }

    /// <summary>Builds a unique owner-lifetime-id per test, so parallel and repeated test runs never collide over the same rendezvous file or named event.</summary>
    private static OwnerLifetimeId UniqueOwnerLifetimeId() =>
        new((uint)Random.Shared.Next(), (ulong)Random.Shared.NextInt64());

    /// <summary>Polls <paramref name="condition"/> until it is true, failing if <paramref name="runTask"/> ends first or the bound elapses.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, Task runTask)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (runTask.IsCompleted)
            {
                await runTask;
                Assert.Fail("The composed run ended before the expected condition became true.");
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting for the expected condition.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
