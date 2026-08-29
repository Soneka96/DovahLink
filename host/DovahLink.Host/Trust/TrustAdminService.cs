using DovahLink.Host.Identity;
using DovahLink.Host.Sessions;

namespace DovahLink.Host.Trust;

/// <summary>
/// Administrative operations on known devices: listing, renaming, revoking, and blocking. Revoking
/// or blocking a device immediately invalidates any of its active sessions, per
/// <c>ai/context/host/migration-audit.md</c>'s "Revocation immediacy".
/// </summary>
public interface ITrustAdminService
{
    /// <summary>Lists every currently known device.</summary>
    IReadOnlyList<TrustRecord> List();

    /// <summary>Changes a known device's display name.</summary>
    /// <param name="clientId">The device to rename.</param>
    /// <param name="displayName">The new display name.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    Task RenameAsync(ClientId clientId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Revokes a known device's trust and invalidates its active sessions.</summary>
    /// <param name="clientId">The device to revoke.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    Task RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Blocks a known device from pairing or reconnecting and invalidates its active sessions.</summary>
    /// <param name="clientId">The device to block.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    Task BlockAsync(ClientId clientId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ITrustAdminService"/>
public sealed class TrustAdminService : ITrustAdminService
{
    /// <summary>The trust records administered by this service.</summary>
    private readonly ITrustStore trustStore;

    /// <summary>The session registry whose sessions are invalidated when a device is revoked or blocked.</summary>
    private readonly ISessionRegistry sessionRegistry;

    /// <summary>Creates a trust administration service.</summary>
    /// <param name="trustStore">The trust records to administer.</param>
    /// <param name="sessionRegistry">The session registry to invalidate sessions in on revoke or block.</param>
    public TrustAdminService(ITrustStore trustStore, ISessionRegistry sessionRegistry)
    {
        this.trustStore = trustStore;
        this.sessionRegistry = sessionRegistry;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrustRecord> List() => trustStore.List();

    /// <inheritdoc/>
    public async Task RenameAsync(ClientId clientId, string displayName, CancellationToken cancellationToken = default)
    {
        TrustRecord record = GetKnownRecord(clientId);
        await trustStore.UpsertAsync(record with { DisplayName = displayName }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        TrustRecord record = GetKnownRecord(clientId);
        await trustStore.UpsertAsync(record with { State = KnownDeviceState.Revoked }, cancellationToken);
        sessionRegistry.InvalidateAllForClient(clientId);
    }

    /// <inheritdoc/>
    public async Task BlockAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        TrustRecord record = GetKnownRecord(clientId);
        await trustStore.UpsertAsync(record with { State = KnownDeviceState.Blocked }, cancellationToken);
        sessionRegistry.InvalidateAllForClient(clientId);
    }

    /// <summary>Looks up a device's trust record, or fails if the device is not known.</summary>
    /// <param name="clientId">The device to look up.</param>
    /// <returns>The device's current trust record.</returns>
    /// <exception cref="KeyNotFoundException">No device with this <paramref name="clientId"/> is known.</exception>
    private TrustRecord GetKnownRecord(ClientId clientId) =>
        trustStore.TryGet(clientId) ?? throw new KeyNotFoundException($"No known device for client '{clientId}'.");
}
