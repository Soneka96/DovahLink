using System.Diagnostics;
using System.Net.WebSockets;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises transport-overload selection and bounded connection disposal.</summary>
public class BridgeConnectionTests
{
    /// <summary>Verifies that receives use the explicit array-segment WebSocket overload.</summary>
    [Fact]
    public async Task ReceiveAsyncUsesTheArraySegmentOverload()
    {
        var socket = new FakeWebSocket();
        await using var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));

        Envelope envelope = await connection.ReceiveAsync();

        Assert.Equal("pong", envelope.MessageType);
        Assert.True(socket.ArraySegmentReceiveCalled);
        Assert.False(socket.MemoryReceiveCalled);
    }

    /// <summary>Verifies that an abrupt peer reset uses the connection wrapper's documented closure exception.</summary>
    [Fact]
    public async Task ReceiveAsyncNormalizesAbruptPeerClosure()
    {
        var socket = new FakeWebSocket(receiveException: new WebSocketException("peer reset"));
        await using var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.ReceiveAsync());

        Assert.IsType<WebSocketException>(error.InnerException);
    }

    /// <summary>Verifies that disposal aborts and returns when a peer withholds its close acknowledgement.</summary>
    [Fact]
    public async Task DisposeAsyncAbortsAfterTheGracefulCloseDeadline()
    {
        var socket = new FakeWebSocket(blockClose: true);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(25));
        var elapsed = Stopwatch.StartNew();

        await connection.DisposeAsync();

        Assert.True(socket.CloseCalled);
        Assert.True(socket.AbortCalled);
        Assert.True(socket.DisposeCalled);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
    }

    /// <summary>Verifies that successful graceful disposal does not abort the socket.</summary>
    [Fact]
    public async Task DisposeAsyncDoesNotAbortAfterSuccessfulGracefulClose()
    {
        var socket = new FakeWebSocket();
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(50));

        await connection.DisposeAsync();

        Assert.True(socket.CloseCalled);
        Assert.False(socket.AbortCalled);
        Assert.True(socket.DisposeCalled);
    }

    /// <summary>Verifies that a withheld close response is bounded and aborted like a withheld close request.</summary>
    [Fact]
    public async Task DisposeAsyncBoundsCloseOutputAfterThePeerInitiatesClosure()
    {
        var socket = new FakeWebSocket(
            blockCloseOutput: true,
            initialState: System.Net.WebSockets.WebSocketState.CloseReceived);
        var connection = new BridgeConnection(socket, TimeSpan.FromMilliseconds(25));

        await connection.DisposeAsync();

        Assert.True(socket.CloseOutputCalled);
        Assert.True(socket.AbortCalled);
        Assert.True(socket.DisposeCalled);
    }
}
