using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Sessions;

/// <summary>Tests for <see cref="ClientSessionInvalidator"/>.</summary>
public class ClientSessionInvalidatorTests
{
    /// <summary>Verifies that client-scoped invalidation notifies every affected session with the exact reason given.</summary>
    [Fact]
    public async Task InvalidateClientAsync_NotifiesEveryAffectedSessionWithExactReason()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        sessionRegistry.TryCreate(clientId, ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);

        await invalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.Revoked);

        SessionInvalidationTarget target = Assert.Single(notifier.NotifiedTargets);
        Assert.Equal(sessionId, target.SessionId);
        Assert.Equal(clientId, target.ClientId);
        Assert.Equal(SessionInvalidationReason.Revoked, target.Reason);
        Assert.Equal(SessionAuthenticationSource.TrustedDeviceCredential, target.AuthenticationSource);
    }

    /// <summary>Verifies that client-scoped invalidation through the seam still exempts a developer-token session.</summary>
    [Fact]
    public async Task InvalidateClientAsync_ExcludesOneTimeLocalTokenSession()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        ConnectionId developerConnection = ConnectionId.NewId();
        sessionRegistry.TryCreate(clientId, developerConnection, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId developerSession);

        await invalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.Blocked);

        Assert.Empty(notifier.NotifiedTargets);
        Assert.True(sessionRegistry.IsActive(developerSession, developerConnection));
    }

    /// <summary>
    /// Verifies that batch invalidation notifies every affected session across every listed client
    /// with the exact reason given, and still exempts a developer-token session -- the multi-client
    /// counterpart to <see cref="InvalidateClientAsync_NotifiesEveryAffectedSessionWithExactReason"/>
    /// and <see cref="InvalidateClientAsync_ExcludesOneTimeLocalTokenSession"/>.
    /// </summary>
    [Fact]
    public async Task InvalidateClientsAsync_NotifiesEveryAffectedSessionAcrossEveryClientAndExcludesDeveloperToken()
    {
        var sessionRegistry = new SessionRegistry(3);
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId first = ClientId.NewId();
        ClientId second = ClientId.NewId();
        ClientId developerClient = ClientId.NewId();
        sessionRegistry.TryCreate(first, ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId firstSession);
        sessionRegistry.TryCreate(second, ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId secondSession);
        ConnectionId developerConnection = ConnectionId.NewId();
        sessionRegistry.TryCreate(developerClient, developerConnection, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId developerSession);

        await invalidator.InvalidateClientsAsync([first, second, developerClient], SessionInvalidationReason.TrustReset);

        Assert.Equal(2, notifier.NotifiedTargets.Count);
        Assert.All(notifier.NotifiedTargets, target => Assert.Equal(SessionInvalidationReason.TrustReset, target.Reason));
        Assert.Equal(
            new[] { firstSession, secondSession }.OrderBy(id => id.Value),
            notifier.NotifiedTargets.Select(target => target.SessionId).OrderBy(id => id.Value));
        Assert.True(sessionRegistry.IsActive(developerSession, developerConnection));
    }

    /// <summary>Verifies that unconditional invalidation through the seam still reaches a developer-token session.</summary>
    [Fact]
    public async Task InvalidateAllAsync_IncludesOneTimeLocalTokenSession()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ConnectionId connectionId = ConnectionId.NewId();
        sessionRegistry.TryCreate(ClientId.NewId(), connectionId, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId sessionId);

        await invalidator.InvalidateAllAsync(SessionInvalidationReason.FactoryReset);

        SessionInvalidationTarget target = Assert.Single(notifier.NotifiedTargets);
        Assert.Equal(sessionId, target.SessionId);
        Assert.Equal(SessionInvalidationReason.FactoryReset, target.Reason);
        Assert.Equal(SessionAuthenticationSource.OneTimeLocalToken, target.AuthenticationSource);
        Assert.False(sessionRegistry.IsActive(sessionId, connectionId));
    }

    /// <summary>
    /// Verifies the security-mandated ordering: a session is already unauthorized in the registry --
    /// removed, per <see cref="ISessionRegistry.IsActive"/> -- before its best-effort terminal
    /// notification is ever attempted.
    /// </summary>
    [Fact]
    public async Task InvalidateClientAsync_SessionIsUnauthorizedBeforeNotificationAttempted()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        sessionRegistry.TryCreate(clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);
        notifier.OnNotify = target => Assert.False(sessionRegistry.IsActive(target.SessionId, target.ConnectionId));

        await invalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.Revoked);

        Assert.Single(notifier.NotifiedTargets);
    }

    /// <summary>
    /// Verifies that a notification failure for one target never propagates to the caller and never
    /// prevents the remaining targets from being attempted -- the trust mutation that already
    /// committed must not observe this best-effort step's failure.
    /// </summary>
    [Fact]
    public async Task InvalidateAllAsync_NotifierFailure_DoesNotPropagateAndStillNotifiesOtherTargets()
    {
        var sessionRegistry = new SessionRegistry(2);
        var notifier = new FakeSessionTerminationNotifier { ThrowOnNotify = new InvalidOperationException("transport unavailable") };
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        Assert.True(sessionRegistry.TryCreate(ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _));
        Assert.True(sessionRegistry.TryCreate(ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _));

        await invalidator.InvalidateAllAsync(SessionInvalidationReason.FactoryReset);

        Assert.Equal(2, notifier.NotifiedTargets.Count);
    }

    /// <summary>
    /// Verifies that a notification failure never resurrects or otherwise preserves the session: it
    /// remains gone from the registry regardless of the best-effort step's own outcome.
    /// </summary>
    [Fact]
    public async Task InvalidateClientAsync_NotifierFailure_DoesNotResurrectSession()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier { ThrowOnNotify = new InvalidOperationException("transport unavailable") };
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        sessionRegistry.TryCreate(clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);

        await invalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.Blocked);

        Assert.False(sessionRegistry.IsActive(sessionId, connectionId));
    }

    /// <summary>Verifies that invalidating a client with no active sessions completes cleanly without attempting any notification.</summary>
    [Fact]
    public async Task InvalidateClientAsync_NoActiveSessions_CompletesWithoutNotifying()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);

        await invalidator.InvalidateClientAsync(ClientId.NewId(), SessionInvalidationReason.Revoked);

        Assert.Empty(notifier.NotifiedTargets);
    }

    /// <summary>Verifies that unconditional invalidation of an empty registry completes cleanly without attempting any notification.</summary>
    [Fact]
    public async Task InvalidateAllAsync_NoActiveSessions_CompletesWithoutNotifying()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);

        await invalidator.InvalidateAllAsync(SessionInvalidationReason.FactoryReset);

        Assert.Empty(notifier.NotifiedTargets);
    }

    /// <summary>Verifies the same unauthorized-before-notification ordering for unconditional invalidation as <see cref="InvalidateClientAsync_SessionIsUnauthorizedBeforeNotificationAttempted"/> proves for client-scoped.</summary>
    [Fact]
    public async Task InvalidateAllAsync_SessionIsUnauthorizedBeforeNotificationAttempted()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ConnectionId connectionId = ConnectionId.NewId();
        sessionRegistry.TryCreate(ClientId.NewId(), connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);
        notifier.OnNotify = target => Assert.False(sessionRegistry.IsActive(target.SessionId, target.ConnectionId));

        await invalidator.InvalidateAllAsync(SessionInvalidationReason.FactoryReset);

        Assert.Single(notifier.NotifiedTargets);
    }

    /// <summary>
    /// Verifies that an <see cref="OperationCanceledException"/> from the notifier is swallowed as an
    /// ordinary best-effort failure when the caller's own token was never cancelled.
    /// </summary>
    [Fact]
    public async Task NotifierOperationCanceledException_WithoutCancellation_IsSwallowedAsBestEffort()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier { ThrowOnNotify = new OperationCanceledException("adapter-side timeout") };
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        sessionRegistry.TryCreate(clientId, ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _);

        await invalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.Revoked);

        Assert.Single(notifier.NotifiedTargets);
    }

    /// <summary>
    /// Verifies that an <see cref="OperationCanceledException"/> from the notifier is swallowed as an
    /// ordinary best-effort failure even when it corresponds to the caller's own token actually being
    /// cancelled: every already-unauthorized target must still receive its best-effort teardown
    /// attempt regardless of why one target's own attempt was cancelled or failed, per
    /// <see cref="NotifyAllAsync_FirstTargetCancelled_StillAttemptsRemainingTargets"/>.
    /// </summary>
    [Fact]
    public async Task NotifierOperationCanceledException_WithCancelledToken_IsSwallowedAsBestEffort()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier { ThrowOnNotify = new OperationCanceledException("cancelled") };
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        sessionRegistry.TryCreate(clientId, ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await invalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.Revoked, cancellation.Token);

        Assert.Single(notifier.NotifiedTargets);
    }

    /// <summary>
    /// Verifies that the first target's notification being cancelled never prevents the remaining
    /// already-unauthorized targets from receiving their own best-effort teardown attempt: the
    /// authoritative trust mutation already committed for all of them, so one target's cancellation is
    /// just another best-effort failure, not a reason to abandon the rest. Every target here throws
    /// the same cancellation so the assertion holds regardless of which target the registry happens to
    /// enumerate first.
    /// </summary>
    [Fact]
    public async Task NotifyAllAsync_FirstTargetCancelled_StillAttemptsRemainingTargets()
    {
        var sessionRegistry = new SessionRegistry(2);
        sessionRegistry.TryCreate(ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _);
        sessionRegistry.TryCreate(ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _);
        var notifier = new FakeSessionTerminationNotifier { ThrowOnNotify = new OperationCanceledException("cancelled") };
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);

        await invalidator.InvalidateAllAsync(SessionInvalidationReason.FactoryReset);

        Assert.Equal(2, notifier.NotifiedTargets.Count);
    }
}
