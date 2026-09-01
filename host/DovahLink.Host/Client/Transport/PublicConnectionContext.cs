namespace DovahLink.Host.Client.Transport;

/// <summary>
/// A narrow, per-connection transport capability handed to application code that consumes one
/// inbound message: it can send a response on the exact connection that delivered the message and
/// request that connection's own orderly close, without owning or ever seeing the underlying
/// WebSocket, stream, or socket. An implementation is scoped to exactly one connection's lifetime;
/// it must never resolve or address a different connection.
/// </summary>
public interface IPublicConnectionContext
{
    /// <summary>
    /// Attempts to enqueue an outbound message for the owning connection's writer to send. See
    /// <see cref="IPublicWebSocketConnection.TrySend"/> for the bounded/serialized delivery
    /// contract this forwards to.
    /// </summary>
    /// <param name="payload">The complete message payload to send.</param>
    /// <returns><see langword="true"/> when the message was accepted onto the bounded outbound queue.</returns>
    bool TrySend(ReadOnlyMemory<byte> payload);

    /// <summary>
    /// Requests the owning connection's own orderly close. See
    /// <see cref="IPublicWebSocketConnection.RequestClose"/> for the drain and teardown contract
    /// this forwards to.
    /// </summary>
    void RequestClose();
}

/// <inheritdoc cref="IPublicConnectionContext"/>
public sealed class PublicConnectionContext : IPublicConnectionContext
{
    /// <summary>The exact connection this context is scoped to for its entire lifetime.</summary>
    private readonly IPublicWebSocketConnection connection;

    /// <summary>Creates a context scoped to one connection.</summary>
    /// <param name="connection">The exact connection this context is scoped to for its entire lifetime.</param>
    public PublicConnectionContext(IPublicWebSocketConnection connection)
    {
        this.connection = connection;
    }

    /// <inheritdoc/>
    public bool TrySend(ReadOnlyMemory<byte> payload) => connection.TrySend(payload);

    /// <inheritdoc/>
    public void RequestClose() => connection.RequestClose();
}
