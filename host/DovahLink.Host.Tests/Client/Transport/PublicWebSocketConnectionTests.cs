using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
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
        var connection = Fixtures.BuildPublicWebSocketConnection(
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

    /// <summary>
    /// Verifies that a handler can send a response through the exact connection that delivered an
    /// inbound message -- via the <see cref="IPublicConnectionContext"/> passed to
    /// <see cref="IPublicWebSocketMessageHandler.HandleMessageAsync"/> -- and that the peer receives
    /// it through the connection's normal serialized/bounded writer, proving the capability is not
    /// merely accepted but actually wired to this connection's own outbound path.
    /// </summary>
    [Fact]
    public async Task HandleMessageAsync_HandlerSendsThroughReceivedContext_PeerReceivesResponseOnSameConnection()
    {
        byte[] responsePayload = Encoding.UTF8.GetBytes("response");
        var handler = new FakePublicWebSocketMessageHandler { AutoRespondPayload = responsePayload };
        (TcpListener listener, int port) = StartLoopbackListener();
        using var cancellation = new CancellationTokenSource();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = Fixtures.BuildPublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync("request"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[64];
        WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.Equal(responsePayload, buffer[..result.Count]);

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that two separate messages on the same connection receive the exact same connection-context instance, matching this connection's documented one-context-per-lifetime contract.</summary>
    [Fact]
    public async Task HandleMessageAsync_TwoMessagesOnSameConnection_ReceiveTheSameContextInstance()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        using var cancellation = new CancellationTokenSource();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = Fixtures.BuildPublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync("first"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 1, runTask);
        IPublicConnectionContext? firstContext = handler.LastConnection;

        await clientWebSocket.SendAsync("second"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 2, runTask);

        Assert.NotNull(firstContext);
        Assert.Same(firstContext, handler.LastConnection);

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Regression test: once <see cref="IPublicWebSocketMessageHandler.HandleMessageAsync"/> has
    /// returned its Task promptly, that Task never completing and ignoring the cancellation token it
    /// received must not be able to block <see cref="PublicWebSocketConnection.RunAsync"/> -- and so
    /// the listener's admission slot -- from ever completing during shutdown. This does not, and
    /// cannot, cover a handler that blocks the calling thread synchronously before ever returning a
    /// Task in the first place; that is a documented contract requirement on the interface instead,
    /// not something any caller-side await can bound.
    /// </summary>
    [Fact]
    public async Task RunAsync_HandleMessageAsyncTaskNeverCompletesIgnoringCancellation_StillEndsPromptlyOnExternalCancellation()
    {
        var handler = new FakePublicWebSocketMessageHandler { HangOnHandleMessageIgnoringCancellation = true };
        (TcpListener listener, int port) = StartLoopbackListener();
        using var cancellation = new CancellationTokenSource();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromMilliseconds(300));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 1, runTask);

        // HandleMessageAsync already returned its Task above; that Task now hangs forever, ignoring
        // cancellation entirely. Without the fix, RunAsync would never observe this because it would
        // be stuck awaiting that Task rather than the cancellation that is about to fire. The short
        // graceful-close timeout above only bounds the unrelated close-handshake fallback this
        // cancellation also triggers (matching
        // RunAsync_CancellationWithUnresponsivePeer_FallsBackToAbortWithoutHanging), so this assertion
        // is not accidentally measuring that instead of the fix under test.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        listener.Stop();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"RunAsync took {stopwatch.Elapsed}; a handler Task that ignores cancellation must not be able to block shutdown.");
    }

    /// <summary>
    /// Verifies that a connection context retained after its own connection has ended cannot affect
    /// a subsequently created connection: the stale context's <see cref="IPublicConnectionContext.TrySend"/>
    /// fails safely, and the new connection's own traffic is unaffected, proving the capability is
    /// structurally scoped to the exact connection instance that issued it rather than any kind of
    /// shared or global addressing.
    /// </summary>
    [Fact]
    public async Task HandleMessageAsync_StaleContextFromEndedConnection_CannotAffectSubsequentConnection()
    {
        var handlerA = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTaskA = listener.AcceptTcpClientAsync();
        using var clientWebSocketA = new ClientWebSocket();
        Task connectTaskA = clientWebSocketA.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClientA = await acceptTaskA.WaitAsync(TimeSpan.FromSeconds(5));
        var connectionA = Fixtures.BuildPublicWebSocketConnection(
            serverTcpClientA.GetStream(), handlerA, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTaskA = connectionA.RunAsync(CancellationToken.None);
        await connectTaskA.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocketA.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handlerA.LastConnection is not null, runTaskA);
        IPublicConnectionContext staleContext = handlerA.LastConnection!;

        await clientWebSocketA.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        await runTaskA.WaitAsync(TimeSpan.FromSeconds(5));

        // Connection A has now fully ended. Its captured context must fail safely rather than
        // reaching into whatever connection happens to be current next.
        Assert.False(staleContext.TrySend("late"u8.ToArray()));
        staleContext.RequestClose(); // must not throw even though connection A is already torn down

        var handlerB = new FakePublicWebSocketMessageHandler();
        Task<TcpClient> acceptTaskB = listener.AcceptTcpClientAsync();
        using var clientWebSocketB = new ClientWebSocket();
        Task connectTaskB = clientWebSocketB.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClientB = await acceptTaskB.WaitAsync(TimeSpan.FromSeconds(5));
        var connectionB = Fixtures.BuildPublicWebSocketConnection(
            serverTcpClientB.GetStream(), handlerB, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        using var cancellationB = new CancellationTokenSource();
        Task runTaskB = connectionB.RunAsync(cancellationB.Token);
        await connectTaskB.WaitAsync(TimeSpan.FromSeconds(5));

        byte[] payloadB = "unaffected"u8.ToArray();
        await clientWebSocketB.SendAsync(payloadB, WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handlerB.ReceivedMessages.Count == 1, runTaskB);

        Assert.Equal(payloadB, Assert.Single(handlerB.ReceivedMessages));
        Assert.False(runTaskB.IsCompleted);

        listener.Stop();
        cancellationB.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTaskB).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a message split across two WebSocket frames is accumulated and delivered to the
    /// handler as one complete payload, that dispatch happens only after the second, final fragment
    /// arrives -- not after the first, partial one -- and that a second fragmented message sent
    /// afterward reassembles independently, proving the accumulation buffer is correctly reset
    /// between messages rather than only ever tested once.
    /// </summary>
    [Fact]
    public async Task RunAsync_MessageSplitAcrossTwoFragments_ReassemblesAndDeliversOneCompleteMessage()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        using var cancellation = new CancellationTokenSource();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = Fixtures.BuildPublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("hel"), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        // Give the reader a chance to process the partial fragment before asserting nothing was
        // dispatched yet; a fixed small delay is enough since delivery would otherwise be near-instant.
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Empty(handler.ReceivedMessages);

        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("lo"), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 1, runTask);

        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("wo"), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("rld"), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 2, runTask);

        Assert.Equal(["hello"u8.ToArray(), "world"u8.ToArray()], handler.ReceivedMessages);

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a peer who never sends a handshake request is disconnected after the deadline without accepting or throwing.</summary>
    [Fact]
    public async Task RunAsync_HandshakeTimeout_EndsWithoutAcceptingOrThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(handshakeTimeout: TimeSpan.FromMilliseconds(200));
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, options: options, diagnostics: diagnostics);

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(0, handler.ConnectionEndedCalls);
        Assert.Equal(0, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.HandshakeTimeout], diagnostics.Reports);
        client.Dispose();
    }

    /// <summary>Verifies that a handshake request missing required WebSocket headers ends the connection without accepting or throwing.</summary>
    [Fact]
    public async Task RunAsync_MalformedHandshakeRequest_EndsWithoutAcceptingOrThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, diagnostics: diagnostics);

        byte[] request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\n\r\n");
        await client.WriteAsync(request);

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(0, handler.ConnectionEndedCalls);
        Assert.Equal(0, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.InvalidHandshake], diagnostics.Reports);
        client.Dispose();
    }

    /// <summary>Verifies that a handshake request exceeding the configured byte bound ends the connection without an unbounded buffer.</summary>
    [Fact]
    public async Task RunAsync_OversizedHandshakeRequest_EndsWithoutAcceptingOrThrowing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(maxHandshakeRequestBytes: 32);
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, options: options, diagnostics: diagnostics);

        // 40 bytes with no "\r\n\r\n" terminator, exceeding the 32-byte bound before headers complete.
        await client.WriteAsync(Encoding.ASCII.GetBytes(new string('a', 40)));

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal([PublicWebSocketConnectionEndReason.InvalidHandshake], diagnostics.Reports);
        client.Dispose();
    }

    /// <summary>Verifies that cancelling before the peer ever sends a handshake request propagates as cancellation without ever notifying the handler of a disconnection.</summary>
    [Fact]
    public async Task RunAsync_CancelledDuringHandshake_ThrowsAndNeverNotifiesDisconnected()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        using var cancellation = new CancellationTokenSource();

        Task runTask = connection.RunAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, handler.ConnectionEndedCalls);
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
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());

        await client.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n"));
        client.Dispose();

        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
    }

    /// <summary>
    /// Verifies that a silent peer is disconnected once the keep-alive deadline elapses, that the pong
    /// timeout genuinely contributes wait time beyond the interval alone (proving it is not silently
    /// ignored), and that detection completes within this file's usual generous bound for a real-timer
    /// test. This does not assert a tight <c>interval + pongTimeout</c> upper bound -- .NET's managed
    /// WebSocket keep-alive scheduler polls rather than firing at an exact instant, so this test only
    /// proves detection is bounded, not that it lands within the configured budget to the millisecond.
    /// Uses distinct interval and pong values so neither could be mistaken for the other.
    /// </summary>
    [Fact]
    public async Task RunAsync_KeepAliveDeadlineWithSilentPeer_PongTimeoutAddsWaitAndDetectionStaysBounded()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        TimeSpan keepAliveInterval = TimeSpan.FromMilliseconds(100);
        TimeSpan pongTimeout = TimeSpan.FromMilliseconds(300);
        var options = Fixtures.BuildPublicWebSocketTransportOptions(
            keepAliveInterval: keepAliveInterval, keepAlivePongTimeout: pongTimeout);
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, options: options, diagnostics: diagnostics);
        var stopwatch = Stopwatch.StartNew();
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The client never reads, so it can never observe or reply to the server's keep-alive ping.
        await handler.Disconnected.WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Elapsed must exceed the interval alone -- proving the pong timeout genuinely adds wait time
        // rather than being ignored -- and stay under this file's usual generous real-timer bound
        // (matching the other Stopwatch-based assertions here), proving detection is bounded. This is
        // a generous tolerance, not a tight proof of interval + pongTimeout as an exact ceiling.
        Assert.True(
            stopwatch.Elapsed > keepAliveInterval,
            $"Expected the pong timeout to add wait time beyond the {keepAliveInterval} interval alone, took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "Silent-peer detection must not hang.");
        Assert.Equal([PublicWebSocketConnectionEndReason.KeepAliveTimeout], diagnostics.Reports);

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
            keepAliveInterval: TimeSpan.FromMilliseconds(100), keepAlivePongTimeout: TimeSpan.FromMilliseconds(100));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        Task clientReadLoop = RunClientReadLoopAsync(clientWebSocket);

        // Several keep-alive cycles' worth of silence, with the client actively able to answer pings.
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.False(runTask.IsCompleted);
        Assert.Equal(0, handler.ConnectionEndedCalls);
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
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(maxMessageBytes: 16);
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, options: options, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync(new byte[10], WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await clientWebSocket.SendAsync(new byte[10], WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.MessageTooLarge], diagnostics.Reports);
        listener.Stop();
    }

    /// <summary>Verifies that a fragmented message left incomplete past the configured assembly deadline is force-closed.</summary>
    [Fact]
    public async Task RunAsync_FragmentedMessageNeverCompletes_ForceClosesAfterAssemblyDeadline()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(fragmentAssemblyTimeout: TimeSpan.FromMilliseconds(200));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, options: options, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync("start"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.FragmentAssemblyTimeout], diagnostics.Reports);
        listener.Stop();
    }

    /// <summary>
    /// Verifies that later fragments of one still-incomplete message do not extend the assembly
    /// deadline anchored by its first fragment -- the core regression this deadline exists to prove:
    /// a deadline re-derived per fragment would let a peer trickle a message open indefinitely.
    /// </summary>
    [Fact]
    public async Task RunAsync_RepeatedFragmentsBeforeExpiry_DoNotExtendAssemblyDeadline()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(fragmentAssemblyTimeout: TimeSpan.FromMilliseconds(300));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, options: options, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await clientWebSocket.SendAsync("a"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await clientWebSocket.SendAsync("b"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        await clientWebSocket.SendAsync("c"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        // A per-fragment reset would push closure out to roughly 250ms (last fragment) + 300ms = 550ms;
        // the correct anchored-once deadline closes around 300ms from the first fragment. 450ms sits
        // comfortably between the two, with headroom on both sides for ordinary scheduling jitter.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(450),
            $"Connection took {stopwatch.Elapsed} to close; a per-fragment reset would push this past 550ms instead of the correct ~300ms.");
        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.FragmentAssemblyTimeout], diagnostics.Reports);
        listener.Stop();
    }

    /// <summary>Verifies that a fragmented message completed well inside the assembly deadline is delivered exactly once and the connection stays usable afterward.</summary>
    [Fact]
    public async Task RunAsync_FragmentedMessageCompletedBeforeAssemblyDeadline_DeliversOnceAndConnectionStaysUsable()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        using var cancellation = new CancellationTokenSource();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(fragmentAssemblyTimeout: TimeSpan.FromMilliseconds(300));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync("hel"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await clientWebSocket.SendAsync("lo"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 1, runTask);

        Assert.Equal("hello"u8.ToArray(), Assert.Single(handler.ReceivedMessages));

        // The connection must remain usable well past the fragmented message's own former deadline --
        // proving it was fully cleared on completion rather than merely not yet fired.
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        await clientWebSocket.SendAsync("world"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 2, runTask);

        Assert.Equal(["hello"u8.ToArray(), "world"u8.ToArray()], handler.ReceivedMessages);
        Assert.False(runTask.IsCompleted);

        listener.Stop();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a fragmented message beginning after an earlier one completed receives its own fresh assembly deadline, unaffected by the first message's now-cleared one.</summary>
    [Fact]
    public async Task RunAsync_FragmentedMessageAfterEarlierOneCompleted_GetsFreshAssemblyDeadline()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(fragmentAssemblyTimeout: TimeSpan.FromMilliseconds(300));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Message A: fragmented, completed quickly.
        await clientWebSocket.SendAsync("a1"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await clientWebSocket.SendAsync("a2"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 1, runTask);

        // Longer than A's own former deadline: if A's deadline object were somehow still live, message
        // B below would already be past its budget before it even finishes sending.
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        // Message B: fragmented, completed comfortably inside its own fresh deadline.
        await clientWebSocket.SendAsync("b1"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await clientWebSocket.SendAsync("b2"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 2, runTask);

        Assert.Equal(["a1a2"u8.ToArray(), "b1b2"u8.ToArray()], handler.ReceivedMessages);
        Assert.False(runTask.IsCompleted);
        listener.Stop();
    }

    /// <summary>Verifies that one completed fragmented message consumes exactly one slot of the completed-message rate limit, never one slot per fragment.</summary>
    [Fact]
    public async Task RunAsync_FragmentedMessage_CountsAsExactlyOneAgainstInboundRateLimit()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(maxInboundMessagesPerSecond: 1);
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, options: options, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // One fragmented message, two fragments: if fragments were separately rate-limited, the
        // connection would already be closed before the second, unfragmented message below is sent.
        await clientWebSocket.SendAsync("fr"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await clientWebSocket.SendAsync("ag"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 1, runTask);

        // This second, unfragmented message exceeds the 1/sec budget the fragmented message above
        // already consumed exactly one slot of, so it must be rejected and the connection closed.
        await clientWebSocket.SendAsync("second"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("frag"u8.ToArray(), Assert.Single(handler.ReceivedMessages));
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.InboundRateLimitExceeded], diagnostics.Reports);
        listener.Stop();
    }

    /// <summary>
    /// Verifies that external cancellation still propagates correctly while a fragmented message is
    /// mid-assembly (an active fragment-assembly deadline in play), rather than being misattributed to
    /// the deadline: the per-receive linked token combines both sources, so this proves the ordering of
    /// the two <c>OperationCanceledException</c> catch clauses correctly favors real external
    /// cancellation.
    /// </summary>
    [Fact]
    public async Task RunAsync_ExternalCancellationDuringFragmentedMessageAssembly_ThrowsAndNotifiesDisconnected()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(
            fragmentAssemblyTimeout: TimeSpan.FromSeconds(5), gracefulCloseTimeout: TimeSpan.FromMilliseconds(200));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Start, but never complete, a fragmented message so fragmentAssemblyDeadline is active and its
        // token is linked into the pending ReceiveAsync call alongside the external cancellation token.
        await clientWebSocket.SendAsync("partial"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(1, handler.ConnectionEndedCalls);
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
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
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
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, diagnostics: diagnostics);
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
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.InvalidFraming], diagnostics.Reports);
        client.Dispose();
    }

    /// <summary>Verifies that a binary message is safely closed without ever reaching the handler.</summary>
    [Fact]
    public async Task RunAsync_BinaryMessage_ClosesWithoutDeliveringToHandler()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(handler.ReceivedMessages);
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.UnsupportedBinaryMessage], diagnostics.Reports);
        listener.Stop();
    }

    /// <summary>Verifies that a peer-initiated close ends the connection gracefully and notifies the handler.</summary>
    [Fact]
    public async Task RunAsync_ClientClose_EndsGracefullyAndNotifiesDisconnected()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromSeconds(2));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, options: options, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Empty(diagnostics.Reports);
        listener.Stop();
    }

    /// <summary>
    /// Verifies that the mandatory <see cref="IPublicWebSocketMessageHandler.HandleConnectionEnded"/>
    /// invalidation runs exactly once, before the best-effort
    /// <see cref="IPublicWebSocketMessageHandler.HandleDisconnectedAsync"/> notification, and before
    /// <see cref="IPublicWebSocketConnection.RunAsync"/> returns -- proving the transport cannot
    /// return (and so cannot let the listener release its admission slot) before mandatory
    /// invalidation has already completed.
    /// </summary>
    [Fact]
    public async Task RunAsync_ClientClose_CallsConnectionEndedBeforeDisconnectedAndBeforeReturning()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = Fixtures.BuildPublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, handler.ConnectionEndedCalls);

        await clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Equal(["ConnectionEnded", "Disconnected"], handler.CallOrder);
        listener.Stop();
    }

    /// <summary>Verifies that a mandatory-invalidation handler that throws still lets the connection fully tear down rather than leaking the transport, and still runs the best-effort disconnect notification afterward.</summary>
    [Fact]
    public async Task RunAsync_ConnectionEndedThrows_StillTearsDownConnection()
    {
        var handler = new FakePublicWebSocketMessageHandler { ConnectionEndedFailure = new InvalidOperationException("Simulated handler failure.") };
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var connection = Fixtures.BuildPublicWebSocketConnection(serverStream, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Throws<ObjectDisposedException>(() => serverStream.ReadByte());
        listener.Stop();
    }

    /// <summary>Verifies that a disconnect-notification handler that throws still lets the connection fully tear down rather than leaking the transport.</summary>
    [Fact]
    public async Task RunAsync_DisconnectNotificationThrows_StillTearsDownConnection()
    {
        var handler = new FakePublicWebSocketMessageHandler { DisconnectedFailure = new InvalidOperationException("Simulated handler failure.") };
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var connection = Fixtures.BuildPublicWebSocketConnection(serverStream, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Throws<ObjectDisposedException>(() => serverStream.ReadByte());
        listener.Stop();
    }

    /// <summary>Verifies that a disconnect-notification handler that never returns still lets the connection tear down within the bounded timeout instead of hanging forever.</summary>
    [Fact]
    public async Task RunAsync_DisconnectNotificationHangs_StillTearsDownWithinBound()
    {
        var handler = new FakePublicWebSocketMessageHandler { HangOnDisconnected = true };
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var disconnectNotificationTimeout = TimeSpan.FromMilliseconds(200);
        var options = Fixtures.BuildPublicWebSocketTransportOptions(disconnectNotificationTimeout: disconnectNotificationTimeout);
        var connection = Fixtures.BuildPublicWebSocketConnection(serverStream, handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        // Without the bounded timeout, this would hang forever rather than complete within 5 seconds.
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Teardown took {stopwatch.Elapsed}, far longer than the configured {disconnectNotificationTimeout} bound.");
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);

        // The handler's own abandoned continuation reacts to its token asynchronously, independently
        // of when this connection's outer WaitAsync gave up on it; poll rather than assert immediately.
        await WaitUntilAsync(() => handler.ReceivedTokenWasCancelled, Task.CompletedTask);
        Assert.Throws<ObjectDisposedException>(() => serverStream.ReadByte());
        listener.Stop();
    }

    /// <summary>Verifies that cancelling the connection propagates as cancellation and still notifies the handler before returning.</summary>
    [Fact]
    public async Task RunAsync_ExternalCancellation_ThrowsOperationCanceledAndNotifiesDisconnected()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromMilliseconds(200));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, options: options, diagnostics: diagnostics);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Empty(diagnostics.Reports);
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
        var connection = Fixtures.BuildPublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        clientWebSocket.Abort();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.ConnectionEndedCalls);
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
        var connection = Fixtures.BuildPublicWebSocketConnection(
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
        var connection = Fixtures.BuildPublicWebSocketConnection(new MemoryStream(), handler, new SystemClock(), options);

        Assert.True(connection.TrySend(new byte[10]));
    }

    /// <summary>Verifies that a payload exactly at the outbound message-count bound is accepted, proving the bound rejects only what exceeds it.</summary>
    [Fact]
    public void TrySend_ExactlyAtOutboundQueueMaxMessages_ReturnsTrue()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 3, outboundQueueMaxBytes: 1024);
        var connection = Fixtures.BuildPublicWebSocketConnection(new MemoryStream(), handler, new SystemClock(), options);

        for (int index = 0; index < 3; index++)
        {
            Assert.True(connection.TrySend(new byte[1]));
        }
    }

    /// <summary>Verifies that a send after the connection has ended returns false instead of throwing.</summary>
    [Fact]
    public async Task TrySend_AfterConnectionEnded_ReturnsFalse()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions(handshakeTimeout: TimeSpan.FromMilliseconds(200)));

        // No handshake request is ever sent, so the connection ends via the handshake timeout.
        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(connection.TrySend(new byte[1]));
        client.Dispose();
    }

    /// <summary>
    /// Verifies that a queue overflow requested before <see cref="PublicWebSocketConnection.RunAsync"/>
    /// is ever called -- as a caller obtaining a reference to the connection before its own setup runs
    /// might do -- is not lost: once <see cref="PublicWebSocketConnection.RunAsync"/> does start, it
    /// must end promptly rather than running as if nothing had happened.
    /// </summary>
    [Fact]
    public async Task TrySend_QueueOverflowBeforeRunAsyncStarts_StillEndsPromptlyOnceStarted()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(
            outboundQueueMaxBytes: 4, handshakeTimeout: TimeSpan.FromSeconds(30));
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, new SystemClock(), options);

        // Overflow the queue before RunAsync is ever called. The client never sends a handshake
        // request, so if this request were lost, RunAsync would otherwise sit waiting for the full
        // 30-second handshake timeout with nothing else ever cancelling it.
        Assert.False(connection.TrySend(new byte[8]));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"RunAsync took {stopwatch.Elapsed} even though the queue overflow happened before it ever " +
            "started; a close request made before RunAsync begins must not be lost.");
        Assert.Equal(0, handler.ConnectionEndedCalls);
        Assert.Equal(0, handler.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that a send beyond the outbound message-count bound is rejected without blocking, when nothing drains the queue.</summary>
    [Fact]
    public void TrySend_BeyondOutboundQueueMaxMessages_ReturnsFalse()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 3, outboundQueueMaxBytes: 1024);
        var connection = Fixtures.BuildPublicWebSocketConnection(new MemoryStream(), handler, options: options, diagnostics: diagnostics);

        for (int index = 0; index < 3; index++)
        {
            Assert.True(connection.TrySend(new byte[1]));
        }

        Assert.False(connection.TrySend(new byte[1]));
        Assert.Equal([PublicWebSocketConnectionEndReason.OutboundCapacityExceeded], diagnostics.Reports);
    }

    /// <summary>Verifies that a send beyond the outbound byte-budget bound is rejected, when nothing drains the queue.</summary>
    [Fact]
    public void TrySend_BeyondOutboundQueueMaxBytes_ReturnsFalse()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 100, outboundQueueMaxBytes: 10);
        var connection = Fixtures.BuildPublicWebSocketConnection(new MemoryStream(), handler, options: options, diagnostics: diagnostics);

        Assert.True(connection.TrySend(new byte[6]));
        Assert.False(connection.TrySend(new byte[6]));
        Assert.Equal([PublicWebSocketConnectionEndReason.OutboundCapacityExceeded], diagnostics.Reports);
    }

    /// <summary>
    /// Verifies that a send rejected only by the byte budget rolls back the message-count slot it
    /// provisionally reserved rather than leaking it -- otherwise the one allowed slot in this test
    /// would already be exhausted by the rejected attempt, and a later message that fits the byte
    /// budget would be wrongly rejected too.
    /// </summary>
    [Fact]
    public void TrySend_RejectedByByteBudget_DoesNotLeakMessageCountReservation()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 1, outboundQueueMaxBytes: 4);
        var connection = Fixtures.BuildPublicWebSocketConnection(new MemoryStream(), handler, new SystemClock(), options);

        Assert.False(connection.TrySend(new byte[8]));
        Assert.True(connection.TrySend(new byte[4]));
    }

    /// <summary>
    /// Verifies that a frame the writer has already dequeued but not yet finished sending still
    /// counts against the outbound message-count limit -- a bounded <see cref="Channel{T}"/> alone
    /// frees its capacity the instant a frame is dequeued, before that frame's send over the wire
    /// actually finishes, which would let this connection own one more transport message than
    /// configured. Also verifies that the resulting rejection requests this connection's own
    /// controlled close rather than leaving it open with the message silently dropped, and that
    /// teardown completes promptly rather than waiting out the far longer per-write deadline
    /// configured for this test (proving the rejection itself is what ends the connection, not the
    /// unrelated per-write timeout).
    /// </summary>
    [Fact]
    public async Task TrySend_MessageCountOverflowWithInFlightWriterFrame_RequestsControlledCloseAndTearsDownWithinBound()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var blockingStream = new BlockingAfterFirstWriteStream(serverStream);
        var options = Fixtures.BuildPublicWebSocketTransportOptions(
            outboundQueueMaxMessages: 1, outboundQueueMaxBytes: 1024, gracefulCloseTimeout: TimeSpan.FromSeconds(30));
        var connection = Fixtures.BuildPublicWebSocketConnection(blockingStream, handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The first frame is picked up by the writer and blocks in SendAsync -- dequeued, so the
        // channel itself is now empty, but still owned by this connection and still consuming the
        // single allowed message slot. The second must be rejected on that basis alone.
        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("first")));
        await blockingStream.BlockedWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(connection.TrySend(Encoding.UTF8.GetBytes("second")));

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Teardown took {stopwatch.Elapsed} even though the configured write deadline is 30 seconds away; " +
            "the message-count rejection itself must end the connection promptly.");
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Throws<ObjectDisposedException>(() => serverStream.ReadByte());
        listener.Stop();
    }

    /// <summary>
    /// Verifies that the one allowed message slot releases only once the writer has actually finished
    /// sending the in-flight frame -- not merely once it was dequeued from the channel -- by blocking
    /// the first send, releasing it, waiting for the peer to receive it, and proving a second send now
    /// succeeds where it would otherwise still be occupying the single-message bound.
    /// </summary>
    [Fact]
    public async Task TrySend_MessageSlotReleasedOnlyAfterWriterFinishesSend_AllowsLaterAdmission()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var blockingStream = new BlockingAfterFirstWriteStream(serverStream);
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 1, outboundQueueMaxBytes: 1024);
        var connection = Fixtures.BuildPublicWebSocketConnection(blockingStream, handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The handshake response was the first write; this send is the second and blocks until
        // released, so the frame is dequeued -- freeing the channel's own capacity -- without yet
        // having actually finished sending.
        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("first")));
        await blockingStream.BlockedWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        blockingStream.Release();

        var buffer = new byte[64];
        WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("first", Encoding.UTF8.GetString(buffer, 0, result.Count));

        // The peer only receives bytes the server has already finished sending, so the writer's own
        // release of the one message slot -- which happens synchronously right after that send
        // completes -- is guaranteed to have already run by this point.
        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("second")));

        listener.Stop();
        connection.RequestClose();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that concurrent <see cref="PublicWebSocketConnection.TrySend"/> calls racing to
    /// reserve the same bounded message-count capacity never collectively over-admit, even though the
    /// reservation itself is lock-free. Uses an undrained connection (<see cref="PublicWebSocketConnection.RunAsync"/>
    /// never started) so the result is fully deterministic rather than dependent on how fast a writer
    /// drains the queue.
    /// </summary>
    [Fact]
    public async Task TrySend_ConcurrentCallsWithUndrainedQueue_NeverAdmitMoreThanTheConfiguredMessageLimit()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 4, outboundQueueMaxBytes: 1024);
        var connection = Fixtures.BuildPublicWebSocketConnection(new MemoryStream(), handler, new SystemClock(), options);

        const int attempts = 50;
        bool[] results = await Task.WhenAll(Enumerable.Range(0, attempts)
            .Select(index => Task.Run(() => connection.TrySend(new byte[1]))));

        Assert.Equal(4, results.Count(succeeded => succeeded));
    }

    /// <summary>
    /// Verifies that a message which cannot be admitted because the bounded outbound queue's byte
    /// budget is already exhausted by a still-in-flight blocked send -- rather than the message-count
    /// limit -- also requests this connection's own controlled close and tears down promptly.
    /// </summary>
    [Fact]
    public async Task TrySend_ByteBudgetOverflowWithBlockedWriter_RequestsControlledCloseAndTearsDownWithinBound()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var blockingStream = new BlockingAfterFirstWriteStream(serverStream);
        // A generous message-count bound ensures only the byte budget can ever reject a send here.
        var options = Fixtures.BuildPublicWebSocketTransportOptions(
            outboundQueueMaxMessages: 100, outboundQueueMaxBytes: 6, gracefulCloseTimeout: TimeSpan.FromSeconds(30));
        var connection = Fixtures.BuildPublicWebSocketConnection(blockingStream, handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // "first" (5 bytes) is picked up by the writer and blocks in SendAsync; its bytes stay
        // reserved in the budget the whole time it is in flight, so "second" (6 bytes) alone already
        // exceeds the 6-byte budget once added to those still-reserved 5 bytes.
        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("first")));
        await blockingStream.BlockedWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(connection.TrySend(Encoding.UTF8.GetBytes("second")));

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Teardown took {stopwatch.Elapsed} even though the configured write deadline is 30 seconds away; " +
            "the queue-overflow request itself must end the connection promptly.");
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Throws<ObjectDisposedException>(() => serverStream.ReadByte());
        listener.Stop();
    }

    /// <summary>Verifies that exceeding the inbound message-rate limit closes the connection before delivering the excess message.</summary>
    [Fact]
    public async Task RunAsync_InboundRateLimitExceeded_ClosesConnectionBeforeExcessMessage()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var clock = new FakeClock();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(maxInboundMessagesPerSecond: 3);
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, clock, options, diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        for (int index = 0; index < 4; index++)
        {
            await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes($"m{index}"), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, handler.ReceivedMessages.Count);
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.InboundRateLimitExceeded], diagnostics.Reports);
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
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, clock, options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("a"), WebSocketMessageType.Text, true, CancellationToken.None);
        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("b"), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 2, runTask);

        clock.Advance(options.InboundMessageRateWindow + TimeSpan.FromTicks(1));

        await clientWebSocket.SendAsync(Encoding.UTF8.GetBytes("c"), WebSocketMessageType.Text, true, CancellationToken.None);
        await WaitUntilAsync(() => handler.ReceivedMessages.Count == 3, runTask);

        Assert.Equal(0, handler.ConnectionEndedCalls);
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
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var faultingStream = new FailAfterFirstWriteStream(serverTcpClient.GetStream());
        var connection = Fixtures.BuildPublicWebSocketConnection(faultingStream, handler, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The handshake response was the first write; this TrySend's frame is the second and fails.
        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("boom")));

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.WriteFailure], diagnostics.Reports);
        listener.Stop();
    }

    /// <summary>
    /// Verifies that a send blocked on an unresponsive peer -- one that stops draining the socket
    /// entirely, rather than failing the write outright -- still ends the connection once its own
    /// per-write deadline elapses, even though nothing external ever cancels the connection or
    /// releases the write.
    /// </summary>
    [Fact]
    public async Task RunAsync_WriteTimeoutWithoutExternalCancellation_EndsWithinBoundAndDisposesStream()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var blockingStream = new BlockingAfterFirstWriteStream(serverStream);
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromMilliseconds(200));
        var connection = Fixtures.BuildPublicWebSocketConnection(blockingStream, handler, options: options, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The handshake response was the first write; this TrySend's frame is the second and blocks
        // until either released or its own per-write deadline elapses -- neither of which this test
        // ever triggers externally, so only the new per-write timeout can end the connection.
        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("stuck")));
        await blockingStream.BlockedWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Teardown took {stopwatch.Elapsed}, far longer than the configured {options.GracefulCloseTimeout} write deadline.");
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Equal([PublicWebSocketConnectionEndReason.WriteFailure], diagnostics.Reports);
        Assert.Throws<ObjectDisposedException>(() => serverStream.ReadByte());
        listener.Stop();
    }

    /// <summary>
    /// Verifies that cancelling a connection whose writer is currently blocked on an unresponsive peer
    /// ends teardown promptly via cancellation, rather than waiting out the far longer per-write
    /// deadline configured for this test.
    /// </summary>
    [Fact]
    public async Task RunAsync_WriterBlockedThenCancelledExternally_EndsPromptlyWithoutHanging()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var blockingStream = new BlockingAfterFirstWriteStream(serverStream);
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromSeconds(30));
        var connection = Fixtures.BuildPublicWebSocketConnection(blockingStream, handler, new SystemClock(), options);
        using var cancellation = new CancellationTokenSource();
        Task runTask = connection.RunAsync(cancellation.Token);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("stuck")));
        await blockingStream.BlockedWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Cancelling a blocked writer took {stopwatch.Elapsed} even though the configured write deadline is 30 seconds away; " +
            "teardown must not wait for the per-write timeout when cancellation already ended it.");
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        Assert.Throws<ObjectDisposedException>(() => serverStream.ReadByte());
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
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
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
        Assert.Equal(1, handler.ConnectionEndedCalls);
        Assert.Equal(1, handler.DisconnectedCalls);
        listener.Stop();
    }

    /// <summary>
    /// Verifies that a <see cref="IPublicWebSocketConnection.RequestClose"/> requested before
    /// <see cref="PublicWebSocketConnection.RunAsync"/> is ever called is not lost, and that it ends
    /// the connection promptly without treating it as a forced close (no handler notification, since
    /// the handshake never happened).
    /// </summary>
    [Fact]
    public async Task RequestClose_BeforeRunAsyncStarts_StillEndsPromptlyOnceStartedWithoutForcing()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var options = Fixtures.BuildPublicWebSocketTransportOptions(handshakeTimeout: TimeSpan.FromSeconds(30));
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, new SystemClock(), options);

        // Requested before RunAsync is ever called. The client never sends a handshake request, so if
        // this request were lost, RunAsync would otherwise sit waiting for the full 30-second
        // handshake timeout with nothing else ever cancelling it.
        connection.RequestClose();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"RunAsync took {stopwatch.Elapsed} even though RequestClose happened before it ever started.");
        Assert.Equal(0, handler.ConnectionEndedCalls);
        Assert.Equal(0, handler.DisconnectedCalls);
        client.Dispose();
    }

    /// <summary>Verifies that calling <see cref="IPublicWebSocketConnection.RequestClose"/> more than once, and alongside another close cause, never throws.</summary>
    [Fact]
    public async Task RequestClose_CalledRepeatedlyAndAlongsideQueueOverflow_NeverThrows()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 1, outboundQueueMaxBytes: 1024);
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        connection.RequestClose();
        connection.RequestClose();
        connection.TrySend(new byte[2000]); // exceeds the byte budget, triggering the distinct forced-close path too

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        listener.Stop();
    }

    /// <summary>
    /// Verifies the supported administrative-invalidation sequence -- send a terminal payload, then
    /// request close -- gives the already-admitted terminal frame a bounded opportunity to reach the
    /// peer before teardown, rather than the close request racing the queued frame out of existence.
    /// Delivery remains best-effort, not a guarantee; this proves the opportunity exists.
    /// </summary>
    [Fact]
    public async Task TrySend_TerminalPayloadThenRequestClose_GivesAdmittedFrameADrainOpportunity()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromSeconds(2));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        byte[] terminalPayload = Encoding.UTF8.GetBytes("terminal");
        Assert.True(connection.TrySend(terminalPayload));
        connection.RequestClose();

        var buffer = new byte[64];
        WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.Equal(terminalPayload, buffer[..result.Count]);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();
    }

    /// <summary>
    /// Regression test: a late <see cref="PublicWebSocketConnection.TrySend"/> rejected only because
    /// <see cref="IPublicWebSocketConnection.RequestClose"/> already completed the outbound queue
    /// must not be misclassified as a queue overflow and force-cancel the writer -- doing so would
    /// abort an already-admitted terminal frame that is still in flight, defeating the very bounded
    /// drain opportunity <see cref="IPublicWebSocketConnection.RequestClose"/> exists to give it.
    /// </summary>
    [Fact]
    public async Task TrySend_LateSendAfterRequestClose_DoesNotAbortInFlightTerminalFrame()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var blockingStream = new BlockingAfterFirstWriteStream(serverStream);
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromSeconds(5));
        var connection = Fixtures.BuildPublicWebSocketConnection(blockingStream, handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The handshake response was the first write; this terminal send is the second and blocks
        // until released, simulating a peer that has not yet drained it when the late send below
        // arrives -- the exact window in which the late send could otherwise abort it.
        byte[] terminalPayload = Encoding.UTF8.GetBytes("terminal");
        Assert.True(connection.TrySend(terminalPayload));
        await blockingStream.BlockedWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        connection.RequestClose();
        Assert.False(connection.TrySend(Encoding.UTF8.GetBytes("late")));

        // With the bug, the late send above would force-cancel the shared writer token, which would
        // abort the still-blocked terminal send and end the connection almost immediately -- far
        // short of the 5-second graceful-close deadline configured above. Waiting here without
        // releasing the block proves that did not happen.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.False(runTask.IsCompleted, "A late send rejected by an in-progress orderly close must not force-cancel the in-flight terminal send.");

        blockingStream.Release();

        var buffer = new byte[64];
        WebSocketReceiveResult result = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.Equal(terminalPayload, buffer[..result.Count]);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();
    }

    /// <summary>
    /// Verifies that a <see cref="PublicWebSocketConnection.TrySend"/> call racing a concurrent
    /// <see cref="IPublicWebSocketConnection.RequestClose"/> call never throws, never corrupts the
    /// outbound queue's accounting, and produces a coherent per-call result: an admitted send that
    /// reaches the peer, or a cleanly rejected one once closing has begun.
    /// </summary>
    [Fact]
    public async Task TrySend_RacingRequestClose_NeverThrowsAndTearsDownCoherently()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(outboundQueueMaxMessages: 200, outboundQueueMaxBytes: 1024 * 1024);
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        Task sendLoop = Task.Run(() =>
        {
            for (int index = 0; index < 100; index++)
            {
                connection.TrySend(Encoding.UTF8.GetBytes($"message-{index}"));
            }
        });
        Task closeCall = Task.Run(connection.RequestClose);

        await Task.WhenAll(sendLoop, closeCall);
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        listener.Stop();
    }

    /// <summary>Verifies that a send made synchronously after <see cref="IPublicWebSocketConnection.RequestClose"/> returns <see langword="false"/>, since the outbound queue is completed immediately rather than only once teardown finishes.</summary>
    [Fact]
    public async Task TrySend_AfterRequestClose_ReturnsFalse()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var connection = Fixtures.BuildPublicWebSocketConnection(
            serverTcpClient.GetStream(), handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions());
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        connection.RequestClose();

        Assert.False(connection.TrySend(Encoding.UTF8.GetBytes("too-late")));

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();
    }

    /// <summary>Verifies that calling <see cref="IPublicWebSocketConnection.RequestClose"/> after the connection has already fully ended is a safe no-op rather than throwing.</summary>
    [Fact]
    public async Task RequestClose_AfterConnectionAlreadyEnded_DoesNotThrow()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (Stream server, Stream client) = await CreateConnectedStreamPairAsync();
        var connection = Fixtures.BuildPublicWebSocketConnection(server, handler, new SystemClock(), Fixtures.BuildPublicWebSocketTransportOptions(handshakeTimeout: TimeSpan.FromMilliseconds(200)));

        // No handshake request is ever sent, so the connection ends via the handshake timeout, fully
        // disposing its internally owned cancellation sources.
        await connection.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        connection.RequestClose();

        client.Dispose();
    }

    /// <summary>
    /// Verifies that a peer who never drains the outbound queue -- so the bounded drain
    /// <see cref="IPublicWebSocketConnection.RequestClose"/> gives it never actually completes -- still
    /// lets teardown converge within roughly <see cref="PublicWebSocketTransportOptions.GracefulCloseTimeout"/>
    /// rather than hanging indefinitely, proving the drain wait's own timeout is what eventually forces
    /// the read loop to stop.
    /// </summary>
    [Fact]
    public async Task RequestClose_WriterBlockedOnUnresponsivePeer_StillTearsDownWithinGracefulCloseTimeoutBound()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        Stream serverStream = serverTcpClient.GetStream();
        var blockingStream = new BlockingAfterFirstWriteStream(serverStream);
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromMilliseconds(300));
        var connection = Fixtures.BuildPublicWebSocketConnection(blockingStream, handler, new SystemClock(), options);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The handshake response was the first write; this send is the second and blocks forever
        // (never released), so the outbound queue can never actually drain.
        Assert.True(connection.TrySend(Encoding.UTF8.GetBytes("stuck")));
        await blockingStream.BlockedWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        connection.RequestClose();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Teardown took {stopwatch.Elapsed}, far longer than the configured {options.GracefulCloseTimeout} drain bound.");
        listener.Stop();
    }

    /// <summary>
    /// Verifies that an ordinary <see cref="IPublicWebSocketConnection.RequestClose"/> that has to
    /// interrupt a read loop idling on a silent peer -- no message in flight, no fragment-assembly
    /// deadline active -- reports no abnormal diagnostic. This is the key regression proof for the
    /// generic keep-alive-timeout catch's <c>orderlyCloseInProgress</c> guard: that same catch also
    /// fires when <see cref="IPublicWebSocketConnection.RequestClose"/> unblocks a pending
    /// <see cref="System.Net.WebSockets.WebSocket.ReceiveAsync(Memory{byte}, CancellationToken)"/>
    /// call, since <c>orderlyCloseRequested</c> is linked into the same cancellation token that catch
    /// reacts to -- without the guard, this ordinary close would be misreported as
    /// <see cref="PublicWebSocketConnectionEndReason.KeepAliveTimeout"/>.
    /// </summary>
    [Fact]
    public async Task RequestClose_WhileReadLoopIdleWaitingOnSilentPeer_ReportsNoAbnormalDiagnostic()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        var diagnostics = new FakePublicWebSocketTransportDiagnostics();
        (TcpListener listener, int port) = StartLoopbackListener();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        using var clientWebSocket = new ClientWebSocket();
        Task connectTask = clientWebSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        using TcpClient serverTcpClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        var options = Fixtures.BuildPublicWebSocketTransportOptions(gracefulCloseTimeout: TimeSpan.FromMilliseconds(300));
        var connection = Fixtures.BuildPublicWebSocketConnection(serverTcpClient.GetStream(), handler, options: options, diagnostics: diagnostics);
        Task runTask = connection.RunAsync(CancellationToken.None);
        await connectTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The peer never sends anything further, so the read loop is genuinely parked in ReceiveAsync
        // with no in-flight message and no fragment-assembly deadline -- a fixed small delay is enough
        // to let it reach that wait since nothing else is racing this connection.
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        connection.RequestClose();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(diagnostics.Reports);
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
            "Host: 127.0.0.1\r\n" +
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
