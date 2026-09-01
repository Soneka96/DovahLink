using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.Client.Transport;

/// <summary>Tests for <see cref="PublicWebSocketConnection"/>.</summary>
public class PublicWebSocketConnectionTests
{
    /// <summary>Verifies that a valid handshake followed by a text message delivers the payload to the handler.</summary>
    [Fact]
    public async Task RunAsync_ValidHandshakeAndTextMessage_DeliversPayloadToHandler()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        using var cancellation = new CancellationTokenSource();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = new PublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        byte[] payload = Encoding.UTF8.GetBytes("hello");
        await clientWebSocket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 1, runTask);

        Assert.Equal(payload, Assert.Single(handler.ReceivedMessages));

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a peer who never sends a handshake request is disconnected after the deadline without accepting or throwing.</summary>
    [Fact]
    public async Task RunAsync_HandshakeTimeout_EndsWithoutAcceptingOrThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(handshakeTimeout: TimeSpan.FromMilliseconds(200));
        var connection = new PublicWebSocketConnection(server, handler, new SystemClock(), options);

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(0, handler.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a handshake request missing required WebSocket headers ends the connection without accepting or throwing.</summary>
    [Fact]
    public async Task RunAsync_MalformedHandshakeRequest_EndsWithoutAcceptingOrThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = new PublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());

        byte[] request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\n\r\n");
        await client.WriteAsync(request);

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(0, handler.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a handshake request exceeding the configured byte bound ends the connection without an unbounded buffer.</summary>
    [Fact]
    public async Task RunAsync_OversizedHandshakeRequest_EndsWithoutAcceptingOrThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(maxHandshakeRequestBytes: 32);
        var connection = new PublicWebSocketConnection(server, handler, new SystemClock(), options);

        // 40 bytes with no "\r\n\r\n" terminator, exceeding the 32-byte bound before headers complete.
        await client.WriteAsync(Encoding.ASCII.GetBytes(new string('a', 40)));

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        client.Dispose();
    }

    /// <summary>Verifies that cancelling before the peer ever sends a handshake request propagates as cancellation without ever notifying the handler of a disconnection.</summary>
    [Fact]
    public async Task RunAsync_CancelledDuringHandshake_ThrowsAndNeverNotifiesDisconnected()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = new PublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        using var cancellation = new CancellationTokenSource();

        Task runTask = connection.RunAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, handler.DisconnectedCalls);
        Assert.Empty(handler.ReceivedMessages);
        client.Dispose();
    }

    /// <summary>Verifies that a peer disconnecting mid-handshake ends the connection cleanly.</summary>
    [Fact]
    public async Task RunAsync_PeerDisconnectsDuringHandshake_EndsWithoutAcceptingOrThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = new PublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());

        await client.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n"));
        client.Dispose();

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
    }

    /// <summary>Verifies that a silent peer is disconnected once the idle keep-alive deadline elapses.</summary>
    [Fact]
    public async Task RunAsync_IdleTimeoutWithSilentPeer_ForcesClosureAndNotifiesDisconnected()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(
            idleTimeout: TimeSpan.FromMilliseconds(200), keepAlivePongTimeout: TimeSpan.FromMilliseconds(200));
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The client never reads, so it can never observe or reply to the server's keep-alive ping.
        await handler.Disconnected.WaitAsync(TimeSpan.FromSeconds(5));
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        listener.Stop();
    }

    /// <summary>Verifies that an actively-reading peer keeps the connection healthy past the idle deadline via WebSocket-level keep-alive.</summary>
    [Fact]
    public async Task RunAsync_ActivePeerPastIdleTimeout_StaysHealthy()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(
            idleTimeout: TimeSpan.FromMilliseconds(100), keepAlivePongTimeout: TimeSpan.FromMilliseconds(100));
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        Task clientReadLoop = RunClientReadLoopAsync(clientWebSocket);

        // Several keep-alive cycles' worth of silence, with the client actively able to answer pings.
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.False(runTask.IsCompleted);
        Assert.Equal(0, handler.DisconnectedCalls);

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
        await clientReadLoop.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
    }

    /// <summary>Verifies that a fragmented message exceeding the byte bound is rejected before completion, without delivering it.</summary>
    [Fact]
    public async Task RunAsync_OversizedFragmentedMessage_ClosesBeforeCompletionWithoutDeliveringToHandler()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(maxMessageBytes: 16);
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync(new byte[10], WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await clientWebSocket.SendAsync(new byte[10], WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>Verifies that a message exactly at the maximum byte bound is accepted and delivered, proving the bound rejects only what exceeds it.</summary>
    [Fact]
    public async Task RunAsync_MessageExactlyAtMaxMessageBytes_IsDeliveredToHandler()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(maxMessageBytes: 16);
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync(new byte[16], WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 1, runTask);

        Assert.Equal(16, Assert.Single(handler.ReceivedMessages).Length);

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a raw frame with an invalid reserved bit set -- invalid per RFC 6455 since no extension was negotiated -- closes the connection as a framing violation instead of being delivered.</summary>
    [Fact]
    public async Task RunAsync_InvalidFraming_ClosesAsFramingViolation()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = new PublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);

        await CompleteRawHandshakeAsync(client);

        // FIN + RSV1 + text opcode (0xC1) with RSV1 set is invalid: no compression extension was
        // negotiated during the handshake, so RFC 6455 requires the connection to fail. Masked (as a
        // real client-to-server frame must be) so the violation under test is the reserved bit, not masking.
        byte[] maskKey = [0x11, 0x22, 0x33, 0x44];
        byte[] payload = "hi"u8.ToArray();
        byte[] masked = new byte[payload.Length];
        for (int index = 0; index < payload.Length; index++)
        {
            masked[index] = (byte)(payload[index] ^ maskKey[index % 4]);
        }

        byte[] frame = [0xC1, (byte)(0x80 | payload.Length), .. maskKey, .. masked];
        await client.WriteAsync(frame);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(1, handler.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a binary message is safely closed without ever reaching the handler.</summary>
    [Fact]
    public async Task RunAsync_BinaryMessage_ClosesWithoutDeliveringToHandler()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = new PublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>Verifies that a peer-initiated close ends the connection gracefully and notifies the handler.</summary>
    [Fact]
    public async Task RunAsync_ClientClose_EndsGracefullyAndNotifiesDisconnected()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromSeconds(2));
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>Verifies that cancelling the connection propagates as cancellation and still notifies the handler before returning.</summary>
    [Fact]
    public async Task RunAsync_ExternalCancellation_ThrowsOperationCanceledAndNotifiesDisconnected()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromMilliseconds(200));
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>Verifies that an abrupt peer disconnect (no close handshake) ends the connection without throwing.</summary>
    [Fact]
    public async Task RunAsync_AbruptPeerDisconnect_EndsWithoutThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = new PublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        clientWebSocket.Abort();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>Verifies that concurrent <see cref="IPublicWebSocketConnection.TrySend"/> calls all reach the peer complete and uncorrupted.</summary>
    [Fact]
    public async Task TrySend_ConcurrentCalls_DeliverAllPayloadsIntactToThePeer()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = new PublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        const int messageCount = 25;
        var sentPayloads = new HashSet<string>();
        for (int index = 0; index < messageCount; index++)
        {
            string payload = $"message-{index}";
            sentPayloads.Add(payload);
        }

        await Task.WhenAll(sentPayloads.Select(payload =>
            Task.Run(() => Assert.True(connection.TrySend(Encoding.UTF8.GetBytes(payload))))));

        var receivedPayloads = new HashSet<string>();
        var buffer = new byte[256];
        for (int index = 0; index < messageCount; index++)
        {
            WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.EndOfMessage);
            receivedPayloads.Add(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }

        Assert.Equal(sentPayloads, receivedPayloads);

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a payload exactly at the outbound byte-budget bound is accepted, proving the budget rejects only what exceeds it.</summary>
    [Fact]
    public void TrySend_ExactlyAtOutboundQueueMaxBytes_ReturnsTrue()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 100, outboundQueueMaxBytes: 10);
        var connection = new PublicWebSocketConnection(new MemoryStream(), handler, new SystemClock(), options);

        Assert.True(connection.TrySend(new byte[10]));
    }

    /// <summary>Verifies that a send after the connection has ended returns false instead of throwing.</summary>
    [Fact]
    public async Task TrySend_AfterConnectionEnded_ReturnsFalse()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = new PublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions(handshakeTimeout: TimeSpan.FromMilliseconds(200)));

        // No handshake request is ever sent, so the connection ends via the handshake timeout.
        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(connection.TrySend(new byte[1]));
        client.Dispose();
    }

    /// <summary>Verifies that a send beyond the outbound message-count bound is rejected without blocking, when nothing drains the queue.</summary>
    [Fact]
    public void TrySend_BeyondOutboundQueueMaxMessages_ReturnsFalse()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 3, outboundQueueMaxBytes: 1024);
        var connection = new PublicWebSocketConnection(new MemoryStream(), handler, new SystemClock(), options);

        for (int index = 0; index < 3; index++)
        {
            Assert.True(connection.TrySend(new byte[1]));
        }

        Assert.False(connection.TrySend(new byte[1]));
    }

    /// <summary>Verifies that a send beyond the outbound byte-budget bound is rejected, when nothing drains the queue.</summary>
    [Fact]
    public void TrySend_BeyondOutboundQueueMaxBytes_ReturnsFalse()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 100, outboundQueueMaxBytes: 10);
        var connection = new PublicWebSocketConnection(new MemoryStream(), handler, new SystemClock(), options);

        Assert.True(connection.TrySend(new byte[6]));
        Assert.False(connection.TrySend(new byte[6]));
    }

    /// <summary>Verifies that exceeding the inbound message-rate limit closes the connection before delivering the excess message.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimitExceeded_ClosesConnectionBeforeExcessMessage()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var clock = new FakeClock();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(maxInboundMessagesPerSecond: 3);
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, clock, options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        for (int index = 0; index < 4; index++)
        {
            await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes($"m{index}"), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, handler.ReceivedMessages.Count);
        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>Verifies that the inbound rate limit frees up once the rolling window has passed, so a message sent after the window advances is still delivered.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimitAfterWindowAdvances_AcceptsFurtherMessages()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var clock = new FakeClock();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(
            maxInboundMessagesPerSecond: 2, inboundMessageRateWindow: TimeSpan.FromSeconds(1));
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, clock, options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("a"), WebSocketMessageType.Text, true, CancellationToken.None);
        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("b"), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 2, runTask);

        clock.Advance(options.InboundMessageRateWindow + TimeSpan.FromTicks(1));

        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("c"), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 3, runTask);

        Assert.Equal(0, handler.DisconnectedCalls);
        Assert.False(runTask.IsCompleted);

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a write failure after a successful handshake forces teardown without hanging or throwing.</summary>
    [Fact]
    public async Task RunAsync_WriteFaultAfterHandshake_EndsWithoutHangingOrThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var faultingStream = new FailAfterFirstWriteStream(serverTcpClient.GetStream());
        var connection = new PublicWebSocketConnection(faultingStream, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The handshake response was the first write; this TrySend's frame is the second and fails.
        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("boom")));

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>
    /// Verifies that cancelling a connection whose peer never sends anything back still converges
    /// promptly: the cancelled read leaves the WebSocket unable to complete an ordinary close, and the
    /// teardown path's abort/dispose fallback recovers from that without hanging or leaking the
    /// connection.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancellationWithUnresponsivePeer_FallsBackToAbortWithoutHanging()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromMilliseconds(300));
        var connection = new PublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The client never reads and never sends its own close, so the pending read that cancellation
        // interrupts leaves the WebSocket in a state where the teardown path's own close attempt can
        // only fail -- proving the abort/dispose fallback recovers rather than hanging shutdown.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), "Cancellation with an unresponsive peer must not hang host shutdown.");
        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>Starts a loopback <see cref="TcpListener"/> on an operating-system-assigned port.</summary>
    private static (TcpListener Listener, int Port) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    /// <summary>Runs a background receive loop so a <see cref="ClientWebSocket"/> can transparently answer WebSocket-level keep-alive pings.</summary>
    private static async Task RunClientReadLoopAsync(ClientWebSocket clientWebSocket)
    {
        var buffer = new byte[256];
        try
        {
            while (true)
            {
                await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None);
            }
        }
        catch
        {
            // Ends when the socket is aborted/disposed by the test's own teardown.
        }
    }

    /// <summary>Sends a minimal valid WebSocket upgrade request on a raw client stream and reads past the server's 101 response.</summary>
    /// <param name="client">The raw client-side stream to complete the handshake over.</param>
    private static async Task CompleteRawHandshakeAsync(Stream client)
    {
        await client.WriteAsync(Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n"));

        // Read byte-by-byte until the response's terminating blank line.
        List<byte> tail = [];
        byte[] one = new byte[1];
        while (true)
        {
            int read = await client.ReadAsync(one).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(read > 0, "Unexpected end of stream while reading the handshake response.");
            tail.Add(one[0]);
            if (tail.Count > 4)
            {
                tail.RemoveAt(0);
            }

            if (tail.Count == 4 && tail[0] == '\r' && tail[1] == '\n' && tail[2] == '\r' && tail[3] == '\n')
            {
                return;
            }
        }
    }

    /// <summary>
    /// Creates a connected pair of loopback-socket streams for raw handshake-level byte tests. A
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

    /// <summary>Polls a condition until it becomes true, failing the test if the guard task ends unexpectedly early or the condition times out.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, Task guardTask)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (guardTask.IsCompleted)
            {
                await guardTask; // surface the connection's own failure instead of a generic timeout
            }

            Assert.True(DateTime.UtcNow < deadline, "Condition was not met within the expected time.");
            await Task.Delay(10);
        }
    }
}
