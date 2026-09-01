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
    /// <param name="connection">
    /// The narrow, per-connection capability scoped to the exact connection that delivered
    /// <paramref name="payload"/>: a response sent through it reaches this same connection's peer,
    /// and a close requested through it targets this same connection, never a different one.
    /// </param>
    /// <param name="payload">
    /// The complete message payload. Backed by a buffer the connection reuses for the next message; a
    /// handler that needs the bytes beyond the synchronous extent of this call must copy them first.
    /// </param>
    /// <param name="cancellationToken">The token used to stop waiting if handling itself awaits.</param>
    Task HandleMessageAsync(IPublicConnectionContext connection, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    /// <summary>
    /// Mandatorily invalidates any session bound to this connection before the transport's admission
    /// slot can be released. Called exactly once per connection that completed its WebSocket upgrade,
    /// synchronously inside <see cref="IPublicWebSocketConnection.RunAsync"/>'s teardown, before that
    /// call can return and before <see cref="HandleDisconnectedAsync"/> runs. This member is
    /// deliberately synchronous rather than awaitable: an implementation must be a fast, deterministic,
    /// idempotent-safe operation (for example releasing an in-memory session record under a lock) and
    /// must never block on I/O or another connection's teardown -- that is what
    /// <see cref="HandleDisconnectedAsync"/> exists for instead.
    /// </summary>
    void HandleConnectionEnded();

    /// <summary>
    /// Gives the handler a bounded, best-effort opportunity to run additional disconnect cleanup or
    /// notification after <see cref="HandleConnectionEnded"/> has already completed. Called exactly
    /// once per connection that completed its WebSocket upgrade, always before
    /// <see cref="IPublicWebSocketConnection.RunAsync"/> returns. Failure or a timeout here is
    /// tolerated by the transport and never prevents or delays its own teardown or the admission
    /// slot's release, so this member must not be relied on for anything the connection's own
    /// lifecycle correctness depends on -- use <see cref="HandleConnectionEnded"/> for that.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop waiting if the notification itself awaits.</param>
    Task HandleDisconnectedAsync(CancellationToken cancellationToken);
}
