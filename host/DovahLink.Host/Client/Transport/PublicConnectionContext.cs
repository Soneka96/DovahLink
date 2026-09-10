using DovahLink.Host.State;

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
    /// <param name="lane">The reserved-capacity lane to admit this message onto.</param>
    /// <returns><see langword="true"/> when the message was accepted onto <paramref name="lane"/>'s bounded outbound queue.</returns>
    bool TrySend(ReadOnlyMemory<byte> payload, PublicOutboundLane lane);

    /// <summary>
    /// Attempts to enqueue a Snapshot value for the owning connection's writer to send. See
    /// <see cref="IPublicWebSocketConnection.TrySendSnapshot"/> for the replaceable-slot, non-closing
    /// delivery contract this forwards to.
    /// </summary>
    /// <param name="areaId">The state area this snapshot value belongs to.</param>
    /// <param name="payload">The complete message payload to send.</param>
    /// <returns><see langword="true"/> when the value is now the pending snapshot for <paramref name="areaId"/>.</returns>
    bool TrySendSnapshot(StateAreaId areaId, ReadOnlyMemory<byte> payload);

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
    public bool TrySend(ReadOnlyMemory<byte> payload, PublicOutboundLane lane) => connection.TrySend(payload, lane);

    /// <inheritdoc/>
    public bool TrySendSnapshot(StateAreaId areaId, ReadOnlyMemory<byte> payload) => connection.TrySendSnapshot(areaId, payload);

    /// <inheritdoc/>
    public void RequestClose() => connection.RequestClose();
}
