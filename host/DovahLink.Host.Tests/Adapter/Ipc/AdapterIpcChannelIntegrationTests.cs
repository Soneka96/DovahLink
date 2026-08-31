using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>
/// Full-stack proof that the real listener, connection, session, codec, availability tracker, and
/// peer-proof verifier work together end to end over a real loopback socket, driven by a small
/// in-test stand-in for the adapter. Individual collaborators already have their own isolated unit
/// tests; this class proves only that the production composition is wired correctly.
/// </summary>
public class AdapterIpcChannelIntegrationTests
{
    /// <summary>Verifies that a valid Hello followed by an accepted resynchronization result leaves the tracker available and resynchronized.</summary>
    [Fact]
    public async Task Connect_ValidHelloThenAcceptedResync_TrackerBecomesAvailableAndResynchronized()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, instanceId, verifier.ExpectedToken)));
        IpcMessage ack = await ReadOneFrameAsync(adapterStream, codec);
        var request = Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, codec));

        Assert.True(Assert.IsType<IpcHelloAckMessage>(ack).Accepted);
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);
        Assert.Equal(instanceId, tracker.CurrentInstanceId);
        Assert.True(tracker.NeedsResynchronization);

        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true)));
        await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a declined resynchronization result leaves the tracker still needing resynchronization rather than clearing it.</summary>
    [Fact]
    public async Task Connect_ValidHelloThenDeclinedResync_TrackerStillNeedsResynchronization()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        var request = Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, codec));
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);

        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: false)));
        await Task.Delay(TimeSpan.FromMilliseconds(200)); // give a wrongly-clearing implementation a chance to show itself

        Assert.True(tracker.NeedsResynchronization);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a Hello with the wrong peer-ownership proof is rejected and never reports the tracker as available.</summary>
    [Fact]
    public async Task Connect_WrongProofHello_RejectsAndTrackerStaysUnavailable()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, _) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: false);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [9, 9, 9])));
        IpcMessage ack = await ReadOneFrameAsync(adapterStream, codec);

        Assert.False(Assert.IsType<IpcHelloAckMessage>(ack).Accepted);
        Assert.Equal(IpcHelloRejectReason.InvalidProof, ((IpcHelloAckMessage)ack).RejectReason);
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that the adapter disconnecting after a full resynchronization marks the tracker unavailable and needing a fresh resynchronization.</summary>
    [Fact]
    public async Task Disconnect_AfterResynchronized_MarksTrackerUnavailableAndNeedingResync()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();

        using Socket adapterSocket = await ConnectClientAsync(listener.BoundPort);
        using var adapterStream = new NetworkStream(adapterSocket, ownsSocket: true);
        await adapterStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), verifier.ExpectedToken)));
        await ReadOneFrameAsync(adapterStream, codec); // acknowledgement
        var request = Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(adapterStream, codec));
        await adapterStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(request.CorrelationId, Accepted: true)));
        await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);

        adapterStream.Dispose();

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);
        Assert.True(tracker.NeedsResynchronization);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that reconnecting after a disconnect is assigned a fresh connection generation and again requires a fresh baseline.</summary>
    [Fact]
    public async Task Reconnect_AfterDisconnect_GetsFreshGenerationAndRequiresFreshBaseline()
    {
        (IAdapterIpcListener listener, IAdapterAvailabilityTracker tracker, IAdapterPeerProofVerifier verifier) = CreateRealStack();
        using IAdapterIpcListener ownedListener = listener;
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        var codec = new IpcFrameCodec();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        using (Socket firstSocket = await ConnectClientAsync(listener.BoundPort))
        using (var firstStream = new NetworkStream(firstSocket, ownsSocket: false))
        {
            await firstStream.WriteAsync(codec.Encode(new IpcHelloMessage(1, instanceId, verifier.ExpectedToken)));
            await ReadOneFrameAsync(firstStream, codec); // acknowledgement
            var firstRequest = Assert.IsType<IpcResynchronizeRequestMessage>(await ReadOneFrameAsync(firstStream, codec));
            await firstStream.WriteAsync(codec.Encode(new IpcResynchronizeResultMessage(firstRequest.CorrelationId, Accepted: true)));
            await WaitUntilAsync(() => !tracker.NeedsResynchronization, runTask);
        }

        long firstGeneration = tracker.CurrentConnectionGeneration;
        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Unavailable, runTask);

        using Socket secondSocket = await ConnectClientAsync(listener.BoundPort);
        using var secondStream = new NetworkStream(secondSocket, ownsSocket: false);
        await secondStream.WriteAsync(codec.Encode(new IpcHelloMessage(2, instanceId, verifier.ExpectedToken)));
        await ReadOneFrameAsync(secondStream, codec); // acknowledgement
        await ReadOneFrameAsync(secondStream, codec); // fresh resynchronize request

        await WaitUntilAsync(() => tracker.Current == AdapterAvailability.Available, runTask);
        Assert.Equal(firstGeneration + 1, tracker.CurrentConnectionGeneration);
        Assert.True(tracker.NeedsResynchronization);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Composes the real production private-IPC graph over a listener bound to an OS-assigned loopback port.</summary>
    private static (IAdapterIpcListener Listener, IAdapterAvailabilityTracker Tracker, IAdapterPeerProofVerifier Verifier) CreateRealStack()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        var verifier = new AdapterPeerProofVerifier();
        var codec = new IpcFrameCodec();
        var listener = new AdapterIpcListener(0, stream =>
            new AdapterIpcConnection(stream, codec, new AdapterIpcSession(lifecycle, verifier), new SystemClock()));
        return (listener, tracker, verifier);
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

    /// <summary>
    /// Polls a condition until it becomes true, failing the test if it never does within a bounded
    /// time. If <paramref name="guardTask"/> completes first, awaits it so a fault in the listener's
    /// run loop surfaces directly instead of being masked by a confusing timeout failure.
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
