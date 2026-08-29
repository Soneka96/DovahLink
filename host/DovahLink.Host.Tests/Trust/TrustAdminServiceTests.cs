using DovahLink.Host.Identity;
using DovahLink.Host.Trust;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="TrustAdminService"/>.</summary>
public class TrustAdminServiceTests
{
    /// <summary>Verifies that List() delegates directly to the trust store.</summary>
    [Fact]
    public void List_ReturnsTrustStoreRecords()
    {
        var trustStore = new FakeTrustStore();
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        trustStore.Seed(record);
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry());

        Assert.Equal([record], admin.List());
    }

    /// <summary>Verifies that List() on a trust store with no known devices returns an empty list.</summary>
    [Fact]
    public void List_EmptyStore_ReturnsEmpty()
    {
        var admin = new TrustAdminService(new FakeTrustStore(), new FakeSessionRegistry());

        Assert.Empty(admin.List());
    }

    /// <summary>Verifies that renaming a known device updates only its display name.</summary>
    [Fact]
    public async Task RenameAsync_KnownClient_UpdatesDisplayNameOnly()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        var record = new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        trustStore.Seed(record);
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry());

        await admin.RenameAsync(clientId, "Bedroom PC");

        TrustRecord updated = trustStore.TryGet(clientId)!;
        Assert.Equal("Bedroom PC", updated.DisplayName);
        Assert.Equal(record.State, updated.State);
    }

    /// <summary>Verifies that renaming an unknown client is rejected rather than silently creating a record.</summary>
    [Fact]
    public async Task RenameAsync_UnknownClient_Throws()
    {
        var admin = new TrustAdminService(new FakeTrustStore(), new FakeSessionRegistry());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.RenameAsync(ClientId.NewId(), "New Name"));
    }

    /// <summary>Verifies that a persistence failure during rename propagates rather than being swallowed.</summary>
    [Fact]
    public async Task RenameAsync_PersistenceFails_Throws()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, new FakeSessionRegistry());

        await Assert.ThrowsAsync<IOException>(() => admin.RenameAsync(clientId, "Bedroom PC"));
    }

    /// <summary>Verifies that revoking a known device sets its state to Revoked and invalidates its sessions.</summary>
    [Fact]
    public async Task RevokeAsync_KnownClient_RevokesAndInvalidatesSessions()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, sessionRegistry);
        SessionId session = sessionRegistry.Create(clientId);

        await admin.RevokeAsync(clientId);

        Assert.Equal(KnownDeviceState.Revoked, trustStore.TryGet(clientId)!.State);
        Assert.False(sessionRegistry.IsActive(session));
        Assert.Equal([clientId], sessionRegistry.InvalidateAllForClientCalls);
    }

    /// <summary>Verifies that revoking a device with multiple active sessions invalidates every one of them.</summary>
    [Fact]
    public async Task RevokeAsync_ClientWithMultipleSessions_InvalidatesAllOfThem()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, sessionRegistry);
        SessionId firstSession = sessionRegistry.Create(clientId);
        SessionId secondSession = sessionRegistry.Create(clientId);

        await admin.RevokeAsync(clientId);

        Assert.False(sessionRegistry.IsActive(firstSession));
        Assert.False(sessionRegistry.IsActive(secondSession));
    }

    /// <summary>Verifies that revoking an unknown client is rejected and never touches the session registry.</summary>
    [Fact]
    public async Task RevokeAsync_UnknownClient_ThrowsAndDoesNotInvalidateSessions()
    {
        var sessionRegistry = new FakeSessionRegistry();
        var admin = new TrustAdminService(new FakeTrustStore(), sessionRegistry);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.RevokeAsync(ClientId.NewId()));

        Assert.Empty(sessionRegistry.InvalidateAllForClientCalls);
    }

    /// <summary>
    /// Verifies that when the trust store's write fails, revoke never invalidates the client's
    /// sessions -- a revocation that didn't actually persist must not kill sessions as if it had.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_PersistenceFails_DoesNotInvalidateSessions()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var sessionRegistry = new FakeSessionRegistry();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, sessionRegistry);
        SessionId session = sessionRegistry.Create(clientId);

        await Assert.ThrowsAsync<IOException>(() => admin.RevokeAsync(clientId));

        Assert.True(sessionRegistry.IsActive(session));
        Assert.Empty(sessionRegistry.InvalidateAllForClientCalls);
    }

    /// <summary>Verifies that blocking a known device sets its state to Blocked and invalidates its sessions.</summary>
    [Fact]
    public async Task BlockAsync_KnownClient_BlocksAndInvalidatesSessions()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, sessionRegistry);
        SessionId session = sessionRegistry.Create(clientId);

        await admin.BlockAsync(clientId);

        Assert.Equal(KnownDeviceState.Blocked, trustStore.TryGet(clientId)!.State);
        Assert.False(sessionRegistry.IsActive(session));
        Assert.Equal([clientId], sessionRegistry.InvalidateAllForClientCalls);
    }

    /// <summary>Verifies that blocking an unknown client is rejected.</summary>
    [Fact]
    public async Task BlockAsync_UnknownClient_Throws()
    {
        var admin = new TrustAdminService(new FakeTrustStore(), new FakeSessionRegistry());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.BlockAsync(ClientId.NewId()));
    }

    /// <summary>
    /// Verifies that when the trust store's write fails, block never invalidates the client's
    /// sessions, mirroring RevokeAsync's same guarantee.
    /// </summary>
    [Fact]
    public async Task BlockAsync_PersistenceFails_DoesNotInvalidateSessions()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var sessionRegistry = new FakeSessionRegistry();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, sessionRegistry);
        SessionId session = sessionRegistry.Create(clientId);

        await Assert.ThrowsAsync<IOException>(() => admin.BlockAsync(clientId));

        Assert.True(sessionRegistry.IsActive(session));
        Assert.Empty(sessionRegistry.InvalidateAllForClientCalls);
    }

    /// <summary>Verifies that resetting a known device sets its state to Unpaired and invalidates its active sessions.</summary>
    [Fact]
    public async Task ResetAsync_KnownClient_ResetsAndInvalidatesSessions()
    {
        var trustStore = new FakeTrustStore();
        var sessionRegistry = new FakeSessionRegistry();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, sessionRegistry);
        SessionId session = sessionRegistry.Create(clientId);

        await admin.ResetAsync(clientId);

        Assert.Equal(KnownDeviceState.Unpaired, trustStore.TryGet(clientId)!.State);
        Assert.False(sessionRegistry.IsActive(session));
        Assert.Equal([clientId], sessionRegistry.InvalidateAllForClientCalls);
    }

    /// <summary>Verifies that resetting an unknown client is rejected.</summary>
    [Fact]
    public async Task ResetAsync_UnknownClient_Throws()
    {
        var admin = new TrustAdminService(new FakeTrustStore(), new FakeSessionRegistry());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.ResetAsync(ClientId.NewId()));
    }

    /// <summary>
    /// Verifies that when the trust store's write fails, reset never invalidates the client's
    /// sessions, mirroring RevokeAsync's and BlockAsync's same guarantee.
    /// </summary>
    [Fact]
    public async Task ResetAsync_PersistenceFails_DoesNotInvalidateSessions()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var sessionRegistry = new FakeSessionRegistry();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var admin = new TrustAdminService(trustStore, sessionRegistry);
        SessionId session = sessionRegistry.Create(clientId);

        await Assert.ThrowsAsync<IOException>(() => admin.ResetAsync(clientId));

        Assert.True(sessionRegistry.IsActive(session));
        Assert.Empty(sessionRegistry.InvalidateAllForClientCalls);
    }
}
