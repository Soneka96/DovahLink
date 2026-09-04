using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>An in-memory stand-in for <see cref="ISessionRegistry"/> that records which clients were mass-invalidated.</summary>
public sealed class FakeSessionRegistry : ISessionRegistry
{
    private readonly object gate = new();

    private readonly int maxActiveSessions;

    /// <summary>The client each currently active session belongs to.</summary>
    private readonly Dictionary<SessionId, ActiveSessionRecord> activeSessions = new();

    /// <summary>Connection owners retained only so tests can assert ownership after invalidation.</summary>
    private readonly Dictionary<SessionId, ConnectionId> sessionConnections = new();

    /// <summary>Creates a synchronized fake with a configurable admission bound.</summary>
    public FakeSessionRegistry(int maxActiveSessions = int.MaxValue)
    {
        if (maxActiveSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxActiveSessions));
        }

        this.maxActiveSessions = maxActiveSessions;
    }

    /// <summary>Every client id and reason passed to <see cref="InvalidateAllForClient"/>, in call order.</summary>
    private readonly List<(ClientId ClientId, SessionInvalidationReason Reason)> invalidateAllForClientCalls = [];

    /// <summary>
    /// When set, invoked synchronously by <see cref="TryCreate"/> after the new session record is
    /// stored and <see cref="gate"/> is released, letting a test deterministically run a concurrent
    /// invalidation against the just-created session before the caller's own next step observes it.
    /// </summary>
    public Action? AfterCreate { get; set; }

    /// <summary>
    /// Optional hook invoked with a short label immediately after <see cref="InvalidateAllForClient"/>
    /// or <see cref="InvalidateAll"/> applies (<c>"InvalidateAllForClient"</c> or <c>"InvalidateAll"</c>),
    /// letting a test build a cross-collaborator call-order timeline together with
    /// <see cref="FakeTrustStore.OnMutationApplied"/> and <see cref="FakePairingCoordinator.OnMutationApplied"/>.
    /// </summary>
    public Action<string>? OnMutationApplied { get; set; }

    /// <summary>A synchronized snapshot of client-wide invalidation calls and their exact reasons.</summary>
    public IReadOnlyList<(ClientId ClientId, SessionInvalidationReason Reason)> InvalidateAllForClientCalls
    {
        get
        {
            lock (gate)
            {
                return invalidateAllForClientCalls.ToList();
            }
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
        lock (gate)
        {
            if (activeSessions.Count >= maxActiveSessions)
            {
                sessionId = default;
                return false;
            }

            sessionId = SessionId.NewId();
            activeSessions[sessionId] = new ActiveSessionRecord(
                sessionId, clientId, connectionId, SessionState.Active, authenticationSource, trustTier);
            sessionConnections[sessionId] = connectionId;
        }

        AfterCreate?.Invoke();
        return true;
    }

    /// <inheritdoc/>
    public void Invalidate(SessionId sessionId, ConnectionId connectionId)
    {
        lock (gate)
        {
            if (activeSessions.TryGetValue(sessionId, out ActiveSessionRecord? record) && record.ConnectionId == connectionId)
            {
                activeSessions.Remove(sessionId);
            }
        }
    }

    /// <summary>
    /// Creates a fake session for tests that do not inspect the generated connection identity or care
    /// about authentication source/trust tier, defaulting to a trusted-device, fully-trusted session.
    /// </summary>
    public SessionId Create(ClientId clientId) => Create(clientId, ConnectionId.NewId());

    /// <summary>
    /// Creates a fake session with an explicit connection identity, defaulting to a trusted-device,
    /// fully-trusted session for tests that do not care about authentication source/trust tier.
    /// </summary>
    public SessionId Create(ClientId clientId, ConnectionId connectionId)
    {
        if (!TryCreate(
            clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId))
        {
            throw new InvalidOperationException("The active session capacity has been reached.");
        }

        return sessionId;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SessionInvalidationTarget> InvalidateAllForClient(ClientId clientId, SessionInvalidationReason reason)
    {
        List<SessionInvalidationTarget> targets;
        lock (gate)
        {
            invalidateAllForClientCalls.Add((clientId, reason));
            targets = [];
            foreach (SessionId sessionId in activeSessions
                .Where(pair => pair.Value.ClientId.Equals(clientId) &&
                    pair.Value.AuthenticationSource != SessionAuthenticationSource.OneTimeLocalToken)
                .Select(pair => pair.Key)
                .ToList())
            {
                ActiveSessionRecord record = activeSessions[sessionId];
                targets.Add(new SessionInvalidationTarget(sessionId, record.ConnectionId, record.ClientId, reason, record.AuthenticationSource));
                activeSessions.Remove(sessionId);
            }
        }

        OnMutationApplied?.Invoke("InvalidateAllForClient");
        return targets;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SessionInvalidationTarget> InvalidateAllForClients(IReadOnlyList<ClientId> clientIds, SessionInvalidationReason reason)
    {
        var clientIdSet = new HashSet<ClientId>(clientIds);
        List<SessionInvalidationTarget> targets;
        lock (gate)
        {
            targets = [];
            foreach (SessionId sessionId in activeSessions
                .Where(pair => clientIdSet.Contains(pair.Value.ClientId) &&
                    pair.Value.AuthenticationSource != SessionAuthenticationSource.OneTimeLocalToken)
                .Select(pair => pair.Key)
                .ToList())
            {
                ActiveSessionRecord record = activeSessions[sessionId];
                targets.Add(new SessionInvalidationTarget(sessionId, record.ConnectionId, record.ClientId, reason, record.AuthenticationSource));
                activeSessions.Remove(sessionId);
            }
        }

        OnMutationApplied?.Invoke("InvalidateAllForClients");
        return targets;
    }

    /// <inheritdoc/>
    public bool IsActive(SessionId sessionId, ConnectionId connectionId)
    {
        lock (gate)
        {
            return activeSessions.TryGetValue(sessionId, out ActiveSessionRecord? record) && record.ConnectionId == connectionId;
        }
    }

    /// <summary>Returns the owner identity for a session created by this test double.</summary>
    public ConnectionId ConnectionIdFor(SessionId sessionId)
    {
        lock (gate)
        {
            return sessionConnections[sessionId];
        }
    }

    /// <summary>The number of times <see cref="InvalidateAll"/> has been called.</summary>
    public int InvalidateAllCallCount
    {
        get
        {
            lock (gate)
            {
                return invalidateAllCallCount;
            }
        }
    }

    private int invalidateAllCallCount;

    /// <inheritdoc/>
    public IReadOnlyList<SessionInvalidationTarget> InvalidateAll(SessionInvalidationReason reason)
    {
        List<SessionInvalidationTarget> targets;
        lock (gate)
        {
            invalidateAllCallCount++;
            targets = activeSessions.Values
                .Select(record => new SessionInvalidationTarget(record.SessionId, record.ConnectionId, record.ClientId, reason, record.AuthenticationSource))
                .ToList();
            activeSessions.Clear();
        }

        OnMutationApplied?.Invoke("InvalidateAll");
        return targets;
    }

    /// <summary>Creates a fake session with an explicit connection identity, authentication source, and trust tier.</summary>
    public SessionId Create(
        ClientId clientId,
        ConnectionId connectionId,
        SessionAuthenticationSource authenticationSource,
        SessionTrustTier trustTier)
    {
        if (!TryCreate(clientId, connectionId, authenticationSource, trustTier, out SessionId sessionId))
        {
            throw new InvalidOperationException("The active session capacity has been reached.");
        }

        return sessionId;
    }

    /// <summary>The current number of active sessions, mirroring <see cref="SessionRegistry.ActiveCount"/>.</summary>
    public int ActiveCount
    {
        get
        {
            lock (gate)
            {
                return activeSessions.Count;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryFinalizeAdmission(SessionId sessionId, ConnectionId connectionId)
    {
        lock (gate)
        {
            return activeSessions.TryGetValue(sessionId, out ActiveSessionRecord? record) && record.ConnectionId == connectionId;
        }
    }

    /// <inheritdoc/>
    public bool TryUpgradeToFullTrust(SessionId sessionId, ConnectionId connectionId)
    {
        lock (gate)
        {
            if (!activeSessions.TryGetValue(sessionId, out ActiveSessionRecord? record) || record.ConnectionId != connectionId)
            {
                return false;
            }

            activeSessions[sessionId] = record with { TrustTier = SessionTrustTier.Full };
            return true;
        }
    }

    /// <summary>Returns the current trust tier for a session created by this test double.</summary>
    public SessionTrustTier TrustTierFor(SessionId sessionId)
    {
        lock (gate)
        {
            return activeSessions[sessionId].TrustTier;
        }
    }
}
