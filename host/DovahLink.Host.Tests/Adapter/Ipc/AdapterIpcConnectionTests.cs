using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="AdapterIpcConnection"/>.</summary>
public class AdapterIpcConnectionTests
{
    // ---- Handshake and resynchronization, over a real connected stream pair ----

    /// <summary>Verifies that a successful handshake sends the acknowledgement then the resynchronization request, and that disconnecting after notifies the session.</summary>
    [Fact]
    public async Task RunAsync_ValidHello_SendsAckThenResynchronizeRequestThenNotifiesDisconnect()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            HandshakeResult = new AdapterHandshakeResult(true, new IpcHelloAckMessage(1, true, IpcHelloRejectReason.None)),
            ResynchronizeRequest = new IpcResynchronizeRequestMessage(2),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        IpcMessage ack = await ReadOneFrameAsync(client, codec);
        IpcMessage resync = await ReadOneFrameAsync(client, codec);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<IpcHelloAckMessage>(ack);
        Assert.IsType<IpcResynchronizeRequestMessage>(resync);
        Assert.Single(fakeSession.HandshakeCalls);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
    }

    /// <summary>Verifies that a rejected handshake sends only the rejection acknowledgement and never requests resynchronization.</summary>
    [Fact]
    public async Task RunAsync_RejectedHandshake_SendsRejectionAckAndClosesWithoutResync()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            HandshakeResult = new AdapterHandshakeResult(false, new IpcHelloAckMessage(1, false, IpcHelloRejectReason.InvalidProof)),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        IpcMessage ack = await ReadOneFrameAsync(client, codec);
        int trailingByte = await client.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        var helloAck = Assert.IsType<IpcHelloAckMessage>(ack);
        Assert.False(helloAck.Accepted);
        Assert.Equal(0, trailingByte);
        client.Dispose();
    }

    /// <summary>Verifies that a first frame other than a Hello is handed to the session's generic frame handling and the resulting outcome is sent.</summary>
    [Fact]
    public async Task RunAsync_UnexpectedFirstMessage_SendsOutcomeMessageAndCloses()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            FrameOutcome = AdapterIpcOutcome.SendAndClose(new IpcRejectMessage(5, IpcRejectReason.UnknownMessageKind)),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcCancelMessage(5)));

        Task runTask = connection.RunAsync(CancellationToken.None);
        IpcMessage received = await ReadOneFrameAsync(client, codec);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        var reject = Assert.IsType<IpcRejectMessage>(received);
        Assert.Equal(IpcRejectReason.UnknownMessageKind, reject.Reason);
        Assert.Empty(fakeSession.HandshakeCalls);
        Assert.Single(fakeSession.HandledFrames);
        client.Dispose();
    }

    /// <summary>Verifies that a structurally invalid frame length triggers the session's decode-failure handling and sends its close message.</summary>
    [Fact]
    public async Task RunAsync_MalformedFrameLength_InvokesDecodeFailureAndSendsCloseMessage()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            DecodeFailureOutcome = AdapterIpcOutcome.SendAndClose(new IpcCloseMessage(0, IpcCloseReason.Error)),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }); // declares an over-limit frame length

        Task runTask = connection.RunAsync(CancellationToken.None);
        IpcMessage received = await ReadOneFrameAsync(client, codec);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        var close = Assert.IsType<IpcCloseMessage>(received);
        Assert.Equal(IpcCloseReason.Error, close.Reason);
        Assert.Equal(1, fakeSession.DecodeFailureCalls);
        Assert.Empty(fakeSession.HandshakeCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a post-handshake outcome requesting closure without messages ends the loop cleanly.</summary>
    [Fact]
    public async Task RunAsync_PostHandshakeOutcomeRequestsClose_EndsLoopWithoutFurtherMessages()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession { FrameOutcome = AdapterIpcOutcome.Close };
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        await client.WriteAsync(codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal)));
        int trailingByte = await client.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, trailingByte);
        Assert.Single(fakeSession.HandledFrames);
        Assert.IsType<IpcCloseMessage>(fakeSession.HandledFrames[0]);
        client.Dispose();
    }

    /// <summary>Verifies that cancelling before a Hello ever arrives propagates cancellation and still notifies the session of disconnection.</summary>
    [Fact]
    public async Task RunAsync_CancelledBeforeHandshake_PropagatesCancellationAndNotifiesDisconnect()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(server, new IpcFrameCodec(), fakeSession);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.RunAsync(cancellation.Token))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, fakeSession.DisconnectedCalls);
        client.Dispose();
    }

    // ---- Host-directed intents ----

    /// <summary>Verifies that a queued event-listening intent is actually written to the peer once connected.</summary>
    [Fact]
    public async Task TrySendListenEvent_Connected_DeliversFrameToPeer()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession { ListenEventResult = new IpcListenEventMessage(9, 42) };
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        bool enqueued = connection.TrySendListenEvent(42, out ulong correlationId);
        IpcMessage delivered = await ReadOneFrameAsync(client, codec);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(enqueued);
        Assert.Equal(9UL, correlationId);
        var listenEvent = Assert.IsType<IpcListenEventMessage>(delivered);
        Assert.Equal(42u, listenEvent.EventKey);
    }

    /// <summary>Verifies that a queued sample-read intent is actually written to the peer once connected.</summary>
    [Fact]
    public async Task TrySendReadSample_Connected_DeliversFrameToPeer()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession { ReadSampleResult = new IpcReadSampleMessage(9, 42) };
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        bool enqueued = connection.TrySendReadSample(42, out ulong correlationId);
        IpcMessage delivered = await ReadOneFrameAsync(client, codec);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(enqueued);
        Assert.Equal(9UL, correlationId);
        var readSample = Assert.IsType<IpcReadSampleMessage>(delivered);
        Assert.Equal(42u, readSample.SampleToken);
    }

    /// <summary>Verifies that a queued cancellation is actually written to the peer once connected.</summary>
    [Fact]
    public async Task TryCancel_Connected_DeliversFrameToPeer()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession { CancelResult = new IpcCancelMessage(7) };
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        bool enqueued = connection.TryCancel(7);
        IpcMessage delivered = await ReadOneFrameAsync(client, codec);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(enqueued);
        var cancel = Assert.IsType<IpcCancelMessage>(delivered);
        Assert.Equal(7UL, cancel.CorrelationId);
    }

    /// <summary>Verifies that cancelling while the read loop is waiting for the next post-handshake frame ends the connection without hanging.</summary>
    [Fact]
    public async Task RunAsync_CancelledDuringReadLoop_EndsWithoutHanging()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(server, codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));
        using var cancellation = new CancellationTokenSource();

        Task runTask = connection.RunAsync(cancellation.Token);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, fakeSession.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a write fault in the outbound writer loop ends the connection instead of hanging or crashing the caller.</summary>
    [Fact]
    public async Task RunAsync_WriteFaultInWriterLoop_EndsWithoutHangingOrThrowing()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(new WriteFaultingStream(server), codec, fakeSession);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));
        client.Dispose(); // nothing more will arrive; lets the read loop end once the handshake completes

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(fakeSession.HandshakeCalls);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
    }

    /// <summary>Verifies that an event-listening intent is refused before the session has anything to prepare it from.</summary>
    [Fact]
    public void TrySendListenEvent_SessionRefuses_ReturnsFalse()
    {
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), new FakeAdapterIpcSession());

        bool enqueued = connection.TrySendListenEvent(1, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(0UL, correlationId);
    }

    /// <summary>Verifies that a sample-read intent is refused before the session has anything to prepare it from.</summary>
    [Fact]
    public void TrySendReadSample_SessionRefuses_ReturnsFalse()
    {
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), new FakeAdapterIpcSession());

        bool enqueued = connection.TrySendReadSample(1, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(0UL, correlationId);
    }

    /// <summary>Verifies that a cancellation is refused before the session has anything to prepare it from.</summary>
    [Fact]
    public void TryCancel_SessionRefuses_ReturnsFalse()
    {
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), new FakeAdapterIpcSession());

        Assert.False(connection.TryCancel(1));
    }

    /// <summary>Verifies that the bounded outbound queue refuses further intents once it is full, rather than growing without limit.</summary>
    [Fact]
    public void TrySendListenEvent_QueueFull_ReturnsFalse()
    {
        var fakeSession = new FakeAdapterIpcSession { ListenEventResult = new IpcListenEventMessage(1, 1) };
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), fakeSession);
        for (int i = 0; i < Constants.MaxIpcQueuedMessages; i++)
        {
            Assert.True(connection.TrySendListenEvent(1, out _));
        }

        bool enqueued = connection.TrySendListenEvent(1, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(1UL, correlationId);
    }

    /// <summary>
    /// Creates a connected pair of loopback-socket streams for realistic byte-level I/O tests. A
    /// loopback socket is used rather than a named pipe purely as a test transport convenience --
    /// the production listener still uses a named pipe -- since the connection under test depends
    /// only on <see cref="Stream"/>.
    /// </summary>
    private static async Task<(Stream Server, Stream Client)> CreateConnectedStreamPairAsync()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        Task<Socket> acceptTask = listener.AcceptAsync();
        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync((IPEndPoint)listener.LocalEndPoint!);
        Socket serverSocket = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        return (new NetworkStream(serverSocket, ownsSocket: true), new NetworkStream(clientSocket, ownsSocket: true));
    }

    /// <summary>Reads and decodes exactly one frame from a raw transport, mirroring the connection's own wire protocol.</summary>
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
