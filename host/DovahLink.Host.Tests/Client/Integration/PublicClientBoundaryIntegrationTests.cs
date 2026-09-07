using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Identity;
using DovahLink.Host.Process;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Client.Integration;

/// <summary>
/// Full-stack proof of the public client's own session-boundary contract over the actual composed
/// <see cref="global::Program.ComposeAndRunAsync"/> process graph with both the public WebSocket
/// listener and the private adapter IPC listener bound to real loopback sockets: a reconnect never
/// resumes a prior session, and the two listeners cannot be reached as if they were each other.
/// Individual collaborators already have their own isolated unit and lower-level integration tests;
/// this class proves only that the two boundaries are wired together correctly in production
/// composition.
/// </summary>
public class PublicClientBoundaryIntegrationTests
{
    /// <summary>Verifies that a second connection from the same clientId is admitted with a fresh sessionId, never resuming the first connection's session.</summary>
    [Fact]
    public async Task Reconnect_SameClientId_GetsFreshSessionThatNeverResumesThePrevious()
    {
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, _, _, _) = await StartComposedHostAsync();
        var codec = new PublicEnvelopeCodec();
        string clientId = Guid.NewGuid().ToString();

        string firstSessionId;
        using (ClientWebSocket firstClient = new())
        {
            firstSessionId = await ConnectAndAdmitAsync(firstClient, publicPort, codec, clientId);
            await firstClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }

        using var secondClient = new ClientWebSocket();
        string secondSessionId = await ConnectAndAdmitAsync(secondClient, publicPort, codec, clientId);

        Assert.NotEqual(firstSessionId, secondSessionId);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a WebSocket client cannot reach the private adapter listener as if it were the public listener.</summary>
    [Fact]
    public async Task PrivateAdapterListener_ConnectedAsPublicClient_RejectsTheHandshake()
    {
        (Task<int> runTask, CancellationTokenSource shutdown, _, int adapterPort, _, _) = await StartComposedHostAsync();

        using var client = new ClientWebSocket();
        await Assert.ThrowsAnyAsync<WebSocketException>(() =>
            client.ConnectAsync(new Uri($"ws://127.0.0.1:{adapterPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a raw private-IPC hello frame sent to the public listener is never interpreted and the connection is closed without a response.</summary>
    [Fact]
    public async Task PublicListener_ConnectedAsPrivateAdapter_ClosesWithoutInterpretingTheFrame()
    {
        (Task<int> runTask, CancellationTokenSource shutdown, int publicPort, _, byte[] adapterProof, _) = await StartComposedHostAsync();
        var ipcCodec = new IpcFrameCodec();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, publicPort);
        using var stream = new NetworkStream(socket, ownsSocket: false);
        await stream.WriteAsync(ipcCodec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), adapterProof)));

        // The bytes are not a valid HTTP request; the public listener keeps waiting for the rest of
        // one until its own five-second handshake deadline elapses and closes, so this wait exceeds
        // that deadline rather than racing it.
        byte[] buffer = new byte[64];
        int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(8));

        Assert.Equal(0, read);

        shutdown.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Composes the real production graph with both listeners bound to OS-assigned loopback ports and
    /// returns the values a raw adapter/public client stand-in needs to connect to it.
    /// </summary>
    private static async Task<(Task<int> RunTask, CancellationTokenSource Shutdown, int PublicPort, int AdapterPort, byte[] AdapterProofToken, OwnerLifetimeId OwnerLifetimeId)> StartComposedHostAsync()
    {
        var ownerLifetimeId = new OwnerLifetimeId((uint)Random.Shared.Next(), (ulong)Random.Shared.NextInt64());
        var shutdown = new CancellationTokenSource();
        var output = new SynchronizedTextCapture();

        Task<int> runTask = global::Program.ComposeAndRunAsync(
            ownerLifetimeId, listenerPort: 0, output, new HostProcessLifetime(), shutdown, publicListenerPort: 0);
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
    /// Connects an already-constructed WebSocket client, completes admission with the given clientId,
    /// consumes the unsolicited <c>capabilities</c> message the host sends immediately after
    /// <c>hello_ack</c>, and returns the admitted sessionId.
    /// </summary>
    private static async Task<string> ConnectAndAdmitAsync(ClientWebSocket client, int publicPort, PublicEnvelopeCodec codec, string clientId)
    {
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{publicPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        byte[] hello = codec.Encode(
            PublicMessageType.Hello, "hello-1", null, null, null, null,
            new HelloPayload { Endpoint = "client", ClientId = clientId, Auth = new HelloAuthPayload { Method = HelloAuthMethod.Unpaired } });
        await client.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[8192];
        WebSocketReceiveResult helloAckResult = await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(codec.TryDecode(buffer.AsMemory(0, helloAckResult.Count), out PublicEnvelope? helloAck));
        Assert.Equal(PublicMessageType.HelloAck, helloAck!.MessageType);

        await client.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); // unsolicited capabilities

        return helloAck.SessionId!;
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
