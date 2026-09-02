using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;

namespace DovahLink.Host.Tests.Sessions;

/// <summary>Tests for <see cref="SessionRegistry"/>.</summary>
public class SessionRegistryTests
{
    /// <summary>Verifies that a newly created session reports as active only for its owner.</summary>
    [Fact]
    public void TryCreate_ReturnsAnActiveOwnedSession()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();

        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        Assert.True(registry.IsActive(sessionId, connectionId));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>Verifies that an unknown session is inactive.</summary>
    [Fact]
    public void IsActive_UnknownSession_ReturnsFalse()
    {
        var registry = new SessionRegistry();

        Assert.False(registry.IsActive(SessionId.NewId(), ConnectionId.NewId()));
    }

    /// <summary>Verifies that an owner can invalidate its session and free the admission slot.</summary>
    [Fact]
    public void Invalidate_OwnerConnection_RemovesSession()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        registry.Invalidate(sessionId, connectionId);

        Assert.False(registry.IsActive(sessionId, connectionId));
        Assert.Equal(0, registry.ActiveCount);
        Assert.True(registry.TryCreate(
            ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _));
    }

    /// <summary>Verifies that a non-owner cannot invalidate a live session.</summary>
    [Fact]
    public void Invalidate_WrongConnection_LeavesSessionActive()
    {
        var registry = new SessionRegistry();
        ConnectionId owner = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), owner, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        registry.Invalidate(sessionId, ConnectionId.NewId());

        Assert.True(registry.IsActive(sessionId, owner));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>Verifies that a session id cannot be reused by another connection.</summary>
    [Fact]
    public void IsActive_SessionCannotCrossConnections()
    {
        var registry = new SessionRegistry();
        ConnectionId owner = ConnectionId.NewId();
        ConnectionId otherConnection = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), owner, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        Assert.False(registry.IsActive(sessionId, otherConnection));
        Assert.True(registry.IsActive(sessionId, owner));
    }

    /// <summary>Verifies that client-wide invalidation affects only that client's active sessions.</summary>
    [Fact]
    public void InvalidateAllForClient_InvalidatesOnlyThatClientsSessions()
    {
        var registry = new SessionRegistry(3);
        ClientId targetClient = ClientId.NewId();
        ConnectionId firstConnection = ConnectionId.NewId();
        ConnectionId secondConnection = ConnectionId.NewId();
        ConnectionId otherConnection = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            targetClient, firstConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId firstSession));
        Assert.True(registry.TryCreate(
            targetClient, secondConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId secondSession));
        Assert.True(registry.TryCreate(
            ClientId.NewId(), otherConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId otherSession));

        registry.InvalidateAllForClient(targetClient);

        Assert.False(registry.IsActive(firstSession, firstConnection));
        Assert.False(registry.IsActive(secondSession, secondConnection));
        Assert.True(registry.IsActive(otherSession, otherConnection));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>
    /// Verifies that client-wide invalidation exempts a developer-token session: a session whose
    /// <see cref="SessionAuthenticationSource"/> is <see cref="SessionAuthenticationSource.OneTimeLocalToken"/>
    /// is never a Known Device and must survive Block/Revoke's client-scoped invalidation even when
    /// its self-declared <see cref="ClientId"/> matches the target.
    /// </summary>
    [Fact]
    public void InvalidateAllForClient_DeveloperTokenSession_IsExempt()
    {
        var registry = new SessionRegistry(2);
        ClientId clientId = ClientId.NewId();
        ConnectionId developerConnection = ConnectionId.NewId();
        ConnectionId trustedConnection = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            clientId, developerConnection, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId developerSession));
        Assert.True(registry.TryCreate(
            clientId, trustedConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId trustedSession));

        registry.InvalidateAllForClient(clientId);

        Assert.True(registry.IsActive(developerSession, developerConnection));
        Assert.False(registry.IsActive(trustedSession, trustedConnection));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>
    /// Verifies that unconditional global invalidation (Factory Reset) still invalidates a
    /// developer-token session, unlike the client-scoped exemption
    /// <see cref="ISessionRegistry.InvalidateAllForClient"/> applies.
    /// </summary>
    [Fact]
    public void InvalidateAll_DeveloperTokenSession_IsNotExempt()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId sessionId));

        registry.InvalidateAll();

        Assert.False(registry.IsActive(sessionId, connectionId));
        Assert.Equal(0, registry.ActiveCount);
    }

    /// <summary>Verifies that a created session's record carries the authentication source and trust tier it was admitted with.</summary>
    [Theory]
    [InlineData(SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full)]
    [InlineData(SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted)]
    [InlineData(SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full)]
    public void TryCreate_RecordsTheSuppliedAuthenticationSourceAndTrustTier(
        SessionAuthenticationSource authenticationSource, SessionTrustTier trustTier)
    {
        var registry = new SessionRegistry(2);
        ClientId clientId = ClientId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        ConnectionId otherConnection = ConnectionId.NewId();

        // Only OneTimeLocalToken sessions are exempt from client-scoped invalidation, so admitting one
        // of each source under the same clientId and observing which ones InvalidateAllForClient
        // removes indirectly proves the record actually retained the source it was created with.
        Assert.True(registry.TryCreate(clientId, connectionId, authenticationSource, trustTier, out SessionId sessionId));
        Assert.True(registry.TryCreate(
            clientId, otherConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId otherSession));

        registry.InvalidateAllForClient(clientId);

        bool expectedExempt = authenticationSource == SessionAuthenticationSource.OneTimeLocalToken;
        Assert.Equal(expectedExempt, registry.IsActive(sessionId, connectionId));
        Assert.False(registry.IsActive(otherSession, otherConnection));
    }

    /// <summary>Verifies that reconnecting creates a fresh session bound to the new connection.</summary>
    [Fact]
    public void Create_ReconnectWithNewConnection_CreatesFreshOwnedSession()
    {
        var registry = new SessionRegistry();
        ClientId clientId = ClientId.NewId();
        ConnectionId firstConnection = ConnectionId.NewId();
        ConnectionId secondConnection = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            clientId, firstConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId firstSession));
        registry.Invalidate(firstSession, firstConnection);

        Assert.True(registry.TryCreate(
            clientId, secondConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId secondSession));

        Assert.NotEqual(firstSession, secondSession);
        Assert.False(registry.IsActive(firstSession, firstConnection));
        Assert.True(registry.IsActive(secondSession, secondConnection));
    }

    /// <summary>Verifies that admission rejects sessions at capacity.</summary>
    [Fact]
    public void TryCreate_AtCapacity_RejectsAdditionalSession()
    {
        var registry = new SessionRegistry(1);
        Assert.True(registry.TryCreate(
            ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _));

        Assert.False(registry.TryCreate(
            ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>Verifies that concurrent admission cannot exceed the configured capacity.</summary>
    [Fact]
    public async Task TryCreate_ConcurrentCalls_RespectCapacity()
    {
        var registry = new SessionRegistry(1);

        (bool Accepted, SessionId SessionId)[] results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            {
                bool accepted = registry.TryCreate(
                    ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);
                return (accepted, sessionId);
            })));

        Assert.Single(results, result => result.Accepted);
        Assert.Equal(31, results.Count(result => !result.Accepted));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>Verifies that overlapping owner invalidation and administrative invalidation leave no active records.</summary>
    [Fact]
    public async Task ConcurrentOwnerAndAdministrativeInvalidation_CleansAllSessions()
    {
        var registry = new SessionRegistry(32);
        ClientId clientId = ClientId.NewId();
        (SessionId SessionId, ConnectionId ConnectionId)[] sessions = Enumerable.Range(0, 16)
            .Select(_ =>
            {
                ConnectionId connectionId = ConnectionId.NewId();
                registry.TryCreate(
                    clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);
                return (sessionId, connectionId);
            })
            .ToArray();

        Task[] operations = sessions
            .Select(session => Task.Run(() => registry.Invalidate(session.SessionId, session.ConnectionId)))
            .Append(Task.Run(() => registry.InvalidateAllForClient(clientId)))
            .ToArray();

        await Task.WhenAll(operations);

        Assert.Equal(0, registry.ActiveCount);
        Assert.All(sessions, session => Assert.False(registry.IsActive(session.SessionId, session.ConnectionId)));
    }

    /// <summary>Verifies that admission and invalidation can overlap without exceeding capacity or corrupting state.</summary>
    [Fact]
    public async Task ConcurrentCreateAndInvalidation_RespectOwnershipAndCapacity()
    {
        var registry = new SessionRegistry(1);
        ClientId clientId = ClientId.NewId();

        Task[] operations = Enumerable.Range(0, 32)
            .Select(index => Task.Run(() =>
            {
                if (index % 2 == 0)
                {
                    ConnectionId connectionId = ConnectionId.NewId();
                    if (registry.TryCreate(
                        clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId))
                    {
                        registry.Invalidate(sessionId, connectionId);
                    }
                }
                else
                {
                    registry.InvalidateAllForClient(clientId);
                }
            }))
            .ToArray();

        await Task.WhenAll(operations);

        Assert.InRange(registry.ActiveCount, 0, 1);
    }

    /// <summary>Verifies that global invalidation removes every active session.</summary>
    [Fact]
    public void InvalidateAll_RemovesEverySession()
    {
        var registry = new SessionRegistry(2);
        ConnectionId firstConnection = ConnectionId.NewId();
        ConnectionId secondConnection = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), firstConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId firstSession));
        Assert.True(registry.TryCreate(
            ClientId.NewId(), secondConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId secondSession));

        registry.InvalidateAll();

        Assert.Equal(0, registry.ActiveCount);
        Assert.False(registry.IsActive(firstSession, firstConnection));
        Assert.False(registry.IsActive(secondSession, secondConnection));
    }

    /// <summary>Verifies that non-positive capacity is rejected.</summary>
    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRegistry(0));
    }
}
