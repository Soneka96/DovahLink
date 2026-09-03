using DovahLink.Host.Identity;

namespace DovahLink.Host.Sessions;

/// <summary>
/// Owns the host's WebSocket session identities. A session is valid only for the connection it was
/// created for; a reconnect for the same client always creates a fresh session. Only active records
/// are retained, keeping memory bounded by the admission capacity.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>Attempts to create a new session for a client and owning connection.</summary>
    /// <param name="clientId">The client the session belongs to.</param>
    /// <param name="connectionId">The transport connection that owns the session.</param>
    /// <param name="authenticationSource">How the owning connection authenticated at <c>hello</c>. Security-significant state; callers must always state it explicitly.</param>
    /// <param name="trustTier">The session's initial message-authorization tier. Security-significant state; callers must always state it explicitly.</param>
    /// <param name="sessionId">Receives the new session identifier when admission succeeds.</param>
    /// <returns><see langword="true"/> when capacity admitted the session.</returns>
    bool TryCreate(
        ClientId clientId,
        ConnectionId connectionId,
        SessionAuthenticationSource authenticationSource,
        SessionTrustTier trustTier,
        out SessionId sessionId);

    /// <summary>Invalidates one session when called by its owning connection.</summary>
    /// <param name="sessionId">The session to invalidate.</param>
    /// <param name="connectionId">The connection claiming ownership of the session.</param>
    void Invalidate(SessionId sessionId, ConnectionId connectionId);

    /// <summary>
    /// Invalidates every currently active session belonging to a client, except a session whose
    /// <see cref="ActiveSessionRecord.AuthenticationSource"/> is <see cref="SessionAuthenticationSource.OneTimeLocalToken"/>:
    /// a developer-token session is never a Known Device and must not be disconnected merely because
    /// its self-declared <see cref="ClientId"/> matches a client-scoped administrative mutation's
    /// target. Use <see cref="InvalidateAll"/> for Factory Reset's unconditional invalidation instead.
    /// </summary>
    /// <param name="clientId">The client whose sessions should be invalidated.</param>
    void InvalidateAllForClient(ClientId clientId);

    /// <summary>
    /// Reports whether a session is active on its owning connection. For the one-time admission
    /// commit itself, use <see cref="TryFinalizeAdmission"/> instead: this method is for an ongoing,
    /// repeatable liveness check on an already-admitted session.
    /// </summary>
    /// <param name="sessionId">The session to check.</param>
    /// <param name="connectionId">The connection claiming ownership of the session.</param>
    /// <returns><see langword="true"/> if the session belongs to the connection and remains active.</returns>
    bool IsActive(SessionId sessionId, ConnectionId connectionId);

    /// <summary>Unconditionally invalidates every currently active session.</summary>
    void InvalidateAll();

    /// <summary>
    /// Atomically confirms that a session <see cref="TryCreate"/> admitted is still active on its
    /// owning connection, as the sole authoritative linearization point between an in-flight
    /// admission and a concurrent <see cref="Invalidate"/>, <see cref="InvalidateAllForClient"/>, or
    /// <see cref="InvalidateAll"/> call racing against it: because this check and every invalidation
    /// method serialize on the same internal lock, whichever reaches this exact session first decides
    /// its outcome for every check made after it. A caller must call this exactly once, as the last
    /// registry interaction before performing an admission side effect that would be wrong to perform
    /// for an already-invalidated session (sending <c>hello_ack</c>, committing a reserved one-time
    /// token), and must not perform that side effect when this returns <see langword="false"/>.
    /// Because the side effect itself necessarily runs after this method returns and releases the
    /// lock, an invalidation that begins only after this call already returned <see langword="true"/>
    /// is not observed by that call: such a session is invalidated within nanoseconds of admission and
    /// is rejected on its very next message by <see cref="IsActive"/>, so no persistently usable or
    /// inconsistent session can result, but the one <c>hello_ack</c>/<c>capabilities</c> pair already
    /// in flight at that point cannot be recalled.
    /// </summary>
    /// <param name="sessionId">The session to finalize.</param>
    /// <param name="connectionId">The connection claiming ownership of the session.</param>
    /// <returns><see langword="true"/> if the session still belongs to the connection and remains active.</returns>
    bool TryFinalizeAdmission(SessionId sessionId, ConnectionId connectionId);
}

/// <inheritdoc cref="ISessionRegistry"/>
public sealed class SessionRegistry : ISessionRegistry
{
    /// <summary>Guards <see cref="sessionsById"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The maximum number of active sessions admitted at once.</summary>
    private readonly int maxActiveSessions;

    /// <summary>Every active session, keyed by its session id.</summary>
    private readonly Dictionary<SessionId, ActiveSessionRecord> sessionsById = new();

    /// <summary>Creates a registry with an explicit active-session admission bound.</summary>
    /// <param name="maxActiveSessions">The maximum number of simultaneous active sessions.</param>
    public SessionRegistry(int maxActiveSessions = Constants.MaxActiveSessions)
    {
        if (maxActiveSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxActiveSessions));
        }

        this.maxActiveSessions = maxActiveSessions;
    }

    /// <summary>The current number of active sessions.</summary>
    public int ActiveCount
    {
        get
        {
            lock (gate)
            {
                return sessionsById.Count;
            }
        }
    }

    /// <summary>The maximum number of simultaneous active sessions.</summary>
    public int MaxActiveSessions => maxActiveSessions;

    /// <inheritdoc/>
    public bool TryCreate(
        ClientId clientId,
        ConnectionId connectionId,
        SessionAuthenticationSource authenticationSource,
        SessionTrustTier trustTier,
        out SessionId sessionId)
    {
        lock (gate)
        {
            if (sessionsById.Count >= maxActiveSessions)
            {
                sessionId = default;
                return false;
            }

            do
            {
                sessionId = SessionId.NewId();
            }
            while (sessionsById.ContainsKey(sessionId));

            sessionsById[sessionId] = new ActiveSessionRecord(
                sessionId, clientId, connectionId, SessionState.Active, authenticationSource, trustTier);
            return true;
        }
    }

    /// <inheritdoc/>
    public void Invalidate(SessionId sessionId, ConnectionId connectionId)
    {
        lock (gate)
        {
            if (sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) && record.ConnectionId == connectionId)
            {
                sessionsById.Remove(sessionId);
            }
        }
    }

    /// <inheritdoc/>
    public void InvalidateAllForClient(ClientId clientId)
    {
        lock (gate)
        {
            foreach (SessionId sessionId in sessionsById
                .Where(pair => pair.Value.ClientId.Equals(clientId) &&
                    pair.Value.AuthenticationSource != SessionAuthenticationSource.OneTimeLocalToken)
                .Select(pair => pair.Key)
                .ToList())
            {
                sessionsById.Remove(sessionId);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsActive(SessionId sessionId, ConnectionId connectionId)
    {
        lock (gate)
        {
            return sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) &&
                record.ConnectionId == connectionId &&
                record.State == SessionState.Active;
        }
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        lock (gate)
        {
            sessionsById.Clear();
        }
    }

    /// <inheritdoc/>
    public bool TryFinalizeAdmission(SessionId sessionId, ConnectionId connectionId)
    {
        lock (gate)
        {
            return sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) &&
                record.ConnectionId == connectionId &&
                record.State == SessionState.Active;
        }
    }
}
