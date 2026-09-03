using System.Text;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.Sessions;

namespace DovahLink.Host.Trust;

/// <summary>
/// Coordinates known-device administration over the durable trust store, pairing state, and active
/// sessions. It uses short IDs for human-facing selection and client IDs for security identity.
/// </summary>
public interface ITrustAdminService
{
    /// <summary>Lists known devices using the all, trust, or block scope.</summary>
    IReadOnlyList<TrustRecord> List(string scope = "all");

    /// <summary>Returns the canonical trust-administration command help.</summary>
    string Help();

    /// <summary>Changes a trusted device's optional display name.</summary>
    /// <param name="clientId">The device to rename.</param>
    /// <param name="displayName">The new name, or an empty value to clear it.</param>
    /// <param name="cancellationToken">The token used to cancel persistence.</param>
    Task RenameAsync(ClientId clientId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Revokes a trusted device and invalidates its sessions.</summary>
    Task RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Blocks a known device and invalidates its sessions.</summary>
    Task BlockAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Unblocks a device and returns it to the unpaired state.</summary>
    Task UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Forgets an eligible revoked or unpaired device.</summary>
    Task ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Applies Reset Trust to every trusted device and invalidates affected sessions.</summary>
    Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes the device selected by short ID and returns its mutation outcome.</summary>
    Task<TrustMutationOutcome> RevokeByShortIdAsync(string shortId, CancellationToken cancellationToken = default);

    /// <summary>Blocks the device selected by short ID and returns its mutation outcome.</summary>
    Task<TrustMutationOutcome> BlockByShortIdAsync(string shortId, CancellationToken cancellationToken = default);

    /// <summary>Unblocks the device selected by short ID and returns its mutation outcome.</summary>
    Task<TrustMutationOutcome> UnblockByShortIdAsync(string shortId, CancellationToken cancellationToken = default);

    /// <summary>Forgets the device selected by short ID and returns its mutation outcome.</summary>
    Task<TrustMutationOutcome> ForgetByShortIdAsync(string shortId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ITrustAdminService"/>
public sealed class TrustAdminService : ITrustAdminService
{
    /// <summary>The durable trust domain administered by this service.</summary>
    private readonly ITrustStore trustStore;

    /// <summary>The seam through which successful security mutations invalidate active sessions.</summary>
    private readonly IClientSessionInvalidator sessionInvalidator;

    /// <summary>The pairing state cancelled by successful security mutations.</summary>
    private readonly IPairingCoordinator pairingCoordinator;

    /// <summary>Creates a trust administration service.</summary>
    /// <param name="trustStore">The durable trust domain.</param>
    /// <param name="sessionInvalidator">The seam used to invalidate sessions on security mutations.</param>
    /// <param name="pairingCoordinator">The pairing state to cancel on security mutations.</param>
    public TrustAdminService(ITrustStore trustStore, IClientSessionInvalidator sessionInvalidator, IPairingCoordinator pairingCoordinator)
    {
        this.trustStore = trustStore;
        this.sessionInvalidator = sessionInvalidator;
        this.pairingCoordinator = pairingCoordinator;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrustRecord> List(string scope = "all")
    {
        ArgumentNullException.ThrowIfNull(scope);
        IReadOnlyList<TrustRecord> records = scope.ToLowerInvariant() switch
        {
            "all" or "" => trustStore.List().OrderBy(record => record.PairedAtUtc).ThenBy(record => record.ShortId).ToList(),
            "trust" => trustStore.List().Where(record => record.State == KnownDeviceState.Trusted).OrderBy(record => record.PairedAtUtc).ThenBy(record => record.ShortId).ToList(),
            "block" => trustStore.List().Where(record => record.State == KnownDeviceState.Blocked).OrderBy(record => record.PairedAtUtc).ThenBy(record => record.ShortId).ToList(),
            _ => throw new ArgumentException("Scope must be all, trust, or block.", nameof(scope)),
        };

        Dictionary<string, int> counts = records
            .Where(record => !string.IsNullOrEmpty(record.DisplayName))
            .GroupBy(record => record.DisplayName!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, int> indexes = new(StringComparer.Ordinal);
        return records.Select(record =>
        {
            if (string.IsNullOrEmpty(record.DisplayName) || counts[record.DisplayName] < 2)
            {
                return record;
            }

            int index = indexes.TryGetValue(record.DisplayName, out int currentIndex)
                ? currentIndex + 1
                : 1;
            indexes[record.DisplayName] = index;
            return record with { DisplayName = $"{record.DisplayName} #{index}" };
        }).ToList();
    }

    /// <inheritdoc/>
    public string Help() =>
        "DovahLink commands:\n" +
        " list [all|trust|block]\n" +
        " revoke -id <id> | block -id <id> | unblock -id <id> | forget -id <id>\n" +
        " reset-trust | reset | confirm-reset -confirm <code> | help";

    /// <inheritdoc/>
    public async Task RenameAsync(ClientId clientId, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ValidateDisplayName(displayName);
        TrustRecord record = GetKnownRecord(clientId);
        if (record.State != KnownDeviceState.Trusted)
        {
            throw new InvalidOperationException("Only a trusted device can be renamed.");
        }

        await trustStore.UpsertAsync(record with { DisplayName = displayName }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        TrustMutationOutcome outcome = await RevokeCoreAsync(clientId, cancellationToken);
        EnsureChangedOrAlreadyHandled(outcome, clientId, "revoke");
    }

    /// <inheritdoc/>
    public async Task BlockAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        TrustMutationOutcome outcome = await BlockCoreAsync(clientId, cancellationToken);
        EnsureChangedOrAlreadyHandled(outcome, clientId, "block");
    }

    /// <inheritdoc/>
    public async Task UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        TrustMutationOutcome outcome = await trustStore.UnblockAsync(clientId, cancellationToken);
        if (outcome == TrustMutationOutcome.NotFound)
        {
            throw new KeyNotFoundException($"No known device for client '{clientId}'.");
        }
    }

    /// <inheritdoc/>
    public async Task ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        TrustMutationOutcome outcome = await trustStore.ForgetAsync(clientId, cancellationToken);
        if (outcome == TrustMutationOutcome.NotFound)
        {
            throw new KeyNotFoundException($"No known device for client '{clientId}'.");
        }
        if (outcome == TrustMutationOutcome.NotEligible)
        {
            throw new InvalidOperationException("Only revoked or unpaired devices can be forgotten.");
        }

        pairingCoordinator.Cancel(clientId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClientId> affected = await trustStore.ResetTrustAsync(cancellationToken);
        pairingCoordinator.CancelAll();
        foreach (ClientId clientId in affected)
        {
            await sessionInvalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.TrustReset, cancellationToken);
        }

        return affected;
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> RevokeByShortIdAsync(string shortId, CancellationToken cancellationToken = default) =>
        MutateByShortIdAsync(shortId, RevokeCoreAsync, cancellationToken);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> BlockByShortIdAsync(string shortId, CancellationToken cancellationToken = default) =>
        MutateByShortIdAsync(shortId, BlockCoreAsync, cancellationToken);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> UnblockByShortIdAsync(string shortId, CancellationToken cancellationToken = default) =>
        MutateByShortIdAsync(shortId, trustStore.UnblockAsync, cancellationToken);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> ForgetByShortIdAsync(string shortId, CancellationToken cancellationToken = default) =>
        MutateByShortIdAsync(shortId, ForgetCoreAsync, cancellationToken);

    /// <summary>Revokes a client and performs successful-mutation side effects.</summary>
    private async Task<TrustMutationOutcome> RevokeCoreAsync(ClientId clientId, CancellationToken cancellationToken)
    {
        TrustMutationOutcome outcome = await trustStore.RevokeAsync(clientId, cancellationToken);
        if (outcome == TrustMutationOutcome.Changed)
        {
            pairingCoordinator.Cancel(clientId);
            await sessionInvalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.Revoked, cancellationToken);
        }

        return outcome;
    }

    /// <summary>Blocks a client and performs successful-mutation side effects.</summary>
    private async Task<TrustMutationOutcome> BlockCoreAsync(ClientId clientId, CancellationToken cancellationToken)
    {
        TrustMutationOutcome outcome = await trustStore.BlockAsync(clientId, cancellationToken);
        if (outcome == TrustMutationOutcome.Changed)
        {
            pairingCoordinator.Cancel(clientId);
            await sessionInvalidator.InvalidateClientAsync(clientId, SessionInvalidationReason.Blocked, cancellationToken);
        }

        return outcome;
    }

    /// <summary>Forgets a client and cancels any pairing state it owns.</summary>
    private async Task<TrustMutationOutcome> ForgetCoreAsync(ClientId clientId, CancellationToken cancellationToken)
    {
        TrustMutationOutcome outcome = await trustStore.ForgetAsync(clientId, cancellationToken);
        if (outcome == TrustMutationOutcome.Changed)
        {
            pairingCoordinator.Cancel(clientId);
        }

        return outcome;
    }

    /// <summary>Resolves a short ID and applies one client-ID mutation.</summary>
    private async Task<TrustMutationOutcome> MutateByShortIdAsync(
        string shortId,
        Func<ClientId, CancellationToken, Task<TrustMutationOutcome>> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shortId);
        TrustRecord? record = trustStore.TryGetByShortId(shortId);
        return record is null
            ? TrustMutationOutcome.NotFound
            : await mutation(record.ClientId, cancellationToken);
    }

    /// <summary>Returns a known client record or rejects an unknown identity.</summary>
    private TrustRecord GetKnownRecord(ClientId clientId) =>
        trustStore.TryGet(clientId) ?? throw new KeyNotFoundException($"No known device for client '{clientId}'.");

    /// <summary>Rejects a mutation result that cannot be represented by the legacy throwing API.</summary>
    private static void EnsureChangedOrAlreadyHandled(TrustMutationOutcome outcome, ClientId clientId, string operation)
    {
        if (outcome == TrustMutationOutcome.NotFound)
        {
            throw new KeyNotFoundException($"No known device for client '{clientId}'.");
        }
        if (outcome == TrustMutationOutcome.NotEligible)
        {
            throw new InvalidOperationException($"Device is not eligible for {operation}.");
        }
    }

    /// <summary>Validates the presentation-only display-name contract.</summary>
    private static void ValidateDisplayName(string displayName)
    {
        if (Encoding.UTF8.GetByteCount(displayName) > Constants.MaxDisplayNameLengthBytes || displayName.Any(char.IsControl))
        {
            throw new ArgumentException("The display name is not valid.", nameof(displayName));
        }
    }
}
