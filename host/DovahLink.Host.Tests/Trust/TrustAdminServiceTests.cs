using DovahLink.Host.Identity;
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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(new FakeTrustStore(), new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

        await admin.RenameAsync(clientId, "Bedroom PC");
        await admin.RenameAsync(clientId, string.Empty);

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

        await Assert.ThrowsAsync<ArgumentException>(() => admin.RenameAsync(clientId, "Bad\nName"));
    }

    /// <summary>Verifies that non-trusted devices cannot be renamed.</summary>
    [Fact]
    public async Task RenameAsync_NonTrustedDevice_Throws()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => admin.RenameAsync(clientId, "New Name"));
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
        var admin = new TrustAdminService(trustStore, sessions, pairing);

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
        var admin = new TrustAdminService(trustStore, sessions, new FakePairingCoordinator());

        await admin.RevokeAsync(target);

        Assert.False(sessions.IsActive(targetSession, sessions.ConnectionIdFor(targetSession)));
        Assert.True(sessions.IsActive(otherSession, sessions.ConnectionIdFor(otherSession)));
    }

    /// <summary>Verifies that blocking an unpaired known device is allowed and creates no credential.</summary>
    [Fact]
    public async Task BlockAsync_UnpairedDevice_BlocksAndInvalidatesSessions()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", null, KnownDeviceState.Unpaired, string.Empty, DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

        await admin.BlockAsync(clientId);

        TrustRecord blocked = trustStore.TryGet(clientId)!;
        Assert.Equal(KnownDeviceState.Blocked, blocked.State);
        Assert.NotNull(blocked.BlockedAtUtc);
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
        var admin = new TrustAdminService(trustStore, sessions, pairing);

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), pairing);

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), pairing);

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
        var admin = new TrustAdminService(trustStore, sessions, pairing);

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
        var admin = new TrustAdminService(trustStore, sessions, pairing);

        await admin.ResetTrustAsync();

        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(trusted)!.State);
        Assert.Equal(blocked, trustStore.TryGet(blocked)!.ClientId);
        Assert.Equal(KnownDeviceState.Blocked, trustStore.TryGet(blocked)!.State);
        Assert.False(sessions.IsActive(session, sessions.ConnectionIdFor(session)));
    }

    /// <summary>Verifies that Reset Trust cancels pairing even when no trusted device needs revocation.</summary>
    [Fact]
    public async Task ResetTrustAsync_NoTrustedDevices_StillCancelsPairing()
    {
        var pairing = new FakePairingCoordinator();
        var admin = new TrustAdminService(new FakeTrustStore(), new FakeSessionRegistry(), pairing);

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
        var admin = new TrustAdminService(trustStore, sessions, pairing);

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
        var admin = new TrustAdminService(trustStore, sessions, pairing);

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

        TrustMutationOutcome outcome = await admin.RevokeByShortIdAsync("12345");

        Assert.Equal(TrustMutationOutcome.Changed, outcome);
        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>Verifies that an unknown short ID produces a not-found outcome without mutation.</summary>
    [Fact]
    public async Task BlockByShortIdAsync_UnknownId_ReturnsNotFound()
    {
        var admin = new TrustAdminService(new FakeTrustStore(), new FakeSessionRegistry(), new FakePairingCoordinator());

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
        var admin = new TrustAdminService(trustStore, sessions, pairing);

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
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), new FakePairingCoordinator());

        Assert.Equal(TrustMutationOutcome.Changed, await admin.UnblockByShortIdAsync("12345"));
        Assert.Equal(TrustMutationOutcome.Changed, await admin.ForgetByShortIdAsync("12345"));
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the exact security-mandated ordering for Revoke: the authoritative trust-store
    /// mutation happens before pairing is cancelled, which happens before the session is invalidated
    /// -- per <c>ai/context/protocol/security.md</c>'s "authoritative state change, credential
    /// invalidation where applicable, future authentication/pairing enforcement, ... then forced
    /// close" ordering.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_AppliesSideEffectsInTheMandatedOrder()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var sessions = new FakeSessionRegistry { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, sessions, pairing);

        await admin.RevokeAsync(clientId);

        Assert.Equal(["Revoke", "Cancel", "InvalidateAllForClient"], order);
    }

    /// <summary>Verifies the same mandated ordering for Block.</summary>
    [Fact]
    public async Task BlockAsync_AppliesSideEffectsInTheMandatedOrder()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var sessions = new FakeSessionRegistry { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, sessions, pairing);

        await admin.BlockAsync(clientId);

        Assert.Equal(["Block", "Cancel", "InvalidateAllForClient"], order);
    }

    /// <summary>Verifies the same mandated ordering for Reset Trust, including per-affected-device invalidation.</summary>
    [Fact]
    public async Task ResetTrustAsync_AppliesSideEffectsInTheMandatedOrder()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var sessions = new FakeSessionRegistry { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow));
        sessions.Create(clientId);
        var admin = new TrustAdminService(trustStore, sessions, pairing);

        await admin.ResetTrustAsync();

        Assert.Equal(["ResetTrust", "CancelAll", "InvalidateAllForClient"], order);
    }

    /// <summary>
    /// Verifies that Reset Trust with no trusted devices still cancels pairing (proven by the existing
    /// <see cref="ResetTrustAsync_NoTrustedDevices_StillCancelsPairing"/> test) without the store ever
    /// reporting a mutation that did not occur.
    /// </summary>
    [Fact]
    public async Task ResetTrustAsync_NoTrustedDevices_StoreReportsNoMutation()
    {
        List<string> order = [];
        var trustStore = new FakeTrustStore { OnMutationApplied = order.Add };
        var pairing = new FakePairingCoordinator { OnMutationApplied = order.Add };
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry(), pairing);

        await admin.ResetTrustAsync();

        Assert.Equal(["CancelAll"], order);
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
        var admin = new TrustAdminService(trustStore, sessions, pairing);

        await admin.ForgetAsync(clientId);

        Assert.Equal(["Forget", "Cancel"], order);
    }
}
