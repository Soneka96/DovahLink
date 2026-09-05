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
    /// <param name="expectedIncarnation">
    /// The Known Device incarnation this rename was authorized against, captured by the caller (via
    /// <see cref="TryCaptureTrustedIncarnation"/>) while its own authorization boundary -- for example a
    /// session's <see cref="ISessionRegistry.TryExecuteIfActive{T}"/> -- was still the authority, never
    /// re-resolved here after that boundary has already been released. The mutation is applied only if
    /// <paramref name="clientId"/>'s current record still carries this exact incarnation; a mismatch
    /// reports the same rejection as an unrecognized or non-Trusted identity.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel persistence.</param>
    Task RenameAsync(ClientId clientId, string displayName, KnownDeviceIncarnationId expectedIncarnation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously and without side effects reads whether <paramref name="clientId"/> currently has a
    /// <see cref="KnownDeviceState.Trusted"/> record and, if so, its incarnation -- the narrow, bounded
    /// read a caller performs while its own authorization boundary is still the authority, so the
    /// captured value can be trusted as an <see cref="RenameAsync"/> precondition for a later async
    /// mutation authorized against this exact snapshot rather than a snapshot re-resolved after that
    /// boundary was released. Never awaits, persists, notifies, or performs adapter/transport I/O.
    /// </summary>
    /// <param name="clientId">The device to read.</param>
    /// <returns>
    /// The device's current incarnation when it is currently <see cref="KnownDeviceState.Trusted"/>;
    /// otherwise <see langword="null"/>, whether because the identity is unrecognized or because it is
    /// known but not currently Trusted.
    /// </returns>
    KnownDeviceIncarnationId? TryCaptureTrustedIncarnation(ClientId clientId);

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
    /// <remarks>
    /// Applies the mutation using exactly the <paramref name="expectedIncarnation"/> the caller already
    /// captured -- never re-resolving <paramref name="clientId"/>'s current record here. This closes the
    /// stale async request ABA race: if this exact Known Device is forgotten and <paramref name="clientId"/>
    /// later re-pairs -- even under the exact same reused shortId -- while this call is still queued
    /// behind another in-flight <see cref="ITrustStore"/> mutation, the caller's earlier-captured
    /// incarnation no longer matches the replacement record's own, and the eventual
    /// <see cref="ITrustStore.RenameIfTrustedAsync"/> call reports <see cref="TrustMutationOutcome.NotFound"/>
    /// rather than silently renaming the replacement.
    /// </remarks>
    public async Task RenameAsync(ClientId clientId, string displayName, KnownDeviceIncarnationId expectedIncarnation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ValidateDisplayName(displayName);

        TrustMutationOutcome outcome = await trustStore.RenameIfTrustedAsync(clientId, displayName, cancellationToken, expectedIncarnation);
        if (outcome == TrustMutationOutcome.NotFound)
        {
            throw new KeyNotFoundException($"No known device for client '{clientId}'.");
        }
        if (outcome == TrustMutationOutcome.NotEligible)
        {
            throw new InvalidOperationException("Only a trusted device can be renamed.");
        }
    }

    /// <inheritdoc/>
    public KnownDeviceIncarnationId? TryCaptureTrustedIncarnation(ClientId clientId) =>
        trustStore.TryGet(clientId) is { State: KnownDeviceState.Trusted } record ? record.Incarnation : null;

    /// <inheritdoc/>
    public async Task RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        TrustMutationOutcome outcome = await RevokeCoreAsync(clientId, expectedIncarnation: null, cancellationToken);
        EnsureChangedOrAlreadyHandled(outcome, clientId, "revoke");
    }

    /// <inheritdoc/>
    public async Task BlockAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        TrustMutationOutcome outcome = await BlockCoreAsync(clientId, expectedIncarnation: null, cancellationToken);
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
    /// <remarks>
    /// Every affected session becomes unauthorized in the registry -- <c>onPublished</c>'s own
    /// synchronous, immediate <see cref="IClientSessionInvalidator.InvalidateClients"/> call, run by
    /// <see cref="ITrustStore.ResetTrustAsync"/> itself inside the same security-state-gate critical
    /// section its own publish and generation advance happen in -- so no request on one of these
    /// sessions can ever observe the mutation already published while still finding itself active in
    /// the registry: no such post-mutation window exists for it to land in at all, closing the race a
    /// separate call issued only after this method's own <c>await</c> returned could otherwise leave
    /// open. Batch invalidation also removes every affected client in one atomic registry pass, rather
    /// than a per-client loop that would leave client B authorized while client A's teardown is still
    /// in flight. Pairing cancellation follows immediately after, before any best-effort notification
    /// is even attempted, per <c>ai/context/protocol/security.md</c>'s authoritative-mutation -&gt;
    /// unauthorized -&gt; future authentication/pairing enforcement -&gt; notification -&gt; close
    /// ordering.
    /// </remarks>
    public async Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SessionInvalidationTarget> targets = [];
        IReadOnlyList<ClientId> affected = await trustStore.ResetTrustAsync(
            cancellationToken, onPublished: affectedClients => targets = sessionInvalidator.InvalidateClients(affectedClients, SessionInvalidationReason.TrustReset));

        pairingCoordinator.CancelAll();
        if (affected.Count > 0)
        {
            await sessionInvalidator.NotifyAndCloseAllAsync(targets, cancellationToken);
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
        MutateByShortIdAsync(shortId, (clientId, expectedIncarnation, ct) => trustStore.UnblockAsync(clientId, ct, expectedIncarnation), cancellationToken);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> ForgetByShortIdAsync(string shortId, CancellationToken cancellationToken = default) =>
        MutateByShortIdAsync(shortId, ForgetCoreAsync, cancellationToken);

    /// <summary>
    /// Revokes a client and performs successful-mutation side effects. The session becomes
    /// unauthorized in the registry -- <c>onPublished</c>'s own synchronous, immediate
    /// <see cref="IClientSessionInvalidator.InvalidateClient"/> call, run by
    /// <see cref="ITrustStore.RevokeAsync"/> itself inside the same security-state-gate critical
    /// section its own publish and generation advance happen in -- so no request on this session can
    /// ever observe the mutation already published while still finding itself active in the registry:
    /// no such post-mutation window exists for it to land in at all, closing the race a separate call
    /// issued only after this method's own <c>await</c> returned could otherwise leave open. Pairing
    /// cancellation follows immediately after, before any best-effort notification is even attempted,
    /// per <c>ai/context/protocol/security.md</c>'s authoritative-mutation -&gt; unauthorized -&gt;
    /// future authentication/pairing enforcement -&gt; notification -&gt; close ordering.
    /// </summary>
    /// <param name="clientId">The client to revoke.</param>
    /// <param name="expectedIncarnation">The incarnation precondition to enforce, or <see langword="null"/> when this client was resolved directly rather than by shortId.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    private async Task<TrustMutationOutcome> RevokeCoreAsync(ClientId clientId, KnownDeviceIncarnationId? expectedIncarnation, CancellationToken cancellationToken)
    {
        IReadOnlyList<SessionInvalidationTarget> targets = [];
        TrustMutationOutcome outcome = await trustStore.RevokeAsync(
            clientId, cancellationToken, expectedIncarnation,
            onPublished: () => targets = sessionInvalidator.InvalidateClient(clientId, SessionInvalidationReason.Revoked));
        if (outcome == TrustMutationOutcome.Changed)
        {
            pairingCoordinator.Cancel(clientId);
            await sessionInvalidator.NotifyAndCloseAllAsync(targets, cancellationToken);
        }

        return outcome;
    }

    /// <summary>Blocks a client and performs successful-mutation side effects. See <see cref="RevokeCoreAsync"/>'s own remarks for this ordering.</summary>
    /// <param name="clientId">The client to block.</param>
    /// <param name="expectedIncarnation">The incarnation precondition to enforce, or <see langword="null"/> when this client was resolved directly rather than by shortId.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    private async Task<TrustMutationOutcome> BlockCoreAsync(ClientId clientId, KnownDeviceIncarnationId? expectedIncarnation, CancellationToken cancellationToken)
    {
        IReadOnlyList<SessionInvalidationTarget> targets = [];
        TrustMutationOutcome outcome = await trustStore.BlockAsync(
            clientId, cancellationToken, expectedIncarnation,
            onPublished: () => targets = sessionInvalidator.InvalidateClient(clientId, SessionInvalidationReason.Blocked));
        if (outcome == TrustMutationOutcome.Changed)
        {
            pairingCoordinator.Cancel(clientId);
            await sessionInvalidator.NotifyAndCloseAllAsync(targets, cancellationToken);
        }

        return outcome;
    }

    /// <summary>Forgets a client and cancels any pairing state it owns.</summary>
    /// <param name="clientId">The client to forget.</param>
    /// <param name="expectedIncarnation">The incarnation precondition to enforce, or <see langword="null"/> when this client was resolved directly rather than by shortId.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    private async Task<TrustMutationOutcome> ForgetCoreAsync(ClientId clientId, KnownDeviceIncarnationId? expectedIncarnation, CancellationToken cancellationToken)
    {
        TrustMutationOutcome outcome = await trustStore.ForgetAsync(clientId, cancellationToken, expectedIncarnation);
        if (outcome == TrustMutationOutcome.Changed)
        {
            pairingCoordinator.Cancel(clientId);
        }

        return outcome;
    }

    /// <summary>
    /// Resolves a short ID and applies one client-ID mutation, passing the resolved record's own
    /// <see cref="TrustRecord.Incarnation"/> back to <paramref name="mutation"/> as its precondition so
    /// the mutation is applied only if the target's current record still carries it -- see
    /// <see cref="ITrustStore.RevokeAsync"/>'s remarks for the ABA race this closes. Never captures a
    /// resolved <see cref="ClientId"/> and incarnation separately for use across an await without
    /// carrying the incarnation along as an atomic precondition for the mutation itself. <paramref name="shortId"/>
    /// itself remains only the human-facing selector used to resolve the record in the first place.
    /// </summary>
    private async Task<TrustMutationOutcome> MutateByShortIdAsync(
        string shortId,
        Func<ClientId, KnownDeviceIncarnationId?, CancellationToken, Task<TrustMutationOutcome>> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shortId);
        TrustRecord? record = trustStore.TryGetByShortId(shortId);
        return record is null
            ? TrustMutationOutcome.NotFound
            : await mutation(record.ClientId, record.Incarnation, cancellationToken);
    }

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
