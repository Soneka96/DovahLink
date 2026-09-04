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

        registry.InvalidateAllForClient(targetClient, SessionInvalidationReason.Revoked);

        Assert.False(registry.IsActive(firstSession, firstConnection));
        Assert.False(registry.IsActive(secondSession, secondConnection));
        Assert.True(registry.IsActive(otherSession, otherConnection));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>
    /// Verifies that the invalidation operation preserves each returned target's authentication
    /// source, per <c>ai/context/common.md</c>'s domain-modeling rule that a complete, immutable
    /// target must carry every field a later concept needs rather than only the filtering result.
    /// </summary>
    [Fact]
    public void InvalidateAllForClient_PreservesAuthenticationSourceInTarget()
    {
        var registry = new SessionRegistry();
        ClientId clientId = ClientId.NewId();
        Assert.True(registry.TryCreate(
            clientId, ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _));

        IReadOnlyList<SessionInvalidationTarget> targets = registry.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked);

        SessionInvalidationTarget target = Assert.Single(targets);
        Assert.Equal(SessionAuthenticationSource.TrustedDeviceCredential, target.AuthenticationSource);
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

        registry.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked);

        Assert.True(registry.IsActive(developerSession, developerConnection));
        Assert.False(registry.IsActive(trustedSession, trustedConnection));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>
    /// Verifies that batch invalidation removes every session belonging to any of several clients in
    /// one call, while leaving an unrelated client's session active -- the batch counterpart to
    /// <see cref="InvalidateAllForClient_InvalidatesOnlyThatClientsSessions"/>.
    /// </summary>
    [Fact]
    public void InvalidateAllForClients_InvalidatesEveryListedClientsSessions()
    {
        var registry = new SessionRegistry(3);
        ClientId first = ClientId.NewId();
        ClientId second = ClientId.NewId();
        ClientId unrelated = ClientId.NewId();
        ConnectionId firstConnection = ConnectionId.NewId();
        ConnectionId secondConnection = ConnectionId.NewId();
        ConnectionId unrelatedConnection = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            first, firstConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId firstSession));
        Assert.True(registry.TryCreate(
            second, secondConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId secondSession));
        Assert.True(registry.TryCreate(
            unrelated, unrelatedConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId unrelatedSession));

        IReadOnlyList<SessionInvalidationTarget> targets = registry.InvalidateAllForClients([first, second], SessionInvalidationReason.TrustReset);

        Assert.Equal(2, targets.Count);
        Assert.False(registry.IsActive(firstSession, firstConnection));
        Assert.False(registry.IsActive(secondSession, secondConnection));
        Assert.True(registry.IsActive(unrelatedSession, unrelatedConnection));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>
    /// Verifies that batch invalidation exempts a developer-token session the same way
    /// <see cref="InvalidateAllForClient_DeveloperTokenSession_IsExempt"/> proves for the single-client
    /// path.
    /// </summary>
    [Fact]
    public void InvalidateAllForClients_DeveloperTokenSession_IsExempt()
    {
        var registry = new SessionRegistry(2);
        ClientId clientId = ClientId.NewId();
        ConnectionId developerConnection = ConnectionId.NewId();
        ConnectionId trustedConnection = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            clientId, developerConnection, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId developerSession));
        Assert.True(registry.TryCreate(
            clientId, trustedConnection, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId trustedSession));

        registry.InvalidateAllForClients([clientId], SessionInvalidationReason.TrustReset);

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

        registry.InvalidateAll(SessionInvalidationReason.FactoryReset);

        Assert.False(registry.IsActive(sessionId, connectionId));
        Assert.Equal(0, registry.ActiveCount);
    }

    /// <summary>Verifies that unconditional invalidation also preserves each returned target's authentication source.</summary>
    [Fact]
    public void InvalidateAll_PreservesAuthenticationSourceInTarget()
    {
        var registry = new SessionRegistry();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out _));

        IReadOnlyList<SessionInvalidationTarget> targets = registry.InvalidateAll(SessionInvalidationReason.FactoryReset);

        SessionInvalidationTarget target = Assert.Single(targets);
        Assert.Equal(SessionAuthenticationSource.OneTimeLocalToken, target.AuthenticationSource);
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

        registry.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked);

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
            .Append(Task.Run(() => registry.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked)))
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
                    registry.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked);
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

        registry.InvalidateAll(SessionInvalidationReason.FactoryReset);

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

    /// <summary>Verifies that a freshly created session finalizes for its owner.</summary>
    [Fact]
    public void TryFinalizeAdmission_OwnedActiveSession_ReturnsTrue()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        Assert.True(registry.TryFinalizeAdmission(sessionId, connectionId));
    }

    /// <summary>Verifies that an unknown session cannot finalize.</summary>
    [Fact]
    public void TryFinalizeAdmission_UnknownSession_ReturnsFalse()
    {
        var registry = new SessionRegistry();

        Assert.False(registry.TryFinalizeAdmission(SessionId.NewId(), ConnectionId.NewId()));
    }

    /// <summary>Verifies that a session cannot finalize on a connection other than its owner.</summary>
    [Fact]
    public void TryFinalizeAdmission_WrongConnection_ReturnsFalse()
    {
        var registry = new SessionRegistry();
        ConnectionId owner = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), owner, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        Assert.False(registry.TryFinalizeAdmission(sessionId, ConnectionId.NewId()));
    }

    /// <summary>
    /// Verifies the linearization guarantee <see cref="ISessionRegistry.TryFinalizeAdmission"/>
    /// exists for: a session removed by <see cref="ISessionRegistry.InvalidateAll"/> (Factory Reset)
    /// after it was reserved can never finalize, regardless of how much earlier the reservation
    /// itself succeeded.
    /// </summary>
    [Fact]
    public void TryFinalizeAdmission_SessionInvalidatedByInvalidateAll_ReturnsFalse()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        registry.InvalidateAll(SessionInvalidationReason.FactoryReset);

        Assert.False(registry.TryFinalizeAdmission(sessionId, connectionId));
    }

    /// <summary>Verifies the same linearization guarantee against a targeted per-client invalidation.</summary>
    [Fact]
    public void TryFinalizeAdmission_SessionInvalidatedByInvalidateAllForClient_ReturnsFalse()
    {
        var registry = new SessionRegistry();
        ClientId clientId = ClientId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        registry.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked);

        Assert.False(registry.TryFinalizeAdmission(sessionId, connectionId));
    }

    /// <summary>Verifies the same linearization guarantee against the owner's own targeted invalidation.</summary>
    [Fact]
    public void TryFinalizeAdmission_SessionInvalidatedByOwner_ReturnsFalse()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        registry.Invalidate(sessionId, connectionId);

        Assert.False(registry.TryFinalizeAdmission(sessionId, connectionId));
    }

    /// <summary>
    /// Verifies that concurrent admission finalization and an unconditional global invalidation never
    /// leave the registry internally inconsistent: whichever order the two operations actually run in
    /// for a given session, <see cref="ISessionRegistry.InvalidateAll"/> having completed means every
    /// session is unconditionally gone, regardless of how many concurrent finalize calls raced it.
    /// </summary>
    [Fact]
    public async Task ConcurrentFinalizeAdmissionAndInvalidateAll_LeavesRegistryConsistent()
    {
        var registry = new SessionRegistry(32);
        (SessionId SessionId, ConnectionId ConnectionId)[] sessions = Enumerable.Range(0, 16)
            .Select(_ =>
            {
                ConnectionId connectionId = ConnectionId.NewId();
                registry.TryCreate(
                    ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);
                return (sessionId, connectionId);
            })
            .ToArray();

        Task[] operations = sessions
            .Select(session => Task.Run(() => registry.TryFinalizeAdmission(session.SessionId, session.ConnectionId)))
            .Append(Task.Run(() => { registry.InvalidateAll(SessionInvalidationReason.FactoryReset); }))
            .ToArray();

        await Task.WhenAll(operations);

        Assert.Equal(0, registry.ActiveCount);
        Assert.All(sessions, session => Assert.False(registry.IsActive(session.SessionId, session.ConnectionId)));
    }

    /// <summary>Verifies that an owned Restricted session upgrades to Full in place, without a new session id.</summary>
    [Fact]
    public void TryUpgradeToFullTrust_OwnedRestrictedSession_UpgradesInPlace()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));

        Assert.True(registry.TryUpgradeToFullTrust(sessionId, connectionId));

        Assert.Equal(SessionTrustTier.Full, registry.TrustTierFor(sessionId));
        Assert.True(registry.IsActive(sessionId, connectionId));
        Assert.Equal(1, registry.ActiveCount);
    }

    /// <summary>Verifies that upgrading an already-Full session is a harmless no-op success.</summary>
    [Fact]
    public void TryUpgradeToFullTrust_AlreadyFullSession_RemainsFull()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        Assert.True(registry.TryUpgradeToFullTrust(sessionId, connectionId));

        Assert.Equal(SessionTrustTier.Full, registry.TrustTierFor(sessionId));
    }

    /// <summary>Verifies that a connection cannot upgrade a session it does not own.</summary>
    [Fact]
    public void TryUpgradeToFullTrust_WrongConnection_ReturnsFalseAndLeavesTierUnchanged()
    {
        var registry = new SessionRegistry();
        ConnectionId owner = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), owner, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));

        Assert.False(registry.TryUpgradeToFullTrust(sessionId, ConnectionId.NewId()));

        Assert.Equal(SessionTrustTier.Restricted, registry.TrustTierFor(sessionId));
    }

    /// <summary>Verifies that an unknown session cannot be upgraded.</summary>
    [Fact]
    public void TryUpgradeToFullTrust_UnknownSession_ReturnsFalse()
    {
        var registry = new SessionRegistry();

        Assert.False(registry.TryUpgradeToFullTrust(SessionId.NewId(), ConnectionId.NewId()));
    }

    /// <summary>Verifies that an already-invalidated session cannot be upgraded.</summary>
    [Fact]
    public void TryUpgradeToFullTrust_InvalidatedSession_ReturnsFalse()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));
        registry.Invalidate(sessionId, connectionId);

        Assert.False(registry.TryUpgradeToFullTrust(sessionId, connectionId));
    }

    /// <summary>Verifies that a session removed by client-scoped invalidation cannot be upgraded.</summary>
    [Fact]
    public void TryUpgradeToFullTrust_SessionInvalidatedByInvalidateAllForClient_ReturnsFalse()
    {
        var registry = new SessionRegistry();
        ClientId clientId = ClientId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            clientId, connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));
        registry.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked);

        Assert.False(registry.TryUpgradeToFullTrust(sessionId, connectionId));
    }

    /// <summary>Verifies that a session removed by global invalidation cannot be upgraded.</summary>
    [Fact]
    public void TryUpgradeToFullTrust_SessionInvalidatedByInvalidateAll_ReturnsFalse()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));
        registry.InvalidateAll(SessionInvalidationReason.FactoryReset);

        Assert.False(registry.TryUpgradeToFullTrust(sessionId, connectionId));
    }

    /// <summary>Verifies that an owned active session's linearized action runs exactly once and its result is returned.</summary>
    [Fact]
    public void TryExecuteIfActive_OwnedActiveSession_InvokesActionAndReturnsResult()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));
        int callCount = 0;

        bool executed = registry.TryExecuteIfActive(sessionId, connectionId, () => { callCount++; return "mutated"; }, out string? result);

        Assert.True(executed);
        Assert.Equal(1, callCount);
        Assert.Equal("mutated", result);
    }

    /// <summary>Verifies that an unknown session never reaches the action at all.</summary>
    [Fact]
    public void TryExecuteIfActive_UnknownSession_ReturnsFalseAndNeverInvokesAction()
    {
        var registry = new SessionRegistry();
        int callCount = 0;

        bool executed = registry.TryExecuteIfActive(SessionId.NewId(), ConnectionId.NewId(), () => { callCount++; return 0; }, out int result);

        Assert.False(executed);
        Assert.Equal(0, callCount);
        Assert.Equal(0, result);
    }

    /// <summary>Verifies that a session known only on a different connection never reaches the action.</summary>
    [Fact]
    public void TryExecuteIfActive_WrongConnection_ReturnsFalseAndNeverInvokesAction()
    {
        var registry = new SessionRegistry();
        ConnectionId owner = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), owner, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));
        int callCount = 0;

        bool executed = registry.TryExecuteIfActive(sessionId, ConnectionId.NewId(), () => { callCount++; return 0; }, out int result);

        Assert.False(executed);
        Assert.Equal(0, callCount);
    }

    /// <summary>
    /// Verifies the core stale-request scenario this method exists for: once a session is invalidated
    /// -- simulating an administrative mutation that committed before the request's own resumed
    /// execution reaches this linearization point -- the action can never run, regardless of how much
    /// earlier the session was genuinely active.
    /// </summary>
    [Fact]
    public void TryExecuteIfActive_SessionAlreadyInvalidated_ReturnsFalseAndNeverInvokesAction()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));
        registry.Invalidate(sessionId, connectionId);
        int callCount = 0;

        bool executed = registry.TryExecuteIfActive(sessionId, connectionId, () => { callCount++; return 0; }, out int result);

        Assert.False(executed);
        Assert.Equal(0, callCount);
    }

    /// <summary>
    /// Proves the actual mutual-exclusion guarantee this method exists for, with a genuine cross-thread
    /// race rather than a probabilistic repetition: while the action is still running inside this
    /// call's critical section, a concurrent <see cref="ISessionRegistry.Invalidate"/> for the exact
    /// same session on another thread must be blocked on the same internal lock and cannot complete
    /// until the action returns and this call releases it.
    /// </summary>
    [Fact]
    public void TryExecuteIfActive_ConcurrentInvalidate_BlocksUntilActionReturns()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));
        using var invalidateStarted = new ManualResetEventSlim(false);
        using var invalidateCompleted = new ManualResetEventSlim(false);
        Thread invalidatingThread = new(() =>
        {
            invalidateStarted.Set();
            registry.Invalidate(sessionId, connectionId);
            invalidateCompleted.Set();
        });

        // The invalidating thread is started from inside the still-running action, still holding this
        // call's own critical section, so it can only ever observe that section as already taken.
        bool executed = registry.TryExecuteIfActive(sessionId, connectionId, () =>
        {
            invalidatingThread.Start();
            Assert.True(invalidateStarted.Wait(TimeSpan.FromSeconds(5)));
            // The invalidating thread has started and is blocked trying to acquire the same internal
            // lock this action is still running inside; it must not have been able to complete yet.
            // Joining it here (rather than merely checking the event) would deadlock this very thread,
            // since the invalidating thread cannot proceed until this critical section is released.
            Assert.False(invalidateCompleted.Wait(TimeSpan.FromMilliseconds(200)));
            return true;
        }, out bool result);

        Assert.True(executed);
        Assert.True(result);
        Assert.True(invalidatingThread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(invalidateCompleted.IsSet);
        Assert.False(registry.IsActive(sessionId, connectionId));
    }

    /// <summary>
    /// Verifies that an action which throws still releases the internal critical section: the
    /// exception propagates to the caller instead of being swallowed, and a later call for the same
    /// session is not left permanently blocked behind a lock the faulted action never released.
    /// </summary>
    [Fact]
    public void TryExecuteIfActive_ActionThrows_PropagatesAndReleasesTheCriticalSection()
    {
        var registry = new SessionRegistry();
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(registry.TryCreate(
            ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId));

        Assert.Throws<InvalidOperationException>(() =>
            registry.TryExecuteIfActive<int>(sessionId, connectionId, () => throw new InvalidOperationException("boom"), out _));

        bool executedAfterward = registry.TryExecuteIfActive(sessionId, connectionId, () => true, out bool result);
        Assert.True(executedAfterward);
        Assert.True(result);
    }
}
