using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Sessions;

/// <summary>
/// Maps an admitted session to the exact live public connection that owns it, so
/// <see cref="ISessionTerminationNotifier"/> can reach that connection to send a best-effort
/// terminal notification and force it closed without any WebSocket implementation type crossing
/// into the trust/pairing/session layers that decide invalidation. Populated only by the public
/// admission handler, at admission and at connection end; trust and pairing code never write to it.
/// </summary>
public interface IPublicSessionConnectionRegistry
{
    /// <summary>Registers the live connection an admitted session is now bound to.</summary>
    /// <param name="sessionId">The admitted session's identifier.</param>
    /// <param name="connectionId">The connection's own identity, unique for its entire lifetime.</param>
    /// <param name="connection">The live connection to register.</param>
    void Register(SessionId sessionId, ConnectionId connectionId, IPublicConnectionContext connection);

    /// <summary>
    /// Removes a connection's registration, regardless of whether it was ever admitted. A no-op if
    /// no registration exists for <paramref name="connectionId"/>.
    /// </summary>
    /// <param name="connectionId">The connection whose registration to remove.</param>
    void Unregister(ConnectionId connectionId);

    /// <summary>
    /// Looks up the live connection currently registered for the exact session and connection
    /// identity, or <see langword="null"/> when no match exists -- including when the connection
    /// already ended, or when <paramref name="connectionId"/> is registered under a different
    /// session than <paramref name="sessionId"/>.
    /// </summary>
    /// <param name="sessionId">The session identity to match.</param>
    /// <param name="connectionId">The connection identity to match.</param>
    IPublicConnectionContext? TryGet(SessionId sessionId, ConnectionId connectionId);
}

/// <inheritdoc cref="IPublicSessionConnectionRegistry"/>
public sealed class PublicSessionConnectionRegistry : IPublicSessionConnectionRegistry
{
    /// <summary>Guards <see cref="entriesByConnectionId"/>.</summary>
    private readonly object gate = new();

    /// <summary>The currently registered live connections, keyed by connection identity.</summary>
    private readonly Dictionary<ConnectionId, (SessionId SessionId, IPublicConnectionContext Connection)> entriesByConnectionId = [];

    /// <inheritdoc/>
    public void Register(SessionId sessionId, ConnectionId connectionId, IPublicConnectionContext connection)
    {
        lock (gate)
        {
            entriesByConnectionId[connectionId] = (sessionId, connection);
        }
    }

    /// <inheritdoc/>
    public void Unregister(ConnectionId connectionId)
    {
        lock (gate)
        {
            entriesByConnectionId.Remove(connectionId);
        }
    }

    /// <inheritdoc/>
    public IPublicConnectionContext? TryGet(SessionId sessionId, ConnectionId connectionId)
    {
        lock (gate)
        {
            return entriesByConnectionId.TryGetValue(connectionId, out var entry) && entry.SessionId == sessionId
                ? entry.Connection
                : null;
        }
    }
}
