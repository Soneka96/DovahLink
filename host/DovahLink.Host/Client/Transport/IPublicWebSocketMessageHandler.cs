namespace DovahLink.Host.Client.Transport;

/// <summary>
/// Consumes complete inbound public WebSocket messages accepted within
/// <see cref="PublicWebSocketConnection"/>'s bounds. The transport performs no protocol decoding of
/// its own; a later concept implements this contract to interpret message content and manage the
/// session bound to the connection.
/// </summary>
public interface IPublicWebSocketMessageHandler
{
    /// <summary>Handles one complete inbound text message, encoded exactly as received from the peer.</summary>
    /// <param name="payload">
    /// The complete message payload. Backed by a buffer the connection reuses for the next message; a
    /// handler that needs the bytes beyond the synchronous extent of this call must copy them first.
    /// </param>
    /// <param name="cancellationToken">The token used to stop waiting if handling itself awaits.</param>
    Task HandleMessageAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    /// <summary>
    /// Notifies the handler that the transport connection has ended, so any session bound to it can
    /// be invalidated before the transport releases its connection slot. Called exactly once per
    /// connection that completed its WebSocket upgrade, always before
    /// <see cref="IPublicWebSocketConnection.RunAsync"/> returns.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop waiting if the notification itself awaits.</param>
    Task HandleDisconnectedAsync(CancellationToken cancellationToken);
}
