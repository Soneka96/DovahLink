using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.Time;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="AdapterIpcConnection"/>.</summary>
public class AdapterIpcConnectionTests
{
    /// <summary>Reads the shared native/host private-IPC rate-limit fixture.</summary>
    private static (int MaxMessagesPerSecond, TimeSpan MessageRateWindow) ReadPrivateIpcLimitsFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "integration", "private-ipc-limits.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        int maxMessages = document.RootElement.GetProperty("maxMessagesPerSecond").GetInt32();
        int windowMilliseconds = document.RootElement.GetProperty("messageRateWindowMilliseconds").GetInt32();
        return (maxMessages, TimeSpan.FromMilliseconds(windowMilliseconds));
    }

    /// <summary>Verifies that the host constants remain synchronized with the shared contract fixture.</summary>
    [Fact]
    public void InboundRateLimit_MatchesSharedPrivateIpcFixture()
    {
        (int maxMessages, TimeSpan window) = ReadPrivateIpcLimitsFixture();

        Assert.Equal(Constants.MaxIpcMessagesPerSecond, maxMessages);
        Assert.Equal(Constants.IpcMessageRateWindow, window);
    }

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
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        IpcMessage ack = await ReadOneFrameAsync(client, codec);
        IpcMessage resync = await ReadOneFrameAsync(client, codec);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<IpcHelloAckMessage>(ack);
        Assert.IsType<IpcResynchronizeRequestMessage>(resync);
        Assert.Single(fakeSession.HandshakeCalls);
        Assert.Equal(1, fakeSession.CommitHandshakeCalls);
        Assert.Equal(
            new[] { nameof(FakeAdapterIpcSession.Handshake), nameof(FakeAdapterIpcSession.PrepareResynchronizeRequest), nameof(FakeAdapterIpcSession.CommitHandshake) },
            fakeSession.LifecycleCalls);
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
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        IpcMessage ack = await ReadOneFrameAsync(client, codec);
        int trailingByte = await client.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        var helloAck = Assert.IsType<IpcHelloAckMessage>(ack);
        Assert.False(helloAck.Accepted);
        Assert.Equal(0, trailingByte);
        Assert.Equal(0, fakeSession.CommitHandshakeCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a full outbound queue prevents handshake commitment when the acknowledgement cannot be queued.</summary>
    [Fact]
    public async Task RunAsync_FullOutboundQueue_SkipsHandshakeCommit()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server);
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            ListenEventResult = new IpcListenEventMessage(1, 1),
            HandshakeResult = new AdapterHandshakeResult(true, new IpcHelloAckMessage(1, true, IpcHelloRejectReason.None)),
        };
        var connection = new AdapterIpcConnection(blockingStream, codec, fakeSession, new SystemClock());

        for (int index = 0; index < Constants.MaxIpcQueuedMessages; index++)
        {
            Assert.True(connection.TrySendListenEvent(1, out _));
        }

        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));
        Task runTask = connection.RunAsync(CancellationToken.None);

        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, fakeSession.CommitHandshakeCalls);
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
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcCancelMessage(5)));

        Task runTask = connection.RunAsync(CancellationToken.None);
        IpcMessage received = await ReadOneFrameAsync(client, codec);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        var reject = Assert.IsType<IpcRejectMessage>(received);
        Assert.Equal(IpcRejectReason.UnknownMessageKind, reject.Reason);
        Assert.Empty(fakeSession.HandshakeCalls);
        Assert.Single(fakeSession.HandledFrames);
        var handledRequest = Assert.IsType<IpcCancelMessage>(fakeSession.HandledFrames[0]);
        Assert.Equal(5UL, handledRequest.CorrelationId);
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
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
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

    /// <summary>Verifies that malformed input force-closes a writer that cannot drain its queued response.</summary>
    [Fact]
    public async Task RunAsync_MalformedFrameLength_ForceClosesBlockedWriter()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server);
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(blockingStream, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await client.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

        await AssertForcedClosureAsync(runTask, blockingStream, client);

        Assert.Equal(1, fakeSession.DecodeFailureCalls);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
    }

    /// <summary>Verifies that a post-handshake outcome requesting closure without messages ends the loop cleanly.</summary>
    [Fact]
    public async Task RunAsync_PostHandshakeOutcomeRequestsClose_EndsLoopWithoutFurtherMessages()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession { FrameOutcome = AdapterIpcOutcome.Close };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
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

    /// <summary>Verifies that a protocol close force-closes a writer that is blocked on an earlier outbound frame.</summary>
    [Fact]
    public async Task RunAsync_PostHandshakeClose_ForceClosesBlockedWriter()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server, writesBeforeBlocking: 2);
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            FrameOutcome = AdapterIpcOutcome.Close,
            ListenEventResult = new IpcListenEventMessage(3, 42),
        };
        var connection = new AdapterIpcConnection(blockingStream, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // acknowledgement
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendListenEvent(42, out _));
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await client.WriteAsync(codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal)));

        await AssertForcedClosureAsync(runTask, blockingStream, client);

        Assert.Single(fakeSession.HandledFrames);
        Assert.IsType<IpcCloseMessage>(fakeSession.HandledFrames[0]);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
    }

    /// <summary>Verifies that an unexpected first message force-closes a writer blocked on its rejection response.</summary>
    [Fact]
    public async Task RunAsync_UnexpectedFirstMessage_ForceClosesBlockedWriter()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server);
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            FrameOutcome = AdapterIpcOutcome.SendAndClose(new IpcRejectMessage(5, IpcRejectReason.UnknownMessageKind)),
        };
        var connection = new AdapterIpcConnection(blockingStream, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcCancelMessage(5)));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        await AssertForcedClosureAsync(runTask, blockingStream, client);

        Assert.Single(fakeSession.HandledFrames);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
    }

    /// <summary>Verifies that a rejected handshake force-closes a writer blocked on its rejection acknowledgement.</summary>
    [Fact]
    public async Task RunAsync_RejectedHandshake_ForceClosesBlockedWriter()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server);
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            HandshakeResult = new AdapterHandshakeResult(false, new IpcHelloAckMessage(1, false, IpcHelloRejectReason.InvalidProof)),
        };
        var connection = new AdapterIpcConnection(blockingStream, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        await AssertForcedClosureAsync(runTask, blockingStream, client);

        Assert.Single(fakeSession.HandshakeCalls);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
    }

    /// <summary>Verifies that cancelling before a Hello ever arrives propagates cancellation and still notifies the session of disconnection.</summary>
    [Fact]
    public async Task RunAsync_CancelledBeforeHandshake_PropagatesCancellationAndNotifiesDisconnect()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(server, new IpcFrameCodec(), fakeSession, new SystemClock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.RunAsync(cancellation.Token))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, fakeSession.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a peer withholding its Hello past the handshake deadline is disconnected without ever reaching the session's handshake handling.</summary>
    [Fact]
    public async Task RunAsync_NoHelloBeforeHandshakeTimeout_DisconnectsWithoutAccepting()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(server, new IpcFrameCodec(), fakeSession, new SystemClock());

        await connection.RunAsync(CancellationToken.None)
            .WaitAsync(Constants.AdapterIpcHandshakeTimeout + TimeSpan.FromSeconds(5));

        Assert.Empty(fakeSession.HandshakeCalls);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that the handshake deadline also bounds a Hello frame left incomplete partway through, not only a peer that never sends anything.</summary>
    [Fact]
    public async Task RunAsync_PartialHelloFrameBeforeHandshakeTimeout_DisconnectsWithoutAccepting()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(server, new IpcFrameCodec(), fakeSession, new SystemClock());
        byte[] helloFrame = new IpcFrameCodec().Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), []));
        await client.WriteAsync(helloFrame.AsMemory(0, sizeof(uint))); // length prefix only; payload withheld

        await connection.RunAsync(CancellationToken.None)
            .WaitAsync(Constants.AdapterIpcHandshakeTimeout + TimeSpan.FromSeconds(5));

        Assert.Empty(fakeSession.HandshakeCalls);
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
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
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
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
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
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
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
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
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
        var connection = new AdapterIpcConnection(new WriteFaultingStream(server), codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(fakeSession.HandshakeCalls);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that an unexpected writer exception cancels the reader and still disposes the connection.</summary>
    [Fact]
    public async Task RunAsync_UnexpectedWriteFault_EndsWithoutHangingOrThrowing()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var session = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(
            new WriteFaultingStream(server, new InvalidOperationException("Simulated unexpected write fault.")),
            codec,
            session,
            new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(session.HandshakeCalls);
        Assert.Equal(1, session.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that peer EOF closes a transport before awaiting a writer blocked in I/O.</summary>
    [Fact]
    public async Task RunAsync_PeerEofStopsBlockedWriterAndNotifiesDisconnect()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server);
        var codec = new IpcFrameCodec();
        var session = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(blockingStream, codec, session, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
        client.Dispose();

        try
        {
            await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(blockingStream.IsDisposed);
        }
        finally
        {
            blockingStream.Release();
            try
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception) when (runTask.IsCompleted)
            {
                // Preserve the original assertion or timeout result from the main await.
            }
        }

        Assert.Equal(1, session.DisconnectedCalls);
    }

    /// <summary>Verifies that a reader I/O failure ends the connection without escaping to the caller.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ReadTransportFailure_EndsWithoutThrowing(bool disposedException)
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var session = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(
            new ReadFaultingStream(server, disposedException),
            new IpcFrameCodec(),
            session,
            new SystemClock());

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, session.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that exceeding the inbound message rate closes the connection without dispatching the excess frame.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimit_ClosesAfterLimit()
    {
        (int maxMessages, _) = ReadPrivateIpcLimitsFixture();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var clock = new FakeClock();
        var session = new FakeAdapterIpcSession();
        var codec = new IpcFrameCodec();
        var connection = new AdapterIpcConnection(server, codec, session, clock);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // acknowledgement
        await ReadOneFrameAsync(client, codec); // resynchronize request

        byte[] frame = codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal));
        int acceptedPostHandshakeMessages = maxMessages - 1;
        for (int i = 0; i < acceptedPostHandshakeMessages + 1; i++)
        {
            await client.WriteAsync(frame);
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(acceptedPostHandshakeMessages, session.HandledFrames.Count);
        Assert.Equal(1, session.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a rate-limit close unblocks an outbound writer that is stuck in transport I/O.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimit_ForceClosesBlockedWriter()
    {
        (int maxMessages, _) = ReadPrivateIpcLimitsFixture();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server);
        var session = new FakeAdapterIpcSession();
        var codec = new IpcFrameCodec();
        var connection = new AdapterIpcConnection(blockingStream, codec, session, new FakeClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        byte[] frame = codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal));
        for (int i = 0; i < maxMessages; i++)
        {
            await client.WriteAsync(frame);
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(maxMessages - 1, session.HandledFrames.Count);
        Assert.Equal(1, session.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that messages older than the rolling rate window stop counting against a connection.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimit_AllowsMessagesAfterWindowExpires()
    {
        (int maxMessages, TimeSpan window) = ReadPrivateIpcLimitsFixture();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var clock = new FakeClock();
        var session = new FakeAdapterIpcSession();
        var codec = new IpcFrameCodec();
        var connection = new AdapterIpcConnection(server, codec, session, clock);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // acknowledgement
        await ReadOneFrameAsync(client, codec); // resynchronize request

        byte[] frame = codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal));
        int acceptedPostHandshakeMessages = maxMessages - 1;
        for (int i = 0; i < acceptedPostHandshakeMessages; i++)
        {
            await client.WriteAsync(frame);
        }

        await WaitUntilAsync(() => session.HandledFrames.Count == acceptedPostHandshakeMessages, runTask);
        clock.Advance(window + TimeSpan.FromTicks(1));
        await client.WriteAsync(frame);
        await WaitUntilAsync(() => session.HandledFrames.Count == maxMessages, runTask);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a timestamp exactly one window old remains rate-limited at the boundary.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimit_ExactWindowBoundaryRemainsLimited()
    {
        (int maxMessages, TimeSpan window) = ReadPrivateIpcLimitsFixture();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var clock = new FakeClock();
        var session = new FakeAdapterIpcSession();
        var codec = new IpcFrameCodec();
        var connection = new AdapterIpcConnection(server, codec, session, clock);
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // acknowledgement
        await ReadOneFrameAsync(client, codec); // resynchronize request

        byte[] frame = codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal));
        int acceptedPostHandshakeMessages = maxMessages - 1;
        for (int i = 0; i < acceptedPostHandshakeMessages; i++)
        {
            await client.WriteAsync(frame);
        }

        await WaitUntilAsync(() => session.HandledFrames.Count == acceptedPostHandshakeMessages, runTask);
        clock.Advance(window);
        await client.WriteAsync(frame);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(acceptedPostHandshakeMessages, session.HandledFrames.Count);
        Assert.Equal(1, session.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that cancellation closes a transport before awaiting a writer blocked in I/O.</summary>
    [Fact]
    public async Task RunAsync_CancellationStopsBlockedWriterAndNotifiesDisconnect()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server);
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(blockingStream, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));
        using var cancellation = new CancellationTokenSource();

        Task runTask = connection.RunAsync(cancellation.Token);
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            blockingStream.Release();
            client.Dispose();
            try
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                // The caller cancellation is the expected result of this test.
            }
        }

        Assert.Equal(1, fakeSession.DisconnectedCalls);
    }

    /// <summary>Verifies that an event-listening intent is refused before the session has anything to prepare it from.</summary>
    [Fact]
    public void TrySendListenEvent_SessionRefuses_ReturnsFalse()
    {
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), new FakeAdapterIpcSession(), new SystemClock());

        bool enqueued = connection.TrySendListenEvent(1, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(0UL, correlationId);
    }

    /// <summary>Verifies that a sample-read intent is refused before the session has anything to prepare it from.</summary>
    [Fact]
    public void TrySendReadSample_SessionRefuses_ReturnsFalse()
    {
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), new FakeAdapterIpcSession(), new SystemClock());

        bool enqueued = connection.TrySendReadSample(1, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(0UL, correlationId);
    }

    /// <summary>Verifies that a cancellation is refused before the session has anything to prepare it from.</summary>
    [Fact]
    public void TryCancel_SessionRefuses_ReturnsFalse()
    {
        var fakeSession = new FakeAdapterIpcSession { PrepareCancelReturnsNull = true };
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), fakeSession, new SystemClock());

        Assert.False(connection.TryCancel(1));
    }

    /// <summary>Verifies that the bounded outbound queue refuses further intents once it is full, rather than growing without limit.</summary>
    [Fact]
    public void TrySendListenEvent_QueueFull_ReturnsFalse()
    {
        var fakeSession = new FakeAdapterIpcSession { ListenEventResult = new IpcListenEventMessage(1, 1) };
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), fakeSession, new SystemClock());
        for (int i = 0; i < Constants.MaxIpcQueuedMessages; i++)
        {
            Assert.True(connection.TrySendListenEvent(1, out _));
        }

        bool enqueued = connection.TrySendListenEvent(1, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(0UL, correlationId);
    }

    /// <summary>Verifies that a full outbound queue refuses a sample-read intent without exposing an unsent correlation id.</summary>
    [Fact]
    public void TrySendReadSample_QueueFull_ReturnsFalseWithoutCorrelationId()
    {
        var fakeSession = new FakeAdapterIpcSession { ReadSampleResult = new IpcReadSampleMessage(1, 1) };
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), fakeSession, new SystemClock());
        for (int i = 0; i < Constants.MaxIpcQueuedMessages; i++)
        {
            Assert.True(connection.TrySendReadSample(1, out _));
        }

        bool enqueued = connection.TrySendReadSample(1, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(0UL, correlationId);
    }

    /// <summary>Verifies that an intent attempted after connection teardown reports no queued correlation.</summary>
    [Fact]
    public async Task TrySendIntents_AfterConnectionEnds_ReturnFalseWithoutCorrelationIds()
    {
        var fakeSession = new FakeAdapterIpcSession
        {
            ListenEventResult = new IpcListenEventMessage(1, 1),
            ReadSampleResult = new IpcReadSampleMessage(2, 1),
        };
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), fakeSession, new SystemClock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.RunAsync(cancellation.Token))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(connection.TrySendListenEvent(1, out ulong eventCorrelationId));
        Assert.False(connection.TrySendReadSample(1, out ulong sampleCorrelationId));
        Assert.Equal(0UL, eventCorrelationId);
        Assert.Equal(0UL, sampleCorrelationId);
    }

    /// <summary>
    /// Verifies that the outbound channel is already closed to new writes by the time the session is
    /// notified of disconnection: a send prepared and attempted from inside the session's own
    /// <c>HandleDisconnected</c> callback must fail rather than land in the channel after the
    /// generation's unavailability could already be observed by an availability subscriber.
    /// </summary>
    [Fact]
    public async Task RunAsync_Disconnecting_ClosesOutboundQueueBeforeSessionObservesDisconnect()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            ListenEventResult = new IpcListenEventMessage(1, 42),
            ReadSampleResult = new IpcReadSampleMessage(2, 42),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        bool listenAcceptedDuringDisconnect = true;
        bool readSampleAcceptedDuringDisconnect = true;
        bool cancelAcceptedDuringDisconnect = true;
        fakeSession.OnDisconnected = () =>
        {
            listenAcceptedDuringDisconnect = connection.TrySendListenEvent(42, out _);
            readSampleAcceptedDuringDisconnect = connection.TrySendReadSample(42, out _);
            cancelAcceptedDuringDisconnect = connection.TryCancel(3);
        };
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, fakeSession.DisconnectedCalls);
        Assert.False(listenAcceptedDuringDisconnect);
        Assert.False(readSampleAcceptedDuringDisconnect);
        Assert.False(cancelAcceptedDuringDisconnect);
    }

    /// <summary>
    /// Verifies that closing the outbound channel before notifying the session of disconnection does
    /// not turn the connection's graceful shutdown into a race that drops a frame legitimately
    /// accepted just before teardown began: a send that succeeded while the connection was still
    /// active must still reach the peer during the graceful drain that follows a protocol close.
    /// </summary>
    [Fact]
    public async Task RunAsync_MessageAcceptedBeforeGracefulClose_IsStillDeliveredDuringDrain()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            FrameOutcome = AdapterIpcOutcome.Close,
            ListenEventResult = new IpcListenEventMessage(9, 42),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        bool enqueued = connection.TrySendListenEvent(42, out ulong correlationId);
        await client.WriteAsync(codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal)));
        IpcMessage delivered = await ReadOneFrameAsync(client, codec);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(enqueued);
        var listenEvent = Assert.IsType<IpcListenEventMessage>(delivered);
        Assert.Equal(9UL, correlationId);
        Assert.Equal(42u, listenEvent.EventKey);
        Assert.Equal(1, fakeSession.DisconnectedCalls);
        client.Dispose();
    }

    // ---- Pairing display ----

    /// <summary>Verifies that a pairing-display request is refused before the session has anything to prepare it from.</summary>
    [Fact]
    public void TrySendPairingDisplay_SessionRefuses_ReturnsFalse()
    {
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), new FakeAdapterIpcSession(), new SystemClock());

        bool enqueued = connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(0UL, correlationId);
    }

    /// <summary>Verifies that an attempts-exhausted notification is refused before the session has anything to prepare it from.</summary>
    [Fact]
    public void TrySendPairingAttemptsExhausted_SessionRefuses_ReturnsFalse()
    {
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), new FakeAdapterIpcSession(), new SystemClock());

        Assert.False(connection.TrySendPairingAttemptsExhausted());
    }

    /// <summary>Verifies that a queued pairing-display request is actually written to the peer once connected.</summary>
    [Fact]
    public async Task TrySendPairingDisplay_Connected_DeliversFrameToPeer()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        bool enqueued = connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId);
        IpcMessage delivered = await ReadOneFrameAsync(client, codec);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(enqueued);
        Assert.Equal(9UL, correlationId);
        var pairingDisplay = Assert.IsType<IpcPairingDisplayMessage>(delivered);
        Assert.Equal("123456", pairingDisplay.Code);
        Assert.Equal(PairingDisplayMode.Initial, pairingDisplay.Mode);
    }

    /// <summary>Verifies that a queued attempts-exhausted notification is actually written to the peer once connected.</summary>
    [Fact]
    public async Task TrySendPairingAttemptsExhausted_Connected_DeliversFrameToPeer()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession { PairingAttemptsExhaustedResult = new IpcPairingAttemptsExhaustedMessage(0) };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        bool enqueued = connection.TrySendPairingAttemptsExhausted();
        IpcMessage delivered = await ReadOneFrameAsync(client, codec);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(enqueued);
        Assert.IsType<IpcPairingAttemptsExhaustedMessage>(delivered);
    }

    /// <summary>
    /// Verifies that the bounded outbound queue refuses a pairing-display request once full, and that
    /// the session is told to withdraw the correlation id it had already registered as pending -- so a
    /// later stray acknowledgement for a request that was never actually sent cannot be mistaken for a
    /// legitimate reply.
    /// </summary>
    [Fact]
    public void TrySendPairingDisplay_QueueFull_ReturnsFalseAndCancelsPendingCorrelation()
    {
        var fakeSession = new FakeAdapterIpcSession { PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial) };
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), fakeSession, new SystemClock());
        for (int i = 0; i < Constants.MaxIpcQueuedMessages; i++)
        {
            Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out _));
        }

        bool enqueued = connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId);

        Assert.False(enqueued);
        Assert.Equal(0UL, correlationId);
        Assert.Contains(9UL, fakeSession.CancelledPendingPairingDisplayCorrelationIds);
    }

    /// <summary>
    /// Verifies that a rate-limit close -- a connection ending via forced writer closure rather than an
    /// ordinary peer disconnect -- still resolves an outstanding acknowledgement wait as not accepted
    /// instead of leaving it hanging.
    /// </summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_ForceClosedBlockedWriter_ReturnsFalse()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server, writesBeforeBlocking: 2);
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
        };
        var connection = new AdapterIpcConnection(blockingStream, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // acknowledgement
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        Task<bool> awaitTask = connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromSeconds(5), CancellationToken.None);
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await client.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }); // malformed frame length forces closure

        bool result = await awaitTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Empty(fakeSession.PreparedCancelCorrelationIds);
        blockingStream.Release();
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that an acknowledgement arriving from the peer resolves the matching pending wait with its accepted value.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AwaitPairingDisplayAckAsync_AckArrives_ReturnsAcceptedValue(bool accepted)
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
            PairingDisplayAckResult = accepted,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself
        Task<bool> awaitTask = connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromSeconds(5), CancellationToken.None);
        await client.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(correlationId, accepted)));

        bool result = await awaitTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(accepted, result);
        Assert.Single(fakeSession.HandledPairingDisplayAcks);
        // The unconditional cleanup in AwaitPairingDisplayAckAsync's finally block still runs on this
        // success path; it is a harmless no-op against the real session (HandlePairingDisplayAck
        // already removed the entry), which this fake's own unconditional call recording surfaces here.
        Assert.Contains(correlationId, fakeSession.CancelledPendingPairingDisplayCorrelationIds);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that awaiting an acknowledgement for a correlation id that was never sent returns false immediately.</summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_UnknownCorrelationId_ReturnsFalseImmediately()
    {
        var fakeSession = new FakeAdapterIpcSession();
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), fakeSession, new SystemClock());

        bool result = await connection.AwaitPairingDisplayAckAsync(999, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(result);
        Assert.Empty(fakeSession.PreparedCancelCorrelationIds);
    }

    /// <summary>
    /// Verifies that awaiting an acknowledgement that never arrives times out, returns false rather
    /// than hanging, and withdraws the correlation id from the session's own pending set so a
    /// connected-but-never-acknowledging adapter cannot grow that set without bound.
    /// </summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_NoAckArrives_TimesOutAndReturnsFalse()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself

        bool result = await connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromMilliseconds(100), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Contains(correlationId, fakeSession.CancelledPendingPairingDisplayCorrelationIds);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that awaiting an acknowledgement with an already-cancelled token returns false rather
    /// than throwing, and withdraws the correlation id from the session's own pending set the same as
    /// a genuine timeout does.
    /// </summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_Cancelled_ReturnsFalse()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        bool result = await connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromSeconds(5), cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Contains(correlationId, fakeSession.CancelledPendingPairingDisplayCorrelationIds);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a connection ending while an acknowledgement wait is outstanding resolves it as not accepted rather than hanging forever.</summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_ConnectionEndsWhileWaiting_ReturnsFalse()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself
        Task<bool> awaitTask = connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromSeconds(5), CancellationToken.None);
        client.Dispose();

        bool result = await awaitTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Empty(fakeSession.PreparedCancelCorrelationIds);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that an acknowledgement the session does not recognize as valid (a stale or mismatched
    /// correlation, or a superseded connection generation) never resolves the pending wait -- the wait
    /// times out on its own bound instead of resolving with a value the session never approved.
    /// </summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_SessionRejectsAck_TimesOutRatherThanResolving()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
            PairingDisplayAckResult = null,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself
        Task<bool> awaitTask = connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromMilliseconds(200), CancellationToken.None);
        await client.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(correlationId, true)));

        bool result = await awaitTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Single(fakeSession.HandledPairingDisplayAcks);
        Assert.Contains(correlationId, fakeSession.PreparedCancelCorrelationIds);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that an explicit accepted or rejected acknowledgement never triggers a remote
    /// cancellation: the adapter has already executed the display request by the time either
    /// acknowledgement arrives, so there is nothing left to cancel.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AwaitPairingDisplayAckAsync_AckArrives_DoesNotCancelRemotely(bool accepted)
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
            PairingDisplayAckResult = accepted,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself
        Task<bool> awaitTask = connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromSeconds(5), CancellationToken.None);
        await client.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(correlationId, accepted)));

        bool result = await awaitTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(accepted, result);
        Assert.Empty(fakeSession.PreparedCancelCorrelationIds);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a timed-out acknowledgement wait sends a remote cancellation for the exact
    /// correlation id, so a queued Skyrim-side display cannot appear after the host has already
    /// rolled back and reported the challenge unavailable.
    /// </summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_NoAckArrives_SendsRemoteCancellation()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself

        bool result = await connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromMilliseconds(100), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Contains(correlationId, fakeSession.PreparedCancelCorrelationIds);
        var cancelFrame = Assert.IsType<IpcCancelMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(correlationId, cancelFrame.CorrelationId);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a caller-cancelled acknowledgement wait sends a remote cancellation for the exact
    /// correlation id, the same as a timeout does.
    /// </summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_Cancelled_SendsRemoteCancellation()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        bool result = await connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromSeconds(5), cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Contains(correlationId, fakeSession.PreparedCancelCorrelationIds);
        var cancelFrame = Assert.IsType<IpcCancelMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(correlationId, cancelFrame.CorrelationId);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a timed-out acknowledgement wait still returns false when the remote
    /// cancellation itself cannot be enqueued (a full outbound queue), rather than letting a
    /// best-effort cleanup failure change the already-decided timeout result.
    /// </summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_Timeout_CancellationEnqueueFails_StillReturnsFalse()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
            PrepareCancelReturnsNull = true,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself

        bool result = await connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromMilliseconds(100), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Contains(correlationId, fakeSession.PreparedCancelCorrelationIds);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a timed-out acknowledgement wait still returns false when sending the remote
    /// cancellation throws, containing the failure the same as a controlled enqueue failure.
    /// </summary>
    [Fact]
    public async Task AwaitPairingDisplayAckAsync_Timeout_CancellationSendThrows_ContainedAndStillReturnsFalse()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
            ThrowOnPrepareCancel = true,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself

        bool result = await connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromMilliseconds(100), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- Trust-admin requests ----

    /// <summary>Verifies that a received trust-admin request is forwarded to the session and its formatted result is sent back correlated.</summary>
    [Fact]
    public async Task RunAsync_TrustAdminRequest_ForwardsToSessionAndSendsResultBackCorrelated()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession { TrustAdminRequestResult = "Revoked client 12345 (My PC)." };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        var request = new IpcTrustAdminRequestMessage(9, TrustAdminOperation.Revoke, ShortId: "12345");
        await client.WriteAsync(codec.Encode(request));
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));

        Assert.Equal(9UL, result.CorrelationId);
        Assert.Equal("Revoked client 12345 (My PC).", result.ResultText);
        Assert.Single(fakeSession.HandledTrustAdminRequests);
        Assert.Equal(request, fakeSession.HandledTrustAdminRequests[0]);
        Assert.Empty(fakeSession.HandledFrames);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that the read loop keeps serving further frames after handling a trust-admin request.</summary>
    [Fact]
    public async Task RunAsync_TrustAdminRequestThenAnotherMessage_BothAreProcessed()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            TrustAdminRequestResult = "ok",
            FrameOutcome = AdapterIpcOutcome.None,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));
        await ReadOneFrameAsync(client, codec); // the trust-admin result
        await client.WriteAsync(codec.Encode(new IpcCancelMessage(5)));

        // Waits for the read loop to actually record this frame, not a fixed delay: this proves
        // the loop kept serving further frames after the trust-admin request, which is exactly
        // what this test exists to show.
        await WaitUntilAsync(() => fakeSession.HandledFrames.Count == 1, runTask);

        Assert.Single(fakeSession.HandledTrustAdminRequests);
        Assert.Single(fakeSession.HandledFrames);
        Assert.IsType<IpcCancelMessage>(fakeSession.HandledFrames[0]);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that the read loop is not blocked by a trust-admin request whose dispatch has not
    /// yet finished: a pairing-display acknowledgement received while that dispatch is still
    /// outstanding is processed immediately, and the trust-admin request's own correlated result
    /// still arrives once its dispatch is later released. This is the regression proof for the
    /// reason this concept stopped awaiting the dispatch inline on the read loop.
    /// </summary>
    [Fact]
    public async Task RunAsync_TrustAdminRequestPending_DoesNotBlockPairingDisplayAckProcessing()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var dispatchGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeSession = new FakeAdapterIpcSession
        {
            HandleTrustAdminRequestOverride = (_, _) => dispatchGate.Task,
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
            PairingDisplayAckResult = true,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));

        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself
        Task<bool> ackTask = connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromSeconds(5), CancellationToken.None);
        await client.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(correlationId, true)));

        // If the trust-admin request's still-pending dispatch blocked the read loop, this would time
        // out instead of observing the ack that was written to the stream after it.
        bool ackResult = await ackTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(ackResult);

        dispatchGate.SetResult("ok");
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(1UL, result.CorrelationId);
        Assert.Equal("ok", result.ResultText);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a trust-admin handler which blocks the calling thread synchronously -- before
    /// its own returned <see cref="Task"/> is even created, rather than one that merely returns an
    /// already-incomplete <see cref="Task"/> -- still does not block the private IPC read loop from
    /// serving an unrelated pairing-display acknowledgement. This is the deterministic regression
    /// proof for the asynchronous scheduling boundary at the top of
    /// <c>AdapterIpcConnection.RunTrustAdminRequestAsync</c> (private, so not link-eligible from
    /// here): the earlier
    /// <see cref="RunAsync_TrustAdminRequestPending_DoesNotBlockPairingDisplayAckProcessing"/> test
    /// cannot detect a missing boundary because its fake returns a pre-existing incomplete task,
    /// which naturally yields on its own regardless of that boundary.
    /// </summary>
    [Fact]
    public async Task RunAsync_TrustAdminHandlerBlocksSynchronously_StillProcessesPairingDisplayAckWhileBlocked()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        using var handlerEntered = new ManualResetEventSlim(false);
        using var releaseHandler = new ManualResetEventSlim(false);
        var fakeSession = new FakeAdapterIpcSession
        {
            HandleTrustAdminRequestOverride = (_, _) =>
            {
                handlerEntered.Set();
                Assert.True(releaseHandler.Wait(TimeSpan.FromSeconds(5)));
                return Task.FromResult("ok");
            },
            PairingDisplayResult = new IpcPairingDisplayMessage(9, "123456", PairingDisplayMode.Initial),
            PairingDisplayAckResult = true,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));
        Assert.True(handlerEntered.Wait(TimeSpan.FromSeconds(5)));

        // The handler is synchronously blocked inside its own dispatch right now. If that dispatch
        // ran inline on the read loop instead of past an explicit scheduling boundary, the read loop
        // could not still be free to admit and process this unrelated pairing-display request/ack.
        Assert.True(connection.TrySendPairingDisplay("123456", PairingDisplayMode.Initial, out ulong correlationId));
        await ReadOneFrameAsync(client, codec); // the display request itself
        Task<bool> ackTask = connection.AwaitPairingDisplayAckAsync(correlationId, TimeSpan.FromSeconds(5), CancellationToken.None);
        await client.WriteAsync(codec.Encode(new IpcPairingDisplayAckMessage(correlationId, true)));

        bool ackResult = await ackTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(ackResult);

        releaseHandler.Set();
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(1UL, result.CorrelationId);
        Assert.Equal("ok", result.ResultText);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a request beyond <see cref="Constants.MaxPendingTrustAdminRequests"/> is
    /// rejected with a controlled result rather than admitted, and that completing one already-
    /// admitted request frees its slot for a later one.
    /// </summary>
    [Fact]
    public async Task RunAsync_TrustAdminRequestsAtCapacity_RejectsNextWithControlledReplyAndFreesSlotOnCompletion()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var gates = new Dictionary<ulong, TaskCompletionSource<string>>();
        for (ulong i = 1; i <= (ulong)Constants.MaxPendingTrustAdminRequests; i++)
        {
            gates[i] = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var fakeSession = new FakeAdapterIpcSession
        {
            HandleTrustAdminRequestOverride = (request, _) => gates[request.CorrelationId].Task,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        foreach (ulong correlationId in gates.Keys)
        {
            await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(correlationId, TrustAdminOperation.Help)));
        }

        // Every slot is now occupied by a request whose gate is still unreleased; a request beyond
        // the bound must be rejected immediately rather than admitted or left hanging.
        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(999, TrustAdminOperation.Help)));
        var rejected = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(999UL, rejected.CorrelationId);

        gates[1].SetResult("first");
        var freed = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(1UL, freed.CorrelationId);

        foreach ((ulong correlationId, TaskCompletionSource<string> gate) in gates)
        {
            if (correlationId != 1)
            {
                gate.SetResult("done");
            }
        }

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a trust-admin request whose correlation id matches one already admitted and
    /// still outstanding is rejected as a protocol violation and closes the connection, the same as
    /// an unrecognized message kind.
    /// </summary>
    [Fact]
    public async Task RunAsync_DuplicateTrustAdminCorrelationId_RejectsAndCloses()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeSession = new FakeAdapterIpcSession { HandleTrustAdminRequestOverride = (_, _) => gate.Task };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(7, TrustAdminOperation.Help)));
        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(7, TrustAdminOperation.Help)));

        var reject = Assert.IsType<IpcRejectMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(7UL, reject.CorrelationId);
        Assert.Equal(IpcRejectReason.DuplicateTrustAdminCorrelationId, reject.Reason);

        gate.SetResult("unused");
        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that independent trust-admin requests each resolve by their own exact correlation id, out of order.</summary>
    [Fact]
    public async Task RunAsync_MultipleTrustAdminRequests_CompleteOutOfOrder()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var firstGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeSession = new FakeAdapterIpcSession
        {
            HandleTrustAdminRequestOverride = (request, _) => request.CorrelationId == 1 ? firstGate.Task : secondGate.Task,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));
        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(2, TrustAdminOperation.Help)));

        // Resolve the second request first: the still-pending first request must not block it.
        secondGate.SetResult("second");
        var secondResult = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(2UL, secondResult.CorrelationId);

        firstGate.SetResult("first");
        var firstResult = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(1UL, firstResult.CorrelationId);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that an inbound cancellation for a trust-admin request's exact correlation id
    /// cancels that request's own dispatch, dropping it silently -- no reply is ever sent for it --
    /// without disturbing a different, unrelated request.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancelMessage_CancelsMatchingTrustAdminRequestAndDropsItSilently()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            HandleTrustAdminRequestOverride = (request, cancellationToken) =>
            {
                if (request.CorrelationId != 3)
                {
                    return Task.FromResult("ok");
                }

                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
                return tcs.Task;
            },
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(3, TrustAdminOperation.Help)));

        // Waits for the session to actually be entered. DispatchTrustAdminRequest always
        // registers the pending dispatch before RunTrustAdminRequestAsync's own explicit yield
        // lets it call the session (see that method's documentation), so this list update proves
        // admission already happened -- the Cancel below is guaranteed something to actually
        // cancel, rather than racing admission and silently relying on teardown's own blanket
        // cancellation to cover for it.
        await WaitUntilAsync(() => fakeSession.HandledTrustAdminRequests.Count == 1, runTask);
        await client.WriteAsync(codec.Encode(new IpcCancelMessage(3)));

        // A later, unrelated request still completes normally: cancellation reached only the exact
        // correlation id it named.
        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(4, TrustAdminOperation.Help)));
        var onlyResult = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(4UL, onlyResult.CorrelationId);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a cancellation naming a correlation id whose trust-admin request already
    /// completed is a harmless no-op: it neither throws nor disturbs a later, unrelated request.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancelMessage_AfterTrustAdminRequestAlreadyCompleted_IsHarmlessNoOp()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession { TrustAdminRequestResult = "done" };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));
        await ReadOneFrameAsync(client, codec); // already completed and removed by the time this returns

        await client.WriteAsync(codec.Encode(new IpcCancelMessage(1)));
        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(2, TrustAdminOperation.Help)));
        var secondResult = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(2UL, secondResult.CorrelationId);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a cancellation naming a correlation id that was never admitted as a trust-admin
    /// request -- not merely one already completed -- is a harmless no-op: it neither disturbs a
    /// genuinely outstanding, differently correlated request nor closes the connection as a protocol
    /// violation, matching an unrecognized correlation id's treatment everywhere else in this
    /// contract.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancelMessage_UnknownTrustAdminCorrelationId_IsHarmlessNoOpAndDoesNotDisturbAnOutstandingRequest()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeSession = new FakeAdapterIpcSession { HandleTrustAdminRequestOverride = (_, _) => gate.Task };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));

        // 999 was never admitted at all -- not this connection's own correlation id 1, and not a
        // stale id from an already-completed request either.
        await client.WriteAsync(codec.Encode(new IpcCancelMessage(999)));

        gate.SetResult("still outstanding");
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(1UL, result.CorrelationId);
        Assert.Equal("still outstanding", result.ResultText);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that disconnecting while a trust-admin request's dispatch is still outstanding
    /// cancels it, so it does not keep running unbounded past this connection's own teardown.
    /// </summary>
    [Fact]
    public async Task RunAsync_DisconnectWhileTrustAdminRequestPending_CancelsIt()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var cancelledSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeSession = new FakeAdapterIpcSession
        {
            HandleTrustAdminRequestOverride = (_, cancellationToken) =>
            {
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() =>
                {
                    tcs.TrySetCanceled(cancellationToken);
                    cancelledSignal.TrySetResult();
                });
                return tcs.Task;
            },
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));

        // Waits for the session to actually be entered: DispatchTrustAdminRequest always
        // registers the pending dispatch before RunTrustAdminRequestAsync's own explicit yield
        // lets it call the session, so this list update proves admission already happened and
        // disconnect below has an outstanding dispatch to cancel.
        await WaitUntilAsync(() => fakeSession.HandledTrustAdminRequests.Count == 1, runTask);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Teardown now awaits every outstanding trust-admin dispatch actually finishing (bounded by
        // Constants.TrustAdminTeardownDrainTimeout) before RunAsync itself completes, not merely
        // requesting cancellation and moving on: this handler observes cancellation synchronously as
        // part of that Cancel() call, so by the time runTask is done, cancelledSignal is already set.
        Assert.True(cancelledSignal.Task.IsCompleted);
    }

    /// <summary>
    /// Verifies that a trust-admin handler which ignores its <see cref="CancellationToken"/> --
    /// never observing it, never completing -- does not block this connection's teardown past
    /// <see cref="Constants.TrustAdminTeardownDrainTimeout"/>: teardown still completes, abandoning
    /// the still-running dispatch rather than waiting for it forever.
    /// </summary>
    [Fact]
    public async Task RunAsync_DisconnectWhileTrustAdminHandlerIgnoresCancellation_TeardownStillCompletesWithinBound()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var neverCompletes = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeSession = new FakeAdapterIpcSession
        {
            // Deliberately does not register on cancellationToken at all: this Task never completes
            // on its own and never observes cancellation, modeling a handler that violates
            // IAdapterTrustAdminRequestHandler.HandleAsync's documented cancellation contract.
            HandleTrustAdminRequestOverride = (_, _) => neverCompletes.Task,
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));

        // Waits for the session to actually be entered: DispatchTrustAdminRequest always
        // registers the pending dispatch before RunTrustAdminRequestAsync's own explicit yield
        // lets it call the session, so this list update proves admission already happened and
        // disconnect below has an outstanding dispatch to cancel.
        await WaitUntilAsync(() => fakeSession.HandledTrustAdminRequests.Count == 1, runTask);

        client.Dispose();
        // A generous margin over TrustAdminTeardownDrainTimeout: teardown must complete on its own
        // bound, not hang until this outer timeout forces a test failure instead.
        await runTask.WaitAsync(Constants.TrustAdminTeardownDrainTimeout + TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a trust-admin handler throwing an exception other than
    /// <see cref="OperationCanceledException"/> -- violating <see cref="IAdapterTrustAdminRequestHandler"/>'s
    /// documented contract of sanitizing every expected failure into a formatted result -- is
    /// contained rather than left to fault the request's dispatch task or crash the connection: a
    /// controlled result reaches the adapter and the connection keeps serving other requests.
    /// </summary>
    [Fact]
    public async Task RunAsync_TrustAdminHandlerThrows_SendsControlledResultAndKeepsServing()
    {
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var codec = new IpcFrameCodec();
        var fakeSession = new FakeAdapterIpcSession
        {
            HandleTrustAdminRequestOverride = (_, _) => throw new InvalidOperationException("handler bug"),
        };
        var connection = new AdapterIpcConnection(server, codec, fakeSession, new SystemClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await ReadOneFrameAsync(client, codec); // ack
        await ReadOneFrameAsync(client, codec); // resynchronize request

        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help)));
        var result = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(1UL, result.CorrelationId);
        Assert.False(string.IsNullOrEmpty(result.ResultText));

        // The connection is still alive and able to serve a second request, proving the earlier
        // handler exception never faulted anything this connection depends on.
        await client.WriteAsync(codec.Encode(new IpcTrustAdminRequestMessage(2, TrustAdminOperation.Help)));
        var secondResult = Assert.IsType<IpcTrustAdminResultMessage>(await ReadOneFrameAsync(client, codec));
        Assert.Equal(2UL, secondResult.CorrelationId);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Waits for a terminal connection to dispose a blocked writer and cleans up after a failed assertion.</summary>
    /// <param name="runTask">The connection task under test.</param>
    /// <param name="blockingStream">The stream expected to be force-disposed.</param>
    /// <param name="client">The peer stream to dispose after the assertion.</param>
    private static async Task AssertForcedClosureAsync(Task runTask, BlockingWriteStream blockingStream, Stream client)
    {
        try
        {
            await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(blockingStream.IsDisposed);
        }
        finally
        {
            blockingStream.Release();
            client.Dispose();
            try
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception) when (runTask.IsCompleted)
            {
                // Preserve the original assertion or timeout result from the main await.
            }
        }
    }

    /// <summary>
    /// Creates a connected pair of loopback-socket streams for realistic byte-level I/O tests. A
    /// loopback socket matches the production listener and keeps the connection under test focused
    /// on its <see cref="Stream"/> boundary.
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

    /// <summary>Waits for a connection-side observation while surfacing an early connection failure.</summary>
    /// <param name="condition">The condition to poll.</param>
    /// <param name="guardTask">The connection task whose unexpected completion should fail the wait.</param>
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

    /// <summary>A stream whose asynchronous writes wait until release or disposal after an optional prefix.</summary>
    private sealed class BlockingWriteStream : Stream
    {
        /// <summary>The stream whose reads and synchronous operations are delegated.</summary>
        private readonly Stream inner;

        /// <summary>Completes when an asynchronous write begins.</summary>
        private readonly TaskCompletionSource writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Whether the stream has been disposed.</summary>
        private int disposed;

        /// <summary>Completes the blocked write when the stream is released or disposed.</summary>
        private readonly TaskCompletionSource writeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The number of initial writes that should delegate normally before blocking.</summary>
        private int writesBeforeBlocking;

        /// <summary>Creates a wrapper around the connected server stream.</summary>
        /// <param name="inner">The connected stream whose reads are delegated.</param>
        /// <param name="writesBeforeBlocking">The number of initial writes to delegate before blocking.</param>
        public BlockingWriteStream(Stream inner, int writesBeforeBlocking = 0)
        {
            this.inner = inner;
            this.writesBeforeBlocking = writesBeforeBlocking;
        }

        /// <summary>Gets a task that completes when the first write begins.</summary>
        public Task WriteStarted => writeStarted.Task;

        /// <summary>Gets whether this stream has been disposed.</summary>
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        /// <inheritdoc/>
        public override bool CanRead => inner.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush() => inner.Flush();

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref writesBeforeBlocking) >= 0)
            {
                return inner.WriteAsync(buffer, cancellationToken);
            }

            writeStarted.TrySetResult();
            return new ValueTask(writeRelease.Task);
        }

        /// <summary>Releases the blocked write so a failed test can clean up safely.</summary>
        public void Release() => writeRelease.TrySetResult();

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Exchange(ref disposed, 1);
                Release();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A stream that fails every asynchronous read with a selected transport exception.</summary>
    private sealed class ReadFaultingStream : Stream
    {
        /// <summary>The stream whose disposal is owned by this wrapper.</summary>
        private readonly Stream inner;

        /// <summary>Whether reads should throw <see cref="ObjectDisposedException"/> instead of <see cref="IOException"/>.</summary>
        private readonly bool disposedException;

        /// <summary>Creates a wrapper that fails asynchronous reads.</summary>
        /// <param name="inner">The connected stream whose lifetime is owned by this wrapper.</param>
        /// <param name="disposedException">Whether to throw <see cref="ObjectDisposedException"/>.</param>
        public ReadFaultingStream(Stream inner, bool disposedException)
        {
            this.inner = inner;
            this.disposedException = disposedException;
        }

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => inner.CanWrite;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush() => inner.Flush();

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => throw CreateReadException();

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(CreateReadException());

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        /// <summary>Creates the configured reader failure.</summary>
        /// <returns>The exception to raise from a read operation.</returns>
        private Exception CreateReadException() => disposedException
            ? new ObjectDisposedException(nameof(ReadFaultingStream))
            : new IOException("Simulated read fault.");

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
