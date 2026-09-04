using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Sessions;

/// <summary>Tests for <see cref="ClientSessionInvalidator"/>.</summary>
public class ClientSessionInvalidatorTests
{
    /// <summary>Verifies that client-scoped invalidation notifies every affected session with the exact reason given.</summary>
    [Fact]
    public async Task InvalidateClient_ThenNotifyAndCloseAllAsync_NotifiesEveryAffectedSessionWithExactReason()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        sessionRegistry.TryCreate(clientId, ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClient(clientId, SessionInvalidationReason.Revoked);
        await invalidator.NotifyAndCloseAllAsync(targets);

        SessionInvalidationTarget target = Assert.Single(notifier.NotifiedTargets);
        Assert.Equal(sessionId, target.SessionId);
        Assert.Equal(clientId, target.ClientId);
        Assert.Equal(SessionInvalidationReason.Revoked, target.Reason);
        Assert.Equal(SessionAuthenticationSource.TrustedDeviceCredential, target.AuthenticationSource);
    }

    /// <summary>Verifies that client-scoped invalidation through the seam still exempts a developer-token session.</summary>
    [Fact]
    public async Task InvalidateClient_ExcludesOneTimeLocalTokenSession()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        ConnectionId developerConnection = ConnectionId.NewId();
        sessionRegistry.TryCreate(clientId, developerConnection, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId developerSession);

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClient(clientId, SessionInvalidationReason.Blocked);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.Empty(notifier.NotifiedTargets);
        Assert.True(sessionRegistry.IsActive(developerSession, developerConnection));
    }

    /// <summary>
    /// Verifies that batch invalidation notifies every affected session across every listed client
    /// with the exact reason given, and still exempts a developer-token session -- the multi-client
    /// counterpart to <see cref="InvalidateClient_ThenNotifyAndCloseAllAsync_NotifiesEveryAffectedSessionWithExactReason"/>
    /// and <see cref="InvalidateClient_ExcludesOneTimeLocalTokenSession"/>.
    /// </summary>
    [Fact]
    public async Task InvalidateClients_ThenNotifyAndCloseAllAsync_NotifiesEveryAffectedSessionAcrossEveryClientAndExcludesDeveloperToken()
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

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClients([first, second, developerClient], SessionInvalidationReason.TrustReset);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.Equal(2, notifier.NotifiedTargets.Count);
        Assert.All(notifier.NotifiedTargets, target => Assert.Equal(SessionInvalidationReason.TrustReset, target.Reason));
        Assert.Equal(
            new[] { firstSession, secondSession }.OrderBy(id => id.Value),
            notifier.NotifiedTargets.Select(target => target.SessionId).OrderBy(id => id.Value));
        Assert.True(sessionRegistry.IsActive(developerSession, developerConnection));
    }

    /// <summary>Verifies that unconditional invalidation through the seam still reaches a developer-token session.</summary>
    [Fact]
    public async Task InvalidateAll_ThenNotifyAndCloseAllAsync_IncludesOneTimeLocalTokenSession()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ConnectionId connectionId = ConnectionId.NewId();
        sessionRegistry.TryCreate(ClientId.NewId(), connectionId, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId sessionId);

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateAll(SessionInvalidationReason.FactoryReset);
        await invalidator.NotifyAndCloseAllAsync(targets);

        SessionInvalidationTarget target = Assert.Single(notifier.NotifiedTargets);
        Assert.Equal(sessionId, target.SessionId);
        Assert.Equal(SessionInvalidationReason.FactoryReset, target.Reason);
        Assert.Equal(SessionAuthenticationSource.OneTimeLocalToken, target.AuthenticationSource);
        Assert.False(sessionRegistry.IsActive(sessionId, connectionId));
    }

    /// <summary>
    /// Verifies the security-mandated ordering: a session is already unauthorized in the registry --
    /// removed, per <see cref="ISessionRegistry.IsActive"/> -- before <see cref="InvalidateClient"/>
    /// even returns, well before <see cref="ClientSessionInvalidator.NotifyAndCloseAllAsync"/> attempts
    /// any notification.
    /// </summary>
    [Fact]
    public void InvalidateClient_SessionIsUnauthorizedAsSoonAsItReturns()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        sessionRegistry.TryCreate(clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClient(clientId, SessionInvalidationReason.Revoked);

        Assert.Single(targets);
        Assert.False(sessionRegistry.IsActive(sessionId, connectionId));
    }

    /// <summary>
    /// Verifies the same unauthorized-before-notification ordering as
    /// <see cref="InvalidateClient_SessionIsUnauthorizedAsSoonAsItReturns"/>, but observed from inside
    /// the notifier itself once <see cref="ClientSessionInvalidator.NotifyAndCloseAllAsync"/> actually
    /// runs against the already-invalidated target.
    /// </summary>
    [Fact]
    public async Task NotifyAndCloseAllAsync_SessionIsUnauthorizedBeforeNotificationAttempted()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        sessionRegistry.TryCreate(clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);
        notifier.OnNotify = target => Assert.False(sessionRegistry.IsActive(target.SessionId, target.ConnectionId));

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClient(clientId, SessionInvalidationReason.Revoked);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.Single(notifier.NotifiedTargets);
    }

    /// <summary>
    /// Verifies that a notification failure for one target never propagates to the caller and never
    /// prevents the remaining targets from being attempted -- the trust mutation that already
    /// committed must not observe this best-effort step's failure.
    /// </summary>
    [Fact]
    public async Task NotifyAndCloseAllAsync_NotifierFailure_DoesNotPropagateAndStillNotifiesOtherTargets()
    {
        var sessionRegistry = new SessionRegistry(2);
        var notifier = new FakeSessionTerminationNotifier { ThrowOnNotify = new InvalidOperationException("transport unavailable") };
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        Assert.True(sessionRegistry.TryCreate(ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _));
        Assert.True(sessionRegistry.TryCreate(ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _));

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateAll(SessionInvalidationReason.FactoryReset);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.Equal(2, notifier.NotifiedTargets.Count);
    }

    /// <summary>
    /// Verifies that a notification failure never resurrects or otherwise preserves the session: it
    /// remains gone from the registry regardless of the best-effort step's own outcome.
    /// </summary>
    [Fact]
    public async Task NotifyAndCloseAllAsync_NotifierFailure_DoesNotResurrectSession()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier { ThrowOnNotify = new InvalidOperationException("transport unavailable") };
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);
        ClientId clientId = ClientId.NewId();
        ConnectionId connectionId = ConnectionId.NewId();
        sessionRegistry.TryCreate(clientId, connectionId, SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out SessionId sessionId);

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClient(clientId, SessionInvalidationReason.Blocked);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.False(sessionRegistry.IsActive(sessionId, connectionId));
    }

    /// <summary>Verifies that invalidating a client with no active sessions completes cleanly without attempting any notification.</summary>
    [Fact]
    public async Task InvalidateClient_NoActiveSessions_CompletesWithoutNotifying()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClient(ClientId.NewId(), SessionInvalidationReason.Revoked);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.Empty(notifier.NotifiedTargets);
    }

    /// <summary>Verifies that unconditional invalidation of an empty registry completes cleanly without attempting any notification.</summary>
    [Fact]
    public async Task InvalidateAll_NoActiveSessions_CompletesWithoutNotifying()
    {
        var sessionRegistry = new SessionRegistry();
        var notifier = new FakeSessionTerminationNotifier();
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateAll(SessionInvalidationReason.FactoryReset);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.Empty(notifier.NotifiedTargets);
    }

    /// <summary>
    /// Verifies an <see cref="OperationCanceledException"/> from the notifier is swallowed as an
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

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClient(clientId, SessionInvalidationReason.Revoked);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.Single(notifier.NotifiedTargets);
    }

    /// <summary>
    /// Verifies an <see cref="OperationCanceledException"/> from the notifier is swallowed as an
    /// ordinary best-effort failure even when it corresponds to the caller's own token actually being
    /// cancelled: every already-unauthorized target must still receive its best-effort teardown
    /// attempt regardless of why one target's own attempt was cancelled or failed, per
    /// <see cref="NotifyAndCloseAllAsync_FirstTargetCancelled_StillAttemptsRemainingTargets"/>.
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

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateClient(clientId, SessionInvalidationReason.Revoked);
        await invalidator.NotifyAndCloseAllAsync(targets, cancellation.Token);

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
    public async Task NotifyAndCloseAllAsync_FirstTargetCancelled_StillAttemptsRemainingTargets()
    {
        var sessionRegistry = new SessionRegistry(2);
        sessionRegistry.TryCreate(ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _);
        sessionRegistry.TryCreate(ClientId.NewId(), ConnectionId.NewId(), SessionAuthenticationSource.TrustedDeviceCredential, SessionTrustTier.Full, out _);
        var notifier = new FakeSessionTerminationNotifier { ThrowOnNotify = new OperationCanceledException("cancelled") };
        var invalidator = new ClientSessionInvalidator(sessionRegistry, notifier);

        IReadOnlyList<SessionInvalidationTarget> targets = invalidator.InvalidateAll(SessionInvalidationReason.FactoryReset);
        await invalidator.NotifyAndCloseAllAsync(targets);

        Assert.Equal(2, notifier.NotifiedTargets.Count);
    }
}
