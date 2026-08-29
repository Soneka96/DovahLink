using DovahLink.Host.Identity;

namespace DovahLink.Host.Sessions;

/// <summary>
/// Owns the host's WebSocket session identities. A session is valid only for the connection it was
/// created for; a reconnect for the same client always creates a fresh session rather than reusing
/// or reactivating a prior one.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>Creates a new, active session for a client.</summary>
    /// <param name="clientId">The client the session belongs to.</param>
    /// <returns>The new session's identifier.</returns>
    SessionId Create(ClientId clientId);

    /// <summary>Invalidates one session. A dead session can never become valid again.</summary>
    /// <param name="sessionId">The session to invalidate.</param>
    void Invalidate(SessionId sessionId);

    /// <summary>Invalidates every currently active session belonging to a client.</summary>
    /// <param name="clientId">The client whose sessions should be invalidated.</param>
    void InvalidateAllForClient(ClientId clientId);

    /// <summary>Reports whether a session is currently active.</summary>
    /// <param name="sessionId">The session to check.</param>
    /// <returns><see langword="true"/> if the session exists and has not been invalidated.</returns>
    bool IsActive(SessionId sessionId);
}

/// <summary>
/// An in-memory session registry. Session state does not persist across a host restart: a
/// restarted host starts with no sessions, matching <c>ai/context/host/architecture.md</c>'s
/// "Restart behavior" (every existing <c>sessionId</c> is already invalidated the moment its
/// socket closes, and a host restart ends every client session the same way).
/// </summary>
public sealed class SessionRegistry : ISessionRegistry
{
    /// <summary>Guards <see cref="sessionsById"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Every session created during this registry's lifetime, keyed by session id.</summary>
    private readonly Dictionary<SessionId, ActiveSessionRecord> sessionsById = new();

    /// <inheritdoc/>
    public SessionId Create(ClientId clientId)
    {
        SessionId sessionId = SessionId.NewId();
        lock (gate)
        {
            sessionsById[sessionId] = new ActiveSessionRecord(sessionId, clientId, SessionState.Active);
        }

        return sessionId;
    }

    /// <inheritdoc/>
    public void Invalidate(SessionId sessionId)
    {
        lock (gate)
        {
            if (sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record))
            {
                sessionsById[sessionId] = record with { State = SessionState.Invalidated };
            }
        }
    }

    /// <inheritdoc/>
    public void InvalidateAllForClient(ClientId clientId)
    {
        lock (gate)
        {
            foreach (SessionId sessionId in sessionsById.Keys.ToList())
            {
                ActiveSessionRecord record = sessionsById[sessionId];
                if (record.ClientId.Equals(clientId))
                {
                    sessionsById[sessionId] = record with { State = SessionState.Invalidated };
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool IsActive(SessionId sessionId)
    {
        lock (gate)
        {
            return sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) && record.State == SessionState.Active;
        }
    }
}
