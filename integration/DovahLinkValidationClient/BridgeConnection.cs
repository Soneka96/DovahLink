using System.Net.WebSockets;
using System.Text;

namespace DovahLinkValidationClient;

/// Independently sends and receives complete text messages over the bridge WebSocket.
public sealed class BridgeConnection : IAsyncDisposable
{
    /// The default time allowed for one complete inbound message.
    private static readonly TimeSpan DefaultReceiveTimeout = TimeSpan.FromSeconds(10);

    /// The WebSocket owned by this connection.
    private readonly ClientWebSocket _socket = new();

    /// <summary>
    /// Connects the WebSocket to the specified URI.
    /// </summary>
    /// <param name="uri">The URI of the WebSocket endpoint.</param>
    /// <param name="cancellationToken">The token used to cancel the connection attempt.</param>
    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        await _socket.ConnectAsync(uri, cancellationToken);
    }

    /// <summary>
    /// Sends an encoded envelope as a complete text message.
    /// </summary>
    /// <param name="envelope">The envelope to send.</param>
    /// <param name="cancellationToken">The token used to cancel the send operation.</param>
    public async Task SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        await SendRawTextAsync(envelope.Encode(), cancellationToken);
    }

    /// <summary>
    /// Sends arbitrary text as a complete UTF-8 WebSocket message.
    /// </summary>
    /// <param name="text">The text to send.</param>
    public async Task SendRawTextAsync(string text, CancellationToken cancellationToken = default)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    /// <summary>
    /// Receives and decodes one complete text message into an envelope.
    /// </summary>
    /// <param name="timeout">The maximum time allowed to receive the complete message; uses the default receive timeout when omitted.</param>
    /// <param name="cancellationToken">A token that cancels the receive operation.</param>
    /// <returns>The decoded envelope.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the peer closes the connection or sends a non-text message.</exception>
    public async Task<Envelope> ReceiveAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? DefaultReceiveTimeout);

        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException(
                    $"Bridge closed the connection: {result.CloseStatus} {result.CloseStatusDescription}");
            }
            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidOperationException(
                    $"Expected a text message per protocol/schema/README.md, got {result.MessageType}.");
            }
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Envelope.Decode(Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>
    /// Immediately terminates the WebSocket connection without a closing handshake.
    /// </summary>
    public void Abort() => _socket.Abort();

    /// <summary>
    /// Attempts to gracefully close the WebSocket connection.
    /// </summary>
    /// <remarks>
    /// Sends a close request for an open connection or a close response when the peer has already initiated closure. All failures are suppressed.
    /// </remarks>
    /// <param name="cancellationToken">The token used to cancel the close operation.</param>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            switch (_socket.State)
            {
                case WebSocketState.Open:
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                    break;
                case WebSocketState.CloseReceived:
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                    break;
            }
        }
        catch (Exception)
        {
            // See method summary: intentionally swallowed.
        }
    }

    /// <summary>
    /// Attempts to establish a connection, retrying failed attempts with a configurable delay.
    /// </summary>
    /// <param name="uri">The URI to connect to.</param>
    /// <param name="maxAttempts">The maximum number of connection attempts.</param>
    /// <param name="delayBetweenAttempts">The delay between failed attempts, or 50 milliseconds when omitted.</param>
    /// <returns>The first successfully connected instance.</returns>
    /// <exception cref="TimeoutException">Thrown when all connection attempts fail.</exception>
    public static async Task<BridgeConnection> ConnectWithRetryAsync(
        Uri uri, int maxAttempts = 20, TimeSpan? delayBetweenAttempts = null)
    {
        TimeSpan delay = delayBetweenAttempts ?? TimeSpan.FromMilliseconds(50);
        Exception? lastError = null;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var connection = new BridgeConnection();
            try
            {
                await connection.ConnectAsync(uri);
                return connection;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await connection.DisposeAsync();
                await Task.Delay(delay);
            }
        }

        throw new TimeoutException($"Could not connect to {uri} in time.", lastError);
    }

    /// <summary>
    /// Gracefully closes the connection and disposes the underlying WebSocket.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync();
        }
        finally
        {
            _socket.Dispose();
        }
    }
}
