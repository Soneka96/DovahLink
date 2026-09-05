using DovahLink.Host.Identity;
using DovahLink.Host.Security;

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
    /// The returned snapshot is captured while this registry's internal lock is held and handed back
    /// only after it is released, so a caller can safely use it to attempt transport work afterward
    /// without ever running that work under this registry's lock.
    /// </summary>
    /// <param name="clientId">The client whose sessions should be invalidated.</param>
    /// <param name="reason">The authoritative reason these sessions are being invalidated.</param>
    /// <returns>An immutable snapshot of every session this call actually invalidated.</returns>
    IReadOnlyList<SessionInvalidationTarget> InvalidateAllForClient(ClientId clientId, SessionInvalidationReason reason);

    /// <summary>
    /// Invalidates every currently active session belonging to any of several clients in one atomic
    /// pass under this registry's own internal lock, applying the same developer-token exemption as
    /// <see cref="InvalidateAllForClient"/>. Exists so a multi-client administrative mutation (for
    /// example Reset Trust revoking several devices at once) can remove every affected client's
    /// authorization before any target's best-effort notification/close is attempted, rather than
    /// authorizing client B to keep operating while client A's teardown is still in flight -- a
    /// sequential per-client call to <see cref="InvalidateAllForClient"/> cannot give that guarantee.
    /// The returned snapshot is captured while this registry's internal lock is held and handed back
    /// only after it is released, so a caller can safely use it to attempt transport work afterward
    /// without ever running that work under this registry's lock.
    /// </summary>
    /// <param name="clientIds">The clients whose sessions should be invalidated.</param>
    /// <param name="reason">The authoritative reason these sessions are being invalidated.</param>
    /// <returns>An immutable snapshot of every session this call actually invalidated, across every client.</returns>
    IReadOnlyList<SessionInvalidationTarget> InvalidateAllForClients(IReadOnlyList<ClientId> clientIds, SessionInvalidationReason reason);

    /// <summary>
    /// Reports whether a session is active on its owning connection. For the one-time admission
    /// commit itself, use <see cref="TryFinalizeAdmission"/> instead: this method is for an ongoing,
    /// repeatable liveness check on an already-admitted session.
    /// </summary>
    /// <param name="sessionId">The session to check.</param>
    /// <param name="connectionId">The connection claiming ownership of the session.</param>
    /// <returns><see langword="true"/> if the session belongs to the connection and remains active.</returns>
    bool IsActive(SessionId sessionId, ConnectionId connectionId);

    /// <summary>
    /// Unconditionally invalidates every currently active session. The returned snapshot is captured
    /// while this registry's internal lock is held and handed back only after it is released, so a
    /// caller can safely use it to attempt transport work afterward without ever running that work
    /// under this registry's lock.
    /// </summary>
    /// <param name="reason">The authoritative reason every session is being invalidated.</param>
    /// <returns>An immutable snapshot of every session this call invalidated.</returns>
    IReadOnlyList<SessionInvalidationTarget> InvalidateAll(SessionInvalidationReason reason);

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

    /// <summary>
    /// Upgrades an active session's <see cref="ActiveSessionRecord.TrustTier"/> to
    /// <see cref="SessionTrustTier.Full"/> in place, without minting a new <see cref="SessionId"/> or
    /// otherwise disturbing the session record. Used by a successful <c>pairing_ack</c> to upgrade the
    /// same connection's own session exactly once, per <c>ai/context/protocol/security.md</c>'s
    /// "Trust-tier upgrade happens exactly once, on the pairing state machine's own success."
    /// </summary>
    /// <param name="sessionId">The session to upgrade.</param>
    /// <param name="connectionId">The connection claiming ownership of the session.</param>
    /// <returns><see langword="true"/> if the session belonged to the connection and remained active.</returns>
    bool TryUpgradeToFullTrust(SessionId sessionId, ConnectionId connectionId);

    /// <summary>
    /// Establishes this exact session incarnation's authorization linearization point: verifies that
    /// <paramref name="sessionId"/> is still active on <paramref name="connectionId"/> and, only if so,
    /// invokes <paramref name="action"/> -- both inside the one internal critical section every
    /// invalidation method (<see cref="Invalidate"/>, <see cref="InvalidateAllForClient"/>,
    /// <see cref="InvalidateAllForClients"/>, <see cref="InvalidateAll"/>) also serializes on. This
    /// closes the check-then-act gap a separate <see cref="IsActive"/> call followed by an unguarded
    /// mutation would leave open: whichever of this call or a concurrent invalidation reaches that
    /// shared critical section first deterministically decides the outcome for the other, with no third
    /// possibility where authorization succeeds, an invalidation lands, and this call's own mutation
    /// still proceeds afterward against already-superseded state.
    /// </summary>
    /// <typeparam name="T">The type <paramref name="action"/> returns.</typeparam>
    /// <param name="sessionId">The session whose exact incarnation must still be active.</param>
    /// <param name="connectionId">The connection claiming ownership of the session.</param>
    /// <param name="action">
    /// The narrow, synchronous, bounded state mutation to run while this exact session incarnation is
    /// confirmed active. Must never await, perform persistence, adapter, or transport I/O, or call back
    /// into this registry from another thread -- doing so would hold this registry's internal lock
    /// across work an invalidation elsewhere could otherwise complete instantly, turning a bounded
    /// critical section into an unbounded one.
    /// </param>
    /// <param name="result">
    /// <paramref name="action"/>'s return value when this call returns <see langword="true"/>; the
    /// default value of <typeparamref name="T"/> otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> once <paramref name="action"/> has run because the session was still
    /// active; <see langword="false"/> without invoking <paramref name="action"/> at all if the session
    /// was already invalidated, unknown, or owned by a different connection.
    /// </returns>
    bool TryExecuteIfActive<T>(SessionId sessionId, ConnectionId connectionId, Func<T> action, out T result);
}

/// <inheritdoc cref="ISessionRegistry"/>
public sealed class SessionRegistry : ISessionRegistry
{
    /// <summary>
    /// The linearization point every method here shares with <see cref="Trust.TrustStore"/>'s own
    /// administrative-mutation publish, so a client's trust record changing and the sessions that
    /// change affects becoming unauthorized are always one indivisible event to every other caller of
    /// either type -- see <see cref="Trust.ITrustStore.RevokeAsync"/>'s own <c>onPublished</c> remarks.
    /// Also this registry's own internal mutual exclusion, replacing what used to be a private
    /// <c>object</c> field: every method here still serializes on this exact same gate the way it
    /// always serialized on that field, so <see cref="TryExecuteIfActive{T}"/>'s own documented
    /// guarantee against a concurrent invalidation is entirely unchanged.
    /// </summary>
    private readonly ISecurityStateGate securityStateGate;

    /// <summary>The maximum number of active sessions admitted at once.</summary>
    private readonly int maxActiveSessions;

    /// <summary>Every active session, keyed by its session id.</summary>
    private readonly Dictionary<SessionId, ActiveSessionRecord> sessionsById = new();

    /// <summary>Creates a registry with an explicit active-session admission bound.</summary>
    /// <param name="securityStateGate">The linearization point shared with <see cref="Trust.TrustStore"/>.</param>
    /// <param name="maxActiveSessions">The maximum number of simultaneous active sessions.</param>
    public SessionRegistry(ISecurityStateGate securityStateGate, int maxActiveSessions = Constants.MaxActiveSessions)
    {
        if (maxActiveSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxActiveSessions));
        }

        this.securityStateGate = securityStateGate;
        this.maxActiveSessions = maxActiveSessions;
    }

    /// <summary>The current number of active sessions.</summary>
    public int ActiveCount
    {
        get
        {
            securityStateGate.Enter();
            try
            {
                return sessionsById.Count;
            }
            finally
            {
                securityStateGate.Exit();
            }
        }
    }

    /// <summary>The maximum number of simultaneous active sessions.</summary>
    public int MaxActiveSessions => maxActiveSessions;

    /// <summary>Returns the current trust tier of an active session, for diagnostics and tests.</summary>
    /// <param name="sessionId">The session to look up.</param>
    /// <exception cref="KeyNotFoundException"><paramref name="sessionId"/> is not currently active.</exception>
    public SessionTrustTier TrustTierFor(SessionId sessionId)
    {
        securityStateGate.Enter();
        try
        {
            return sessionsById[sessionId].TrustTier;
        }
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public bool TryCreate(
        ClientId clientId,
        ConnectionId connectionId,
        SessionAuthenticationSource authenticationSource,
        SessionTrustTier trustTier,
        out SessionId sessionId)
    {
        securityStateGate.Enter();
        try
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
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public void Invalidate(SessionId sessionId, ConnectionId connectionId)
    {
        securityStateGate.Enter();
        try
        {
            if (sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) && record.ConnectionId == connectionId)
            {
                sessionsById.Remove(sessionId);
            }
        }
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<SessionInvalidationTarget> InvalidateAllForClient(ClientId clientId, SessionInvalidationReason reason)
    {
        securityStateGate.Enter();
        try
        {
            var targets = new List<SessionInvalidationTarget>();
            foreach (SessionId sessionId in sessionsById
                .Where(pair => pair.Value.ClientId.Equals(clientId) &&
                    pair.Value.AuthenticationSource != SessionAuthenticationSource.OneTimeLocalToken)
                .Select(pair => pair.Key)
                .ToList())
            {
                ActiveSessionRecord record = sessionsById[sessionId];
                targets.Add(new SessionInvalidationTarget(sessionId, record.ConnectionId, record.ClientId, reason, record.AuthenticationSource));
                sessionsById.Remove(sessionId);
            }

            return targets;
        }
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<SessionInvalidationTarget> InvalidateAllForClients(IReadOnlyList<ClientId> clientIds, SessionInvalidationReason reason)
    {
        var clientIdSet = new HashSet<ClientId>(clientIds);
        securityStateGate.Enter();
        try
        {
            var targets = new List<SessionInvalidationTarget>();
            foreach (SessionId sessionId in sessionsById
                .Where(pair => clientIdSet.Contains(pair.Value.ClientId) &&
                    pair.Value.AuthenticationSource != SessionAuthenticationSource.OneTimeLocalToken)
                .Select(pair => pair.Key)
                .ToList())
            {
                ActiveSessionRecord record = sessionsById[sessionId];
                targets.Add(new SessionInvalidationTarget(sessionId, record.ConnectionId, record.ClientId, reason, record.AuthenticationSource));
                sessionsById.Remove(sessionId);
            }

            return targets;
        }
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public bool IsActive(SessionId sessionId, ConnectionId connectionId)
    {
        securityStateGate.Enter();
        try
        {
            return sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) &&
                record.ConnectionId == connectionId &&
                record.State == SessionState.Active;
        }
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<SessionInvalidationTarget> InvalidateAll(SessionInvalidationReason reason)
    {
        securityStateGate.Enter();
        try
        {
            List<SessionInvalidationTarget> targets = sessionsById.Values
                .Select(record => new SessionInvalidationTarget(record.SessionId, record.ConnectionId, record.ClientId, reason, record.AuthenticationSource))
                .ToList();
            sessionsById.Clear();
            return targets;
        }
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public bool TryFinalizeAdmission(SessionId sessionId, ConnectionId connectionId)
    {
        securityStateGate.Enter();
        try
        {
            return sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) &&
                record.ConnectionId == connectionId &&
                record.State == SessionState.Active;
        }
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public bool TryUpgradeToFullTrust(SessionId sessionId, ConnectionId connectionId)
    {
        securityStateGate.Enter();
        try
        {
            if (!sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) ||
                record.ConnectionId != connectionId ||
                record.State != SessionState.Active)
            {
                return false;
            }

            sessionsById[sessionId] = record with { TrustTier = SessionTrustTier.Full };
            return true;
        }
        finally
        {
            securityStateGate.Exit();
        }
    }

    /// <inheritdoc/>
    public bool TryExecuteIfActive<T>(SessionId sessionId, ConnectionId connectionId, Func<T> action, out T result)
    {
        securityStateGate.Enter();
        try
        {
            if (!sessionsById.TryGetValue(sessionId, out ActiveSessionRecord? record) ||
                record.ConnectionId != connectionId || record.State != SessionState.Active)
            {
                result = default!;
                return false;
            }

            result = action();
            return true;
        }
        finally
        {
            securityStateGate.Exit();
        }
    }
}
