using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.Security;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for complete known-device administration.</summary>
public class TrustAdminServiceTests
{
    /// <summary>Verifies that list scopes are filtered and ordered by durable device age.</summary>
    [Fact]
    public void List_ScopesReturnOrderedRecords()
    {
        var trustStore = new FakeTrustStore();
        DateTimeOffset older = DateTimeOffset.UtcNow.AddDays(-1);
        TrustRecord trusted = new(ClientId.NewId(), "00002", "Trusted", KnownDeviceState.Trusted, "hash", older);
        TrustRecord blocked = new(ClientId.NewId(), "00001", "Blocked", KnownDeviceState.Blocked, string.Empty, older.AddHours(1));
        trustStore.Seed(trusted);
        trustStore.Seed(blocked);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        Assert.Equal([trusted, blocked], admin.List());
        Assert.Equal([trusted], admin.List("trust"));
        Assert.Equal([blocked], admin.List("block"));
    }

    /// <summary>Verifies that duplicate names receive presentation-only age-ordered suffixes.</summary>
    [Fact]
    public void List_DuplicateDisplayNames_AreDisambiguatedWithoutChangingStore()
    {
        var trustStore = new FakeTrustStore();
        DateTimeOffset oldest = DateTimeOffset.UtcNow.AddDays(-2);
        TrustRecord first = new(ClientId.NewId(), "00001", "Same Name", KnownDeviceState.Trusted, "hash1", oldest);
        TrustRecord second = new(ClientId.NewId(), "00002", "Same Name", KnownDeviceState.Trusted, "hash2", oldest.AddDays(1));
        trustStore.Seed(first);
        trustStore.Seed(second);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        IReadOnlyList<TrustRecord> listed = admin.List();

        Assert.Equal("Same Name #1", listed[0].DisplayName);
        Assert.Equal("Same Name #2", listed[1].DisplayName);
        Assert.Equal("Same Name", trustStore.TryGet(first.ClientId)!.DisplayName);
        Assert.Equal("Same Name", trustStore.TryGet(second.ClientId)!.DisplayName);
    }

    /// <summary>Verifies that help exposes every canonical administration command.</summary>
    [Fact]
    public void Help_ContainsCompleteCommandSurface()
    {
        var admin = new TrustAdminService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        string help = admin.Help();

        Assert.Contains("unblock", help);
        Assert.Contains("forget", help);
        Assert.Contains("reset-trust", help);
        Assert.Contains("confirm-reset", help);
    }

    /// <summary>Verifies that renaming a trusted device preserves identity and allows clearing its name.</summary>
    [Fact]
    public async Task RenameAsync_TrustedDevice_UpdatesAndClearsName()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        TrustRecord original = new(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        trustStore.Seed(original);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());
        KnownDeviceIncarnationId incarnation = admin.TryCaptureTrustedIncarnation(clientId)!.Value;

        await admin.RenameAsync(clientId, "Bedroom PC", incarnation);
        await admin.RenameAsync(clientId, string.Empty, incarnation);

        Assert.Equal(string.Empty, trustStore.TryGet(clientId)!.DisplayName);
        Assert.Equal(original.ShortId, trustStore.TryGet(clientId)!.ShortId);
    }

    /// <summary>Verifies that invalid display names are rejected before persistence.</summary>
    [Fact]
    public async Task RenameAsync_InvalidDisplayName_Throws()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());
        KnownDeviceIncarnationId incarnation = admin.TryCaptureTrustedIncarnation(clientId)!.Value;

        await Assert.ThrowsAsync<ArgumentException>(() => admin.RenameAsync(clientId, "Bad\nName", incarnation));
    }

    /// <summary>
    /// Verifies that a non-trusted device cannot be renamed even when the caller supplies the exact
    /// incarnation its own current, non-Trusted record carries -- <see cref="TrustAdminService.RenameAsync"/>'s
    /// own eligibility check is enforced independently of the incarnation precondition, defense-in-depth
    /// against a caller that captured an incarnation before a concurrent Revoke/Block landed but is only
    /// now reaching the actual mutation.
    /// </summary>
    [Fact]
    public async Task RenameAsync_NonTrustedDevice_Throws()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        var record = new TrustRecord(clientId, "12345", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };
        trustStore.Seed(record);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.RenameAsync(clientId, "New Name", record.Incarnation));
    }

    /// <summary>Verifies that a currently Trusted device's incarnation is captured.</summary>
    [Fact]
    public void TryCaptureTrustedIncarnation_TrustedClient_ReturnsIncarnation()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        var record = new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };
        trustStore.Seed(record);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        Assert.Equal(record.Incarnation, admin.TryCaptureTrustedIncarnation(clientId));
    }

    /// <summary>
    /// Verifies that an unrecognized client -- one with no Known Device record at all -- captures no
    /// incarnation, the same result a known-but-non-Trusted client also reports (see
    /// <see cref="TryCaptureTrustedIncarnation_NonTrustedStates_ReturnsNull"/>): the caller cannot
    /// distinguish "never known" from "known but not currently Trusted" from this result alone, by
    /// design, since a rename is rejected identically either way.
    /// </summary>
    [Fact]
    public void TryCaptureTrustedIncarnation_UnknownClient_ReturnsNull()
    {
        var admin = new TrustAdminService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        Assert.Null(admin.TryCaptureTrustedIncarnation(ClientId.NewId()));
    }

    /// <summary>Verifies that every non-Trusted state -- not merely one of them -- captures no incarnation.</summary>
    [Theory]
    [InlineData(KnownDeviceState.Revoked)]
    [InlineData(KnownDeviceState.Blocked)]
    [InlineData(KnownDeviceState.Unpaired)]
    public void TryCaptureTrustedIncarnation_NonTrustedStates_ReturnsNull(KnownDeviceState state)
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, state, string.Empty, DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() });
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        Assert.Null(admin.TryCaptureTrustedIncarnation(clientId));
    }

    /// <summary>Verifies that revocation destroys the verifier, cancels pairing, and invalidates sessions.</summary>
    [Fact]
    public async Task RevokeAsync_TrustedDevice_AppliesSecuritySideEffects()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        SessionId session = sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        await admin.RevokeAsync(clientId);

        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
        Assert.Empty(trustStore.TryGet(clientId)!.CredentialVerifier);
        Assert.False(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
        Assert.Equal([clientId], pairing.CancelledClientIds);
    }

    /// <summary>Verifies that targeted revocation leaves another client's active session untouched.</summary>
    [Fact]
    public async Task RevokeAsync_TargetedDevice_DoesNotInvalidateOtherClient()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        ClientId target = ClientId.NewId();
        ClientId other = ClientId.NewId();
        trustStore.Seed(new TrustRecord(target, "12345", null, KnownDeviceState.Trusted, "hash1", DateTimeOffset.UtcNow));
        trustStore.Seed(new TrustRecord(other, "12346", null, KnownDeviceState.Trusted, "hash2", DateTimeOffset.UtcNow));
        SessionId targetSession = sessions.Create(target);
        SessionId otherSession = sessions.Create(other);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), new FakePairingCoordinator());

        await admin.RevokeAsync(target);

        Assert.False(sessions.IsActive(targetSession, sessions.ConnectionIdFor(targetSession)));
        Assert.True(sessions.IsActive(otherSession, sessions.ConnectionIdFor(otherSession)));
    }

    /// <summary>
    /// Verifies that an unpaired known device is never eligible for Block, per the canonical Stage 3.2
    /// contract in <c>ai/context/protocol/security.md</c>: the device stays unpaired, and none of
    /// Block's side effects (pairing cancellation, session invalidation) apply to a mutation that
    /// never happened.
    /// </summary>
    [Fact]
    public async Task BlockAsync_UnpairedDevice_ThrowsAndDoesNotApplySideEffects()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Unpaired, string.Empty, DateTimeOffset.UtcNow));
        SessionId session = sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.BlockAsync(clientId));

        TrustRecord unchanged = trustStore.TryGet(clientId)!;
        Assert.Equal(KnownDeviceState.Unpaired, unchanged.State);
        Assert.Null(unchanged.BlockedAtUtc);
        Assert.Empty(pairing.CancelledClientIds);
        Assert.True(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
    }

    /// <summary>Verifies that blocking a trusted device destroys its verifier and invalidates all security state.</summary>
    [Fact]
    public async Task BlockAsync_TrustedDevice_CancelsPairingAndInvalidatesSessions()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        SessionId session = sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        await admin.BlockAsync(clientId);

        Assert.Empty(trustStore.TryGet(clientId)!.CredentialVerifier);
        Assert.False(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
        Assert.Equal([clientId], pairing.CancelledClientIds);
    }

    /// <summary>Verifies that a revoked device can be blocked and loses any verifier it retained.</summary>
    [Fact]
    public async Task BlockAsync_RevokedDevice_BlocksIt()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        await admin.BlockAsync(clientId);

        Assert.Equal(KnownDeviceState.Blocked, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>Verifies that repeating Block is a truthful no-op.</summary>
    [Fact]
    public async Task BlockAsync_AlreadyBlocked_DoesNotInvalidatePairingAgain()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow));
        var pairing = new FakePairingCoordinator();
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), pairing);

        await admin.BlockAsync(clientId);

        Assert.Empty(pairing.CancelledClientIds);
    }

    /// <summary>Verifies that unblocking clears the block timestamp and requires fresh pairing.</summary>
    [Fact]
    public async Task UnblockAsync_BlockedDevice_ReturnsToUnpaired()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        await admin.UnblockAsync(clientId);

        TrustRecord unpaired = trustStore.TryGet(clientId)!;
        Assert.Equal(KnownDeviceState.Unpaired, unpaired.State);
        Assert.Null(unpaired.BlockedAtUtc);
        Assert.Equal("12345", unpaired.ShortId);
    }

    /// <summary>Verifies that unblocking an unknown or non-blocked device is reported without mutation.</summary>
    [Fact]
    public async Task UnblockAsync_InvalidTarget_ThrowsOrNoOpsTruthfully()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.UnblockAsync(ClientId.NewId()));
        await admin.UnblockAsync(clientId);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>Verifies that only revoked and unpaired devices can be forgotten.</summary>
    [Fact]
    public async Task ForgetAsync_EligibleDevice_DeletesRecord()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        await admin.ForgetAsync(clientId);

        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>Verifies that forgetting a trusted device fails without deleting it.</summary>
    [Fact]
    public async Task ForgetAsync_TrustedDevice_ThrowsAndPreservesRecord()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        TrustRecord record = new(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        trustStore.Seed(record);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.ForgetAsync(clientId));

        Assert.Equal(record, trustStore.TryGet(clientId));
    }

    /// <summary>Verifies that a blocked device cannot be forgotten before it is explicitly unblocked.</summary>
    [Fact]
    public async Task ForgetAsync_BlockedDevice_ThrowsAndPreservesRecord()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        TrustRecord record = new(clientId, "12345", null, KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        trustStore.Seed(record);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.ForgetAsync(clientId));

        Assert.Equal(record, trustStore.TryGet(clientId));
    }

    /// <summary>Verifies that forgetting an eligible device cancels any pairing state it owns.</summary>
    [Fact]
    public async Task ForgetAsync_EligibleDevice_CancelsPairing()
    {
        var trustStore = new FakeTrustStore();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Unpaired, string.Empty, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), pairing);

        await admin.ForgetAsync(clientId);

        Assert.Equal([clientId], pairing.CancelledClientIds);
    }

    /// <summary>Verifies that revocation persistence failure leaves trust, pairing, and sessions untouched.</summary>
    [Fact]
    public async Task RevokeAsync_PersistenceFails_DoesNotApplySideEffects()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        SessionId session = sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        await Assert.ThrowsAsync<IOException>(() => admin.RevokeAsync(clientId));

        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
        Assert.True(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
        Assert.Empty(pairing.CancelledClientIds);
    }

    /// <summary>Verifies that Reset Trust preserves revoked and blocked records while invalidating only trusted sessions.</summary>
    [Fact]
    public async Task ResetTrustAsync_PreservesNonTrustedRecords()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId trusted = ClientId.NewId();
        ClientId blocked = ClientId.NewId();
        trustStore.Seed(new TrustRecord(trusted, "12345", "Trusted", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        trustStore.Seed(new TrustRecord(blocked, "12346", "Blocked", KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        SessionId session = sessions.Create(trusted);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        await admin.ResetTrustAsync();

        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(trusted)!.State);
        Assert.Equal(blocked, trustStore.TryGet(blocked)!.ClientId);
        Assert.Equal(KnownDeviceState.Blocked, trustStore.TryGet(blocked)!.State);
        Assert.False(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
    }

    /// <summary>
    /// Verifies that Reset Trust's batch invalidation notifies every trusted device it revokes, not
    /// just the first -- each with its own <c>trust_reset</c> reason, not a single call that only
    /// reaches one of them.
    /// </summary>
    [Fact]
    public async Task ResetTrustAsync_MultipleTrustedDevices_NotifiesEachWithTrustReset()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry(2);
        var pairing = new FakePairingCoordinator();
        var notifier = new FakeSessionTerminationNotifier();
        ClientId first = ClientId.NewId();
        ClientId second = ClientId.NewId();
        trustStore.Seed(new TrustRecord(first, "12345", "First", KnownDeviceState.Trusted, "hash1", DateTimeOffset.UtcNow));
        trustStore.Seed(new TrustRecord(second, "12346", "Second", KnownDeviceState.Trusted, "hash2", DateTimeOffset.UtcNow));
        SessionId firstSession = sessions.Create(first);
        SessionId secondSession = sessions.Create(second);
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);

        IReadOnlyList<ClientId> affected = await admin.ResetTrustAsync();

        Assert.Equal(2, affected.Count);
        Assert.False(sessions.IsActive(firstSession, sessions.ConnectionIdFor(firstSession)));
        Assert.False(sessions.IsActive(secondSession, sessions.ConnectionIdFor(secondSession)));
        Assert.Equal(2, notifier.NotifiedTargets.Count);
        Assert.All(notifier.NotifiedTargets, target => Assert.Equal(SessionInvalidationReason.TrustReset, target.Reason));
        Assert.Equal(
            new[] { first, second }.OrderBy(id => id.Value),
            notifier.NotifiedTargets.Select(target => target.ClientId).OrderBy(id => id.Value));
    }

    /// <summary>
    /// Verifies the batch-invalidation guarantee the sequential-loop bug this fixes would have missed:
    /// with the first affected client's notification deliberately held open, the second affected
    /// client is already unauthorized in the registry -- proving both clients were removed from
    /// authorization in one atomic pass before either one's best-effort teardown began, rather than
    /// the second only becoming unauthorized once the loop reached it.
    /// </summary>
    [Fact]
    public async Task ResetTrustAsync_MultipleTrustedDevices_SecondClientAlreadyUnauthorizedWhileFirstNotificationIsInFlight()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry(2);
        var pairing = new FakePairingCoordinator();
        var enteredFirstNotify = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstNotify = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ClientId first = ClientId.NewId();
        ClientId second = ClientId.NewId();
        var notifier = new FakeSessionTerminationNotifier
        {
            BeforeNotify = target =>
            {
                if (!target.ClientId.Equals(first))
                {
                    return Task.CompletedTask;
                }

                enteredFirstNotify.SetResult();
                return releaseFirstNotify.Task;
            },
        };
        trustStore.Seed(new TrustRecord(first, "12345", "First", KnownDeviceState.Trusted, "hash1", DateTimeOffset.UtcNow));
        trustStore.Seed(new TrustRecord(second, "12346", "Second", KnownDeviceState.Trusted, "hash2", DateTimeOffset.UtcNow));
        SessionId firstSession = sessions.Create(first);
        SessionId secondSession = sessions.Create(second);
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);

        Task<IReadOnlyList<ClientId>> resetTrust = admin.ResetTrustAsync();
        await enteredFirstNotify.Task;

        Assert.False(sessions.IsActive(firstSession, sessions.ConnectionIdFor(firstSession)));
        Assert.False(sessions.IsActive(secondSession, sessions.ConnectionIdFor(secondSession)));

        releaseFirstNotify.SetResult();
        await resetTrust;
    }

    /// <summary>Verifies that Reset Trust cancels pairing even when no trusted device needs revocation.</summary>
    [Fact]
    public async Task ResetTrustAsync_NoTrustedDevices_StillCancelsPairing()
    {
        var pairing = new FakePairingCoordinator();
        var admin = new TrustAdminService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), pairing);

        IReadOnlyList<ClientId> affected = await admin.ResetTrustAsync();

        Assert.Empty(affected);
        Assert.Equal(1, pairing.CancelAllCallCount);
    }

    /// <summary>Verifies that Reset Trust persistence failure leaves sessions and pairing untouched.</summary>
    [Fact]
    public async Task ResetTrustAsync_PersistenceFails_DoesNotApplySideEffects()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Trusted", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        SessionId session = sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        await Assert.ThrowsAsync<IOException>(() => admin.ResetTrustAsync());

        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
        Assert.True(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
        Assert.Equal(0, pairing.CancelAllCallCount);
    }

    /// <summary>Verifies that Reset Trust revokes only trusted devices and cancels all pairing.</summary>
    [Fact]
    public async Task ResetTrustAsync_RevokesTrustedDevicesOnly()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId trustedClient = ClientId.NewId();
        ClientId revokedClient = ClientId.NewId();
        trustStore.Seed(new TrustRecord(trustedClient, "12345", "Trusted", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        trustStore.Seed(new TrustRecord(revokedClient, "12346", "Revoked", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow));
        SessionId session = sessions.Create(trustedClient);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        IReadOnlyList<ClientId> affected = await admin.ResetTrustAsync();

        Assert.Equal([trustedClient], affected);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(trustedClient)!.State);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(revokedClient)!.State);
        Assert.False(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
        Assert.Equal(1, pairing.CancelAllCallCount);
    }

    /// <summary>Verifies that short-ID mutations target the matching device.</summary>
    [Fact]
    public async Task RevokeByShortIdAsync_MatchingDevice_RevokesIt()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        TrustMutationOutcome outcome = await admin.RevokeByShortIdAsync("12345");

        Assert.Equal(TrustMutationOutcome.Changed, outcome);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Proves the incarnation ABA fix end-to-end through <see cref="TrustAdminService"/>'s own wiring,
    /// not only the underlying <see cref="ITrustStore"/> guard, deliberately reusing the exact same
    /// shortId <c>11111</c> for both incarnations: an administrative operation that resolved shortId
    /// <c>11111</c> to a device must not fall through to mutating a different, later incarnation of the
    /// exact same <see cref="ClientId"/> that appears in the gap between that resolution and the
    /// mutation actually applying -- for example the original Known Device forgotten by a Factory Reset
    /// and the same client re-paired, with the freed shortId reassigned to the replacement, before this
    /// call resumes. A replacement that received a different shortId would not prove this closed, since
    /// shortId itself would then also have blocked the stale mutation. <see cref="FakeTrustStore.AfterTryGetByShortId"/>
    /// deterministically injects that exact replacement into the gap <see cref="TrustAdminService"/>'s
    /// own private shortId-resolution helper has between resolving the shortId and invoking the
    /// mutation, without depending on real thread scheduling.
    /// </summary>
    [Fact]
    public async Task RevokeByShortIdAsync_ClientReplacedByNewIncarnationBetweenResolutionAndMutation_ReturnsNotFoundWithoutMutatingReplacement()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() });
        var replacement = new TrustRecord(clientId, "11111", "Living Room PC (re-paired)", KnownDeviceState.Trusted, "beefdead", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };
        trustStore.AfterTryGetByShortId = () => trustStore.Seed(replacement);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        TrustMutationOutcome outcome = await admin.RevokeByShortIdAsync("11111");

        Assert.Equal(TrustMutationOutcome.NotFound, outcome);
        Assert.Equal(replacement, trustStore.TryGet(clientId));
    }

    /// <summary>Verifies that an unknown short ID produces a not-found outcome without mutation.</summary>
    [Fact]
    public async Task BlockByShortIdAsync_UnknownId_ReturnsNotFound()
    {
        var admin = new TrustAdminService(new FakeTrustStore(), Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        Assert.Equal(TrustMutationOutcome.NotFound, await admin.BlockByShortIdAsync("99999"));
    }

    /// <summary>Verifies that short-ID blocking applies the same targeted security side effects as client-ID blocking.</summary>
    [Fact]
    public async Task BlockByShortIdAsync_MatchingDevice_BlocksAndInvalidatesSession()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        SessionId session = sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        Assert.Equal(TrustMutationOutcome.Changed, await admin.BlockByShortIdAsync("12345"));

        Assert.Equal(KnownDeviceState.Blocked, trustStore.TryGet(clientId)!.State);
        Assert.False(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
        Assert.Equal([clientId], pairing.CancelledClientIds);
    }

    /// <summary>Verifies that short-ID wrappers support the complete unblock and forget flow.</summary>
    [Fact]
    public async Task UnblockAndForgetByShortIdAsync_EligibleDevice_CompletesFlow()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        Assert.Equal(TrustMutationOutcome.Changed, await admin.UnblockByShortIdAsync("12345"));
        Assert.Equal(TrustMutationOutcome.Changed, await admin.ForgetByShortIdAsync("12345"));
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the same end-to-end same-shortId incarnation ABA guarantee as
    /// <see cref="RevokeByShortIdAsync_ClientReplacedByNewIncarnationBetweenResolutionAndMutation_ReturnsNotFoundWithoutMutatingReplacement"/>
    /// for <see cref="TrustAdminService.BlockByShortIdAsync"/>, proving the shared shortId-resolution
    /// helper closes the race for this mutation too rather than it being specific to Revoke.
    /// </summary>
    [Fact]
    public async Task BlockByShortIdAsync_ClientReplacedByNewIncarnationBetweenResolutionAndMutation_ReturnsNotFoundWithoutMutatingReplacement()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() });
        var replacement = new TrustRecord(clientId, "11111", "Living Room PC (re-paired)", KnownDeviceState.Trusted, "beefdead", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };
        trustStore.AfterTryGetByShortId = () => trustStore.Seed(replacement);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        TrustMutationOutcome outcome = await admin.BlockByShortIdAsync("11111");

        Assert.Equal(TrustMutationOutcome.NotFound, outcome);
        Assert.Equal(replacement, trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the same end-to-end same-shortId incarnation ABA guarantee as
    /// <see cref="RevokeByShortIdAsync_ClientReplacedByNewIncarnationBetweenResolutionAndMutation_ReturnsNotFoundWithoutMutatingReplacement"/>
    /// for <see cref="TrustAdminService.UnblockByShortIdAsync"/>, proving the shared shortId-resolution
    /// helper closes the race for this mutation too.
    /// </summary>
    [Fact]
    public async Task UnblockByShortIdAsync_ClientReplacedByNewIncarnationBetweenResolutionAndMutation_ReturnsNotFoundWithoutMutatingReplacement()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() });
        var replacement = new TrustRecord(clientId, "11111", "Living Room PC (re-paired)", KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };
        trustStore.AfterTryGetByShortId = () => trustStore.Seed(replacement);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        TrustMutationOutcome outcome = await admin.UnblockByShortIdAsync("11111");

        Assert.Equal(TrustMutationOutcome.NotFound, outcome);
        Assert.Equal(replacement, trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the same end-to-end same-shortId incarnation ABA guarantee as
    /// <see cref="RevokeByShortIdAsync_ClientReplacedByNewIncarnationBetweenResolutionAndMutation_ReturnsNotFoundWithoutMutatingReplacement"/>
    /// for <see cref="TrustAdminService.ForgetByShortIdAsync"/>, proving the shared shortId-resolution
    /// helper closes the race for this mutation too.
    /// </summary>
    [Fact]
    public async Task ForgetByShortIdAsync_ClientReplacedByNewIncarnationBetweenResolutionAndMutation_ReturnsNotFoundWithoutMutatingReplacement()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Unpaired, string.Empty, DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() });
        var replacement = new TrustRecord(clientId, "11111", "Living Room PC (re-paired)", KnownDeviceState.Unpaired, string.Empty, DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };
        trustStore.AfterTryGetByShortId = () => trustStore.Seed(replacement);
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), new FakePairingCoordinator());

        TrustMutationOutcome outcome = await admin.ForgetByShortIdAsync("11111");

        Assert.Equal(TrustMutationOutcome.NotFound, outcome);
        Assert.Equal(replacement, trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the exact security-mandated ordering for Revoke: the authoritative trust-store
    /// mutation happens before the session becomes unauthorized in the registry, before pairing is
    /// cancelled, before its best-effort terminal notification carries the exact <c>revoked</c>
    /// reason -- per <c>ai/context/protocol/security.md</c>'s "authoritative state change, credential
    /// invalidation where applicable, future authentication/pairing enforcement, ... then forced
    /// close" ordering. Session invalidation is placed immediately after the trust mutation, ahead of
    /// pairing cancellation, to minimize the post-mutation window in which a concurrent request on
    /// that session could still find <see cref="ISessionRegistry.IsActive"/> true.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_AppliesSideEffectsInTheMandatedOrder()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var sessions = new FakeSessionRegistry { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        var notifier = new FakeSessionTerminationNotifier { OnNotify = target => order.Add($"Notified:{target.Reason}") };
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);

        await admin.RevokeAsync(clientId);

        Assert.Equal(["Revoke", "InvalidateAllForClient", "Cancel", "Notified:Revoked"], order);
    }

    /// <summary>Verifies the same mandated ordering for Block.</summary>
    [Fact]
    public async Task BlockAsync_AppliesSideEffectsInTheMandatedOrder()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var sessions = new FakeSessionRegistry { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        var notifier = new FakeSessionTerminationNotifier { OnNotify = target => order.Add($"Notified:{target.Reason}") };
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);

        await admin.BlockAsync(clientId);

        Assert.Equal(["Block", "InvalidateAllForClient", "Cancel", "Notified:Blocked"], order);
    }

    /// <summary>Verifies the same mandated ordering for Reset Trust, including the batch invalidation of every affected device.</summary>
    [Fact]
    public async Task ResetTrustAsync_AppliesSideEffectsInTheMandatedOrder()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var sessions = new FakeSessionRegistry { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        var notifier = new FakeSessionTerminationNotifier { OnNotify = target => order.Add($"Notified:{target.Reason}") };
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);

        await admin.ResetTrustAsync();

        Assert.Equal(["ResetTrust", "InvalidateAllForClients", "CancelAll", "Notified:TrustReset"], order);
    }

    /// <summary>
    /// Proves the client-scoped half of the lifecycle-linearization fix directly, from inside the
    /// notifier itself rather than only from the ordering label list above: by the instant the first
    /// (and only) target's best-effort notification is attempted, pairing has already been cancelled
    /// for that exact client -- closing the window where a stale pairing challenge could otherwise
    /// survive for the duration of an in-flight notification/close.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_PairingIsCancelledBeforeNotificationAttempted()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        var notifier = new FakeSessionTerminationNotifier
        {
            BeforeNotify = target =>
            {
                Assert.Equal([clientId], pairing.CancelledClientIds);
                return Task.CompletedTask;
            },
        };
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);

        await admin.RevokeAsync(clientId);

        Assert.Single(notifier.NotifiedTargets);
    }

    /// <summary>Verifies the same direct notifier-observed guarantee as <see cref="RevokeAsync_PairingIsCancelledBeforeNotificationAttempted"/> for Block.</summary>
    [Fact]
    public async Task BlockAsync_PairingIsCancelledBeforeNotificationAttempted()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId clientId = ClientId.NewId();
        var notifier = new FakeSessionTerminationNotifier
        {
            BeforeNotify = target =>
            {
                Assert.Equal([clientId], pairing.CancelledClientIds);
                return Task.CompletedTask;
            },
        };
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);

        await admin.BlockAsync(clientId);

        Assert.Single(notifier.NotifiedTargets);
    }

    /// <summary>
    /// Proves the batch half of the same lifecycle-linearization fix as
    /// <see cref="RevokeAsync_PairingIsCancelledBeforeNotificationAttempted"/>: by the instant any
    /// affected session's best-effort notification is attempted during a Reset Trust, pairing has
    /// already been cancelled for every affected client, not merely the one about to be notified.
    /// </summary>
    [Fact]
    public async Task ResetTrustAsync_PairingIsCancelledForEveryAffectedClientBeforeAnyNotificationAttempted()
    {
        var trustStore = new FakeTrustStore();
        var sessions = new FakeSessionRegistry();
        var pairing = new FakePairingCoordinator();
        ClientId first = ClientId.NewId();
        ClientId second = ClientId.NewId();
        var notifier = new FakeSessionTerminationNotifier
        {
            BeforeNotify = target =>
            {
                Assert.Equal(1, pairing.CancelAllCallCount);
                return Task.CompletedTask;
            },
        };
        trustStore.Seed(new TrustRecord(first, "11111", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        trustStore.Seed(new TrustRecord(second, "22222", "Bedroom Tablet", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(first);
        sessions.Create(second);
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);

        await admin.ResetTrustAsync();

        Assert.Equal(2, notifier.NotifiedTargets.Count);
    }

    /// <summary>
    /// Verifies that Reset Trust with no trusted devices still cancels pairing (proven by the existing
    /// <see cref="ResetTrustAsync_NoTrustedDevices_StillCancelsPairing"/> test) and still advances the
    /// trust store's own security fence -- reported here as a "ResetTrust" mutation event even though
    /// no record changed -- so an in-flight pending pairing credential cannot slip past a concurrent
    /// Reset Trust merely because nothing was left to revoke.
    /// </summary>
    [Fact]
    public async Task ResetTrustAsync_NoTrustedDevices_StillAdvancesSecurityFence()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        var admin = new TrustAdminService(trustStore, Invalidator(new FakeSessionRegistry()), pairing);

        await admin.ResetTrustAsync();

        Assert.Equal(["ResetTrust", "CancelAll"], order);
    }

    /// <summary>
    /// Verifies the mandated ordering for Forget: the authoritative trust-store mutation happens
    /// before pairing is cancelled. Forget never invalidates a session -- unlike Revoke, Block, and
    /// Reset Trust, it carries no session-invalidation side effect to order against.
    /// </summary>
    [Fact]
    public async Task ForgetAsync_AppliesSideEffectsInTheMandatedOrder()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var sessions = new FakeSessionRegistry { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Unpaired, string.Empty, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, Invalidator(sessions), pairing);

        await admin.ForgetAsync(clientId);

        Assert.Equal(["Forget", "Cancel"], order);
    }

    /// <summary>
    /// Wraps <paramref name="sessions"/> in a real <see cref="ClientSessionInvalidator"/> with a
    /// discardable notifier, for tests that only need actual session removal proven -- not the
    /// notifier itself, which the ordering tests wire up explicitly where it matters.
    /// </summary>
    private static IClientSessionInvalidator Invalidator(FakeSessionRegistry sessions) =>
        new ClientSessionInvalidator(sessions, new FakeSessionTerminationNotifier());

    // ---- Security-state gate linearization: admin mutation publication vs. session authorization ----
    //
    // The tests below use real TrustStore, SessionRegistry, and PairingCoordinator instances sharing
    // one SecurityStateGate -- not FakeTrustStore/FakeSessionRegistry -- because the property under
    // test (a trust mutation's publication and the sessions it affects becoming unauthorized are one
    // indivisible event) only exists once those two real collaborators share a real lock; a fake has
    // no such lock to prove ordering against.

    /// <summary>
    /// Composes a real <see cref="TrustStore"/>, <see cref="SessionRegistry"/>, and
    /// <see cref="PairingCoordinator"/> sharing one <see cref="SecurityStateGate"/>, plus a
    /// <see cref="TrustAdminService"/> wired over them, for tests proving the linearization between an
    /// administrative mutation's publication and session authorization.
    /// </summary>
    /// <param name="persistence">The persistence adapter the store writes through to.</param>
    private static async Task<(TrustStore TrustStore, SessionRegistry Sessions, PairingCoordinator Pairing, TrustAdminService Admin, FakeSessionTerminationNotifier Notifier)>
        ComposeRealCollaboratorsAsync(FakeTrustStorePersistence persistence)
    {
        var securityStateGate = new SecurityStateGate();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock(), securityStateGate);
        var sessions = new SessionRegistry(securityStateGate);
        var pairing = new PairingCoordinator(trustStore, new FakeClock());
        var notifier = new FakeSessionTerminationNotifier();
        var admin = new TrustAdminService(trustStore, new ClientSessionInvalidator(sessions, notifier), pairing);
        return (trustStore, sessions, pairing, admin, notifier);
    }

    /// <summary>
    /// Proves race A (Revoke wins first): an old Restricted session attempting <c>pairing_request</c>
    /// from the exact instant its own deauthorization commits -- nested inside
    /// <see cref="ITrustStore.RevokeAsync"/>'s own <c>onPublished</c> callback, running on the same
    /// thread still holding the shared <see cref="SecurityStateGate"/> that
    /// <see cref="ISessionRegistry.TryExecuteIfActive{T}"/> also requires -- can never create a new
    /// challenge under the post-Revoke generation: deterministic because a genuinely later, real
    /// cross-thread attempt could only observe the session gone even later than this exact instant,
    /// never earlier. This mirrors exactly what <see cref="TrustAdminService"/>'s own
    /// <c>onPublished</c> wiring does (<see cref="IClientSessionInvalidator.InvalidateClient"/>),
    /// proven here directly against <see cref="ITrustStore"/> so the test controls the nested attempt.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_OldSessionAttemptsPairingAtTheInstantDeauthorizationCommits_CannotCreateNewChallenge()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, SessionRegistry sessions, PairingCoordinator pairing, _, _) = await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(sessions.TryCreate(clientId, connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));

        bool staleAttemptRan = false;
        TrustMutationOutcome outcome = await trustStore.RevokeAsync(clientId, onPublished: () =>
        {
            sessions.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked);
            staleAttemptRan = sessions.TryExecuteIfActive(sessionId, connectionId, () => pairing.BeginPairing(clientId), out PairingStartResult _);
        });

        Assert.Equal(TrustMutationOutcome.Changed, outcome);
        Assert.False(staleAttemptRan);
        Assert.Equal(PairingStatusKind.Idle, pairing.GetStatusSnapshot(clientId).Kind);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Proves the same race A end to end through <see cref="TrustAdminService.RevokeAsync"/> itself:
    /// once it returns, the old session it just invalidated can never resume pairing, and the store
    /// stays Revoked -- Revoke cannot be undone by a stale session.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_ThroughTrustAdminService_OldSessionCannotResumePairingAfterwardsAndRevokedStaysAuthoritative()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, SessionRegistry sessions, PairingCoordinator pairing, TrustAdminService admin, FakeSessionTerminationNotifier notifier) =
            await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(sessions.TryCreate(clientId, connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));

        await admin.RevokeAsync(clientId);

        bool started = sessions.TryExecuteIfActive(sessionId, connectionId, () => pairing.BeginPairing(clientId), out PairingStartResult _);
        Assert.False(started);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
        // The best-effort notification step still runs, after the atomic publish-and-deauthorize, with
        // the exact session it just invalidated -- the onPublished rewiring only changed when
        // deauthorization happens, not the notification step that follows it.
        SessionInvalidationTarget notified = Assert.Single(notifier.NotifiedTargets);
        Assert.Equal(sessionId, notified.SessionId);
        Assert.Equal(SessionInvalidationReason.Revoked, notified.Reason);
    }

    /// <summary>
    /// Proves race C (request legitimately wins first): a challenge started before Revoke is created
    /// under the pre-Revoke generation, and Revoke's own <see cref="IPairingCoordinator.Cancel"/> call
    /// -- which runs after the atomic publish-and-deauthorize step, exactly as the mandated ordering
    /// requires -- tears it down rather than leaving it reachable. Nothing stale survives either way:
    /// this is the legal opposite ordering to race A, not a second bug.
    /// </summary>
    [Fact]
    public async Task PairingRequest_LegitimatelyBeforeRevoke_CreatesChallengeThatRevokeThenCancels()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, SessionRegistry sessions, PairingCoordinator pairing, TrustAdminService admin, _) = await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(sessions.TryCreate(clientId, connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));

        bool started = sessions.TryExecuteIfActive(sessionId, connectionId, () => pairing.BeginPairing(clientId), out PairingStartResult beginResult);
        Assert.True(started);
        Assert.Equal(PairingStartOutcome.Started, beginResult.Outcome);

        await admin.RevokeAsync(clientId);

        Assert.Equal(PairingStatusKind.Idle, pairing.GetStatusSnapshot(clientId).Kind);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Proves race D (Reset Trust equivalent of race A): the same nested-callback proof against
    /// <see cref="ITrustStore.ResetTrustAsync"/>, mirroring <see cref="TrustAdminService.ResetTrustAsync"/>'s
    /// own <c>onPublished</c> wiring (<see cref="IClientSessionInvalidator.InvalidateClients"/>).
    /// </summary>
    [Fact]
    public async Task ResetTrustAsync_OldSessionAttemptsPairingAtTheInstantDeauthorizationCommits_CannotCreateNewChallenge()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, SessionRegistry sessions, PairingCoordinator pairing, _, _) = await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(sessions.TryCreate(clientId, connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));

        bool staleAttemptRan = false;
        IReadOnlyList<ClientId> affected = await trustStore.ResetTrustAsync(onPublished: affectedClients =>
        {
            sessions.InvalidateAllForClients(affectedClients, SessionInvalidationReason.TrustReset);
            staleAttemptRan = sessions.TryExecuteIfActive(sessionId, connectionId, () => pairing.BeginPairing(clientId), out PairingStartResult _);
        });

        Assert.Equal([clientId], affected);
        Assert.False(staleAttemptRan);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Proves race E (Block equivalent of race A), and that Blocked stays authoritative: the old
    /// session cannot create a new challenge, and the record remains Blocked rather than being
    /// silently overwritten by a stale pairing completion.
    /// </summary>
    [Fact]
    public async Task BlockAsync_OldSessionAttemptsPairingAtTheInstantDeauthorizationCommits_CannotCreateNewChallengeAndBlockedStaysAuthoritative()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, SessionRegistry sessions, PairingCoordinator pairing, _, _) = await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(sessions.TryCreate(clientId, connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Restricted, out SessionId sessionId));

        bool staleAttemptRan = false;
        TrustMutationOutcome outcome = await trustStore.BlockAsync(clientId, onPublished: () =>
        {
            sessions.InvalidateAllForClient(clientId, SessionInvalidationReason.Blocked);
            staleAttemptRan = sessions.TryExecuteIfActive(sessionId, connectionId, () => pairing.BeginPairing(clientId), out PairingStartResult _);
        });

        Assert.Equal(TrustMutationOutcome.Changed, outcome);
        Assert.False(staleAttemptRan);
        Assert.Equal(KnownDeviceState.Blocked, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Proves race F (developer-token semantics survive the new atomic path): a developer-token
    /// session for the same self-declared <see cref="ClientId"/> a client-scoped Revoke targets is
    /// never deauthorized by it, exactly as before this fix -- <c>onPublished</c> only ever calls
    /// <see cref="IClientSessionInvalidator.InvalidateClient"/>, which already excludes
    /// <see cref="SessionAuthenticationSource.OneTimeLocalToken"/> sessions.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_DeveloperTokenSessionSharingTheClientId_IsNeverInvalidated()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, SessionRegistry sessions, _, TrustAdminService admin, _) = await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(sessions.TryCreate(clientId, connectionId, SessionAuthenticationSource.OneTimeLocalToken, SessionTrustTier.Full, out SessionId sessionId));

        await admin.RevokeAsync(clientId);

        Assert.True(sessions.IsActive(sessionId, connectionId));
    }

    /// <summary>
    /// Proves race G (rename replacement-incarnation race) at the <see cref="TrustAdminService"/>/
    /// <see cref="TrustStore"/> integration level, using real collaborators rather than the
    /// <see cref="TrustStoreTests"/> unit-level proof alone: a caller (the dispatcher's own session
    /// -authorization boundary, in production) already captured the Known Device's incarnation before
    /// this rename call was even issued, then the rename call gets queued behind another mutation's own
    /// persistence write -- exactly as <see cref="TrustStore"/>'s <c>mutationSemaphore</c> serializes
    /// every mutation. By the time the queued rename's own check finally runs, the other mutation has
    /// already replaced the client's Known Device with a brand new incarnation (a forget-and-re-pair
    /// collapsed into one replacing <see cref="ITrustStore.UpsertAsync"/> for this test's purposes,
    /// deliberately reassigned the exact same shortId <c>11111</c> the original held) -- a structurally
    /// different Known Device sharing only the same durable <see cref="ClientId"/> and shortId. The
    /// stale captured incarnation no longer matches it, so the rename must be rejected as
    /// <see cref="TrustMutationOutcome.NotFound"/> rather than silently renaming the replacement. A
    /// replacement under a different shortId would not prove this closed, since shortId itself would
    /// then also have blocked the stale mutation.
    /// </summary>
    [Fact]
    public async Task RenameAsync_QueuedBehindConcurrentIncarnationSwap_DoesNotRenameTheReplacement()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, _, _, TrustAdminService admin, _) = await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() });

        // The caller already captured the still-current incarnation A, exactly as the dispatcher's own
        // session-authorization boundary would while it is still the authority.
        KnownDeviceIncarnationId capturedIncarnation = admin.TryCaptureTrustedIncarnation(clientId)!.Value;

        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };
        var newIncarnation = new TrustRecord(clientId, "11111", "Living Room PC (re-paired)", KnownDeviceState.Trusted, "beefdead", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };

        // The incarnation swap is already in flight and holds the store's mutation serialization,
        // blocked in its own gated persistence write below -- the original record (incarnation A,
        // shortId 11111) is still the only one visible to any reader at this point.
        Task swapTask = trustStore.UpsertAsync(newIncarnation);
        await enteredSave.Task;

        // The old Full session's rename starts now, carrying the incarnation it already captured, and
        // queues behind the swap's still-held mutation serialization.
        Task renameTask = admin.RenameAsync(clientId, "Renamed By Stale Session", capturedIncarnation);

        releaseSave.SetResult();
        await swapTask;

        await Assert.ThrowsAsync<KeyNotFoundException>(() => renameTask);
        Assert.Equal(newIncarnation, trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Proves the rename authorization-boundary invariant's "request wins" ordering, using real
    /// <see cref="SessionRegistry"/> and <see cref="TrustStore"/> collaborators: an old Full session
    /// captures its target incarnation through <see cref="ISessionRegistry.TryExecuteIfActive{T}"/> while
    /// it is still active -- the same linearization point <see cref="DovahLink.Host.Client.Dispatch.ClientMessageDispatcher"/>'s
    /// own <c>rename_request</c> handling uses -- before a later administrative mutation replaces the Known
    /// Device with a brand new incarnation (a forget-and-re-pair collapsed into one replacing
    /// <see cref="ITrustStore.UpsertAsync"/> for this test's purposes, deliberately reassigned the exact
    /// same shortId). The captured incarnation is honored as authoritative: the async rename that
    /// resumes afterward, carrying it, must reject the replacement rather than silently renaming it.
    /// </summary>
    [Fact]
    public async Task RenameAuthorizationBoundary_RequestCapturesBeforeReplacement_ReplacementCannotBeRenamed()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, SessionRegistry sessions, _, TrustAdminService admin, _) = await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() });
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(sessions.TryCreate(clientId, connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Full, out SessionId sessionId));

        bool captured = sessions.TryExecuteIfActive(
            sessionId, connectionId, () => admin.TryCaptureTrustedIncarnation(clientId), out KnownDeviceIncarnationId? capturedIncarnation);
        Assert.True(captured);
        Assert.NotNull(capturedIncarnation);

        var replacement = new TrustRecord(clientId, "11111", "Living Room PC (re-paired)", KnownDeviceState.Trusted, "beefdead", DateTimeOffset.UtcNow) { Incarnation = KnownDeviceIncarnationId.NewId() };
        await trustStore.UpsertAsync(replacement);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.RenameAsync(clientId, "Renamed By Stale Session", capturedIncarnation.Value));
        Assert.Equal(replacement, trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Proves the rename authorization-boundary invariant's "admin wins" ordering, the same deterministic
    /// nested-callback technique race A/D above use: the incarnation capture attempt runs from inside
    /// <see cref="ITrustStore.RevokeAsync"/>'s own <c>onPublished</c> callback, on the same thread still
    /// holding the shared <see cref="SecurityStateGate"/> that <see cref="ISessionRegistry.TryExecuteIfActive{T}"/>
    /// also requires, immediately after the session was deauthorized as part of that same atomic publish
    /// -- deterministic because a genuinely later, real cross-thread capture attempt could only observe
    /// the session gone even later than this exact instant, never earlier. The capture must fail
    /// outright: in production this means <see cref="DovahLink.Host.Client.Dispatch.ClientMessageDispatcher"/>
    /// never calls <see cref="TrustAdminService.RenameAsync"/> at all, reporting <c>stale_session</c> instead --
    /// there is no target incarnation for a rename to even be attempted against.
    /// </summary>
    [Fact]
    public async Task RenameAuthorizationBoundary_AdminMutationWinsFirst_CaptureNeverSucceeds()
    {
        var persistence = new FakeTrustStorePersistence();
        (TrustStore trustStore, SessionRegistry sessions, _, TrustAdminService admin, _) = await ComposeRealCollaboratorsAsync(persistence);
        ClientId clientId = ClientId.NewId();
        await trustStore.UpsertAsync(new TrustRecord(clientId, "11111", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        ConnectionId connectionId = ConnectionId.NewId();
        Assert.True(sessions.TryCreate(clientId, connectionId, SessionAuthenticationSource.Unpaired, SessionTrustTier.Full, out SessionId sessionId));

        bool captureAttempted = false;
        bool captureSucceeded = false;
        TrustMutationOutcome outcome = await trustStore.RevokeAsync(clientId, onPublished: () =>
        {
            sessions.InvalidateAllForClient(clientId, SessionInvalidationReason.Revoked);
            captureAttempted = true;
            captureSucceeded = sessions.TryExecuteIfActive(
                sessionId, connectionId, () => admin.TryCaptureTrustedIncarnation(clientId), out KnownDeviceIncarnationId? _);
        });

        Assert.Equal(TrustMutationOutcome.Changed, outcome);
        Assert.True(captureAttempted);
        Assert.False(captureSucceeded);
    }
}
