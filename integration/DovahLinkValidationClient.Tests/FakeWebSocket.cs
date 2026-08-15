using System.Net.WebSockets;
using System.Text;

namespace DovahLinkValidationClient.Tests;

/// <summary>Provides a scripted WebSocket for validation-client and scenario-helper tests.</summary>
internal sealed class FakeWebSocket : WebSocket
{
    /// <summary>A default complete protocol envelope returned when no receive script is supplied.</summary>
    private const string DefaultMessage =
        """{"messageType":"pong","messageId":"message-1","sessionId":"session-1","correlationId":null,"payload":{},"bridgeInstanceId":null,"playContextId":null,"clientId":null}""";

    /// <summary>Complete messages returned by successive receive operations.</summary>
    private readonly Queue<byte[]> _receiveMessages;

    /// <summary>Whether a full graceful close remains pending until abort.</summary>
    private readonly bool _blockClose;

    /// <summary>Whether a close response remains pending until abort.</summary>
    private readonly bool _blockCloseOutput;

    /// <summary>An optional receive failure surfaced by both receive overloads.</summary>
    private readonly Exception? _receiveException;

    /// <summary>An optional send failure surfaced by the send overload.</summary>
    private readonly Exception? _sendException;

    /// <summary>Completes blocked close work after abort.</summary>
    private readonly TaskCompletionSource _closeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The current fake socket state.</summary>
    private WebSocketState _state;

    /// <summary>Creates a scripted socket with controllable closure behavior.</summary>
    /// <param name="receiveMessages">Complete text messages returned by receives, or one default pong when omitted.</param>
    /// <param name="blockClose">Whether full graceful close should remain pending.</param>
    /// <param name="blockCloseOutput">Whether a close response should remain pending.</param>
    /// <param name="initialState">The socket state visible at construction.</param>
    /// <param name="receiveException">An optional exception thrown by receive operations.</param>
    /// <param name="sendException">An optional exception thrown by the send operation.</param>
    public FakeWebSocket(
        IEnumerable<string>? receiveMessages = null,
        bool blockClose = false,
        bool blockCloseOutput = false,
        WebSocketState initialState = WebSocketState.Open,
        Exception? receiveException = null,
        Exception? sendException = null)
    {
        _receiveMessages = new Queue<byte[]>((receiveMessages ?? [DefaultMessage]).Select(Encoding.UTF8.GetBytes));
        _blockClose = blockClose;
        _blockCloseOutput = blockCloseOutput;
        _state = initialState;
        _receiveException = receiveException;
        _sendException = sendException;
    }

    /// <summary>Records whether the array-segment receive overload was called.</summary>
    public bool ArraySegmentReceiveCalled { get; private set; }

    /// <summary>Records whether the memory receive overload was called.</summary>
    public bool MemoryReceiveCalled { get; private set; }

    /// <summary>Records whether full graceful close was attempted.</summary>
    public bool CloseCalled { get; private set; }

    /// <summary>Records whether a close response was attempted.</summary>
    public bool CloseOutputCalled { get; private set; }

    /// <summary>Records whether the connection aborted the socket.</summary>
    public bool AbortCalled { get; private set; }

    /// <summary>Records whether the connection disposed the socket.</summary>
    public bool DisposeCalled { get; private set; }

    /// <summary>The complete text of every message passed to the send overload, in call order.</summary>
    public List<string> SentMessages { get; } = [];

    /// <inheritdoc/>
    public override WebSocketCloseStatus? CloseStatus => null;

    /// <inheritdoc/>
    public override string? CloseStatusDescription => null;

    /// <inheritdoc/>
    public override WebSocketState State => _state;

    /// <inheritdoc/>
    public override string? SubProtocol => null;

    /// <inheritdoc/>
    public override void Abort()
    {
        AbortCalled = true;
        _state = WebSocketState.Aborted;
        _closeCompletion.TrySetResult();
    }

    /// <inheritdoc/>
    public override Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        CloseCalled = true;
        if (_blockClose)
        {
            return _closeCompletion.Task;
        }

        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        CloseOutputCalled = true;
        if (_blockCloseOutput)
        {
            return _closeCompletion.Task;
        }

        _state = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        DisposeCalled = true;
        _closeCompletion.TrySetResult();
    }

    /// <inheritdoc/>
    public override Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        ArraySegmentReceiveCalled = true;
        if (_receiveException is not null)
        {
            return Task.FromException<WebSocketReceiveResult>(_receiveException);
        }
        byte[] message = _receiveMessages.Dequeue();
        message.AsSpan().CopyTo(buffer.AsSpan());
        return Task.FromResult(new WebSocketReceiveResult(message.Length, WebSocketMessageType.Text, true));
    }

    /// <inheritdoc/>
    public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        MemoryReceiveCalled = true;
        if (_receiveException is not null)
        {
            return ValueTask.FromException<ValueWebSocketReceiveResult>(_receiveException);
        }
        byte[] message = _receiveMessages.Dequeue();
        message.AsSpan().CopyTo(buffer.Span);
        return ValueTask.FromResult(new ValueWebSocketReceiveResult(message.Length, WebSocketMessageType.Text, true));
    }

    /// <inheritdoc/>
    public override Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        if (_sendException is not null)
        {
            return Task.FromException(_sendException);
        }
        SentMessages.Add(Encoding.UTF8.GetString(buffer));
        return Task.CompletedTask;
    }
}
