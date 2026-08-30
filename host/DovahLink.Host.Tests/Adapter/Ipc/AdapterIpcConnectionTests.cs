using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.Time;
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
        var fakeSession = new FakeAdapterIpcSession { CancelResult = new IpcCancelMessage(7) };
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
        int acceptedPostHandshakeMessages = Constants.MaxIpcMessagesPerSecond - 1;
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
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var blockingStream = new BlockingWriteStream(server);
        var session = new FakeAdapterIpcSession();
        var codec = new IpcFrameCodec();
        var connection = new AdapterIpcConnection(blockingStream, codec, session, new FakeClock());
        await client.WriteAsync(codec.Encode(new IpcHelloMessage(1, AdapterInstanceId.NewId(), [])));

        Task runTask = connection.RunAsync(CancellationToken.None);
        await blockingStream.WriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        byte[] frame = codec.Encode(new IpcCloseMessage(0, IpcCloseReason.Normal));
        for (int i = 0; i < Constants.MaxIpcMessagesPerSecond; i++)
        {
            await client.WriteAsync(frame);
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Constants.MaxIpcMessagesPerSecond - 1, session.HandledFrames.Count);
        Assert.Equal(1, session.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that messages older than the rolling rate window stop counting against a connection.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimit_AllowsMessagesAfterWindowExpires()
    {
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
        int acceptedPostHandshakeMessages = Constants.MaxIpcMessagesPerSecond - 1;
        for (int i = 0; i < acceptedPostHandshakeMessages; i++)
        {
            await client.WriteAsync(frame);
        }

        await WaitUntilAsync(() => session.HandledFrames.Count == acceptedPostHandshakeMessages, runTask);
        clock.Advance(Constants.IpcMessageRateWindow + TimeSpan.FromTicks(1));
        await client.WriteAsync(frame);
        await WaitUntilAsync(() => session.HandledFrames.Count == Constants.MaxIpcMessagesPerSecond, runTask);

        client.Dispose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a timestamp exactly one window old remains rate-limited at the boundary.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimit_ExactWindowBoundaryRemainsLimited()
    {
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
        int acceptedPostHandshakeMessages = Constants.MaxIpcMessagesPerSecond - 1;
        for (int i = 0; i < acceptedPostHandshakeMessages; i++)
        {
            await client.WriteAsync(frame);
        }

        await WaitUntilAsync(() => session.HandledFrames.Count == acceptedPostHandshakeMessages, runTask);
        clock.Advance(Constants.IpcMessageRateWindow);
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
        var connection = new AdapterIpcConnection(new MemoryStream(), new IpcFrameCodec(), new FakeAdapterIpcSession(), new SystemClock());

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
