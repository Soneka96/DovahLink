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

    /// <summary>Every client id passed to <see cref="InvalidateAllForClient"/>, in call order.</summary>
    private readonly List<ClientId> invalidateAllForClientCalls = [];

    /// <summary>A synchronized snapshot of client-wide invalidation calls.</summary>
    public IReadOnlyList<ClientId> InvalidateAllForClientCalls
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
            return true;
        }
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
    public void InvalidateAllForClient(ClientId clientId)
    {
        lock (gate)
        {
            invalidateAllForClientCalls.Add(clientId);
            foreach (SessionId sessionId in activeSessions
                .Where(pair => pair.Value.ClientId.Equals(clientId) &&
                    pair.Value.AuthenticationSource != SessionAuthenticationSource.OneTimeLocalToken)
                .Select(pair => pair.Key)
                .ToList())
            {
                activeSessions.Remove(sessionId);
            }
        }
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
    public void InvalidateAll()
    {
        lock (gate)
        {
            invalidateAllCallCount++;
            activeSessions.Clear();
        }
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
}
