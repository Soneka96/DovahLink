using DovahLink.Host.Identity;
using DovahLink.Host.Security;
using DovahLink.Host.Time;

namespace DovahLink.Host.Trust;

/// <summary>The host's in-memory, persistence-backed index of every known device's trust record.</summary>
public interface ITrustStore
{
    /// <summary>Looks up the trust record for a client, if one exists.</summary>
    /// <param name="clientId">The client to look up.</param>
    /// <returns>The client's trust record, or <see langword="null"/> if the client is not known.</returns>
    TrustRecord? TryGet(ClientId clientId);

    /// <summary>
    /// Reads a client's current trust record and the store's current
    /// <see cref="SecurityFenceGeneration"/> as one coherent snapshot, under the same internal lock,
    /// so a caller deciding both eligibility and the generation to stamp on new state can never
    /// observe a mix of state from before one mutation and a generation from after it (or vice
    /// versa) -- see <see cref="TrustSecuritySnapshot"/>.
    /// </summary>
    /// <param name="clientId">The client to look up.</param>
    TrustSecuritySnapshot GetSecuritySnapshot(ClientId clientId);

    /// <summary>Lists every currently known trust record.</summary>
    IReadOnlyList<TrustRecord> List();

    /// <summary>
    /// Inserts a new trust record or replaces an existing one for the same client. The record
    /// becomes visible through <see cref="TryGet"/>, <see cref="List"/>, and
    /// <see cref="TryGetByShortId"/> at the same instant it becomes durably persisted -- never
    /// before, and a failed or cancelled write leaves the store exactly as it was.
    /// </summary>
    /// <param name="record">The record to store.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    Task UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every trust record as one mutation. Every record stays visible through
    /// <see cref="TryGet"/>, <see cref="List"/>, and <see cref="TryGetByShortId"/> until the empty
    /// set is durably persisted, and a failed or cancelled write leaves the store exactly as it was.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    /// <param name="onPublished">
    /// Invoked synchronously, once persistence has succeeded and the empty set has just become the
    /// published state, before any other caller can observe that state or a session's authorization
    /// against it -- the single ordering point at which a caller may deauthorize every affected
    /// session as part of the same indivisible event. Must be synchronous and bounded: it must never
    /// call back into pairing state, notification, transport, or persistence.
    /// </param>
    Task ClearAsync(CancellationToken cancellationToken = default, Action? onPublished = null);

    /// <summary>
    /// The monotonically advancing security fence used to invalidate an in-flight pending pairing
    /// operation against a concurrent administrative trust mutation. It advances on every mutation
    /// this store applies, including one that changed zero records (for example a Reset Trust with no
    /// currently trusted devices): the fence's purpose is to guarantee that no pairing operation which
    /// began before an administrative mutation can still complete after it, not to literally count
    /// records changed.
    /// </summary>
    long SecurityFenceGeneration { get; }

    /// <summary>
    /// Upserts a record only when the store still has the supplied fence generation. The record
    /// becomes visible through <see cref="TryGet"/>, <see cref="List"/>, and
    /// <see cref="TryGetByShortId"/> at the same instant it becomes durably persisted -- never
    /// before: a concurrent reader can never observe this upsert's outcome while its persistence
    /// write is still in flight.
    /// </summary>
    /// <param name="record">The record to store.</param>
    /// <param name="expectedGeneration">The fence generation observed before a pending operation began.</param>
    /// <param name="cancellationToken">The token used to cancel the persistence write.</param>
    /// <returns><see langword="true"/> when the record was persisted; otherwise the fence generation changed first.</returns>
    Task<bool> TryUpsertIfGenerationAsync(
        TrustRecord record,
        long expectedGeneration,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a known device by its administration-only short identifier.</summary>
    /// <param name="shortId">The five-digit administration identifier.</param>
    TrustRecord? TryGetByShortId(string shortId);

    /// <summary>
    /// Revokes a trusted device and destroys its credential verifier.
    /// </summary>
    /// <param name="clientId">The device to revoke.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    /// <param name="expectedShortId">
    /// When supplied, the mutation is applied only if <paramref name="clientId"/>'s current record
    /// still carries this exact <see cref="TrustRecord.ShortId"/> at the instant this store's
    /// serialized mutation actually resolves it -- never a value the caller resolved earlier and
    /// captured separately. Closes the incarnation race where a shortId-resolved administrative
    /// operation, queued behind another mutation, would otherwise land on an unrelated later
    /// incarnation of the same <paramref name="clientId"/> (for example forgotten and re-paired under
    /// a new shortId) rather than the exact Known Device the caller originally selected. A mismatch
    /// reports <see cref="TrustMutationOutcome.NotFound"/>, the same as an unrecognized identity,
    /// without mutation. Omit (or pass <see langword="null"/>) when the caller already resolved and
    /// authorized <paramref name="clientId"/> directly and has no shortId precondition to enforce.
    /// </param>
    /// <param name="onPublished">
    /// Invoked synchronously, only when this call actually changes the record, once persistence has
    /// succeeded and the change has just become the published state, before any other caller can
    /// observe it or a session's authorization against it -- the single ordering point at which a
    /// caller may deauthorize <paramref name="clientId"/>'s affected sessions as part of the same
    /// indivisible event. Must be synchronous and bounded: it must never call back into pairing
    /// state, notification, transport, or persistence.
    /// </param>
    Task<TrustMutationOutcome> RevokeAsync(
        ClientId clientId, CancellationToken cancellationToken = default, string? expectedShortId = null, Action? onPublished = null);

    /// <summary>
    /// Blocks a currently <see cref="KnownDeviceState.Trusted"/> or <see cref="KnownDeviceState.Revoked"/>
    /// known device and destroys its credential verifier. An already-<see cref="KnownDeviceState.Blocked"/>
    /// device reports <see cref="TrustMutationOutcome.AlreadyInState"/> without mutation; an
    /// <see cref="KnownDeviceState.Unpaired"/> device is never eligible for Block and reports
    /// <see cref="TrustMutationOutcome.NotEligible"/> without mutation.
    /// </summary>
    /// <param name="clientId">The device to block.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    /// <param name="expectedShortId">See <see cref="RevokeAsync"/>'s own remarks for this precondition's contract.</param>
    /// <param name="onPublished">See <see cref="RevokeAsync"/>'s own remarks for this callback's contract.</param>
    Task<TrustMutationOutcome> BlockAsync(
        ClientId clientId, CancellationToken cancellationToken = default, string? expectedShortId = null, Action? onPublished = null);

    /// <summary>
    /// Unblocks a blocked device and returns it to the unpaired state. Has no <c>onPublished</c>
    /// parameter, unlike <see cref="RevokeAsync"/>/<see cref="BlockAsync"/>: unblocking only restores
    /// eligibility for a future pairing, it never removes an already-authorized session's current
    /// trust, so there is never a session to deauthorize as part of this publish.
    /// </summary>
    /// <param name="clientId">The device to unblock.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    /// <param name="expectedShortId">See <see cref="RevokeAsync"/>'s own remarks for this precondition's contract.</param>
    Task<TrustMutationOutcome> UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default, string? expectedShortId = null);

    /// <summary>
    /// Forgets an eligible revoked or unpaired device completely. Has no <c>onPublished</c> parameter
    /// for the same reason as <see cref="UnblockAsync"/>: eligible only for a device that is already
    /// <see cref="KnownDeviceState.Revoked"/> or <see cref="KnownDeviceState.Unpaired"/>, neither of
    /// which has a currently trusted session to deauthorize.
    /// </summary>
    /// <param name="clientId">The device to forget.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    /// <param name="expectedShortId">See <see cref="RevokeAsync"/>'s own remarks for this precondition's contract.</param>
    Task<TrustMutationOutcome> ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default, string? expectedShortId = null);

    /// <summary>Applies Reset Trust to every trusted device and returns affected identities.</summary>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    /// <param name="onPublished">
    /// Invoked synchronously, once persistence (if any was needed) has succeeded and the change has
    /// just become the published state, before any other caller can observe it or a session's
    /// authorization against it -- the single ordering point at which a caller may deauthorize the
    /// affected sessions as part of the same indivisible event. Invoked with the exact affected
    /// client list this call returns, including an empty list when no record was currently trusted.
    /// Must be synchronous and bounded: it must never call back into pairing state, notification,
    /// transport, or persistence.
    /// </param>
    Task<IReadOnlyList<ClientId>> ResetTrustAsync(
        CancellationToken cancellationToken = default, Action<IReadOnlyList<ClientId>>? onPublished = null);

    /// <summary>
    /// Renames a currently trusted device, resolving and validating its current state inside this
    /// store's own serialized mutation -- never from a snapshot the caller read earlier -- so a
    /// concurrent Revoke, Block, Reset Trust, or Factory Reset that lands first is never overwritten
    /// by a stale replacement. Every other field, including the current credential verifier, is
    /// carried over unchanged from the record that exists at the mutation's linearization point.
    /// </summary>
    /// <param name="clientId">The device to rename.</param>
    /// <param name="displayName">The new display name, or an empty value to clear it.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    /// <param name="expectedShortId">See <see cref="RevokeAsync"/>'s own remarks for this precondition's contract.</param>
    /// <returns>
    /// <see cref="TrustMutationOutcome.Changed"/> once persisted; <see cref="TrustMutationOutcome.NotFound"/>
    /// for an unrecognized <paramref name="clientId"/>; <see cref="TrustMutationOutcome.NotEligible"/>
    /// when the device is not currently <see cref="KnownDeviceState.Trusted"/>, with no persistence in
    /// either rejection case.
    /// </returns>
    Task<TrustMutationOutcome> RenameIfTrustedAsync(
        ClientId clientId, string displayName, CancellationToken cancellationToken = default, string? expectedShortId = null);
}

/// <summary>
/// An in-memory cache of trust records backed by <see cref="ITrustStorePersistence"/>. Loaded once
/// at construction via <see cref="CreateAsync"/>; every subsequent mutation writes the complete set
/// through to persistence before returning, so trust survives a host restart. Every mutation
/// constructs its proposed record set, persists it, and only then publishes it into the in-memory
/// index and advances <see cref="SecurityFenceGeneration"/> -- so a concurrent reader can never
/// observe a mutation's outcome before it is durable, and a failed or cancelled write leaves the
/// live index and fence exactly as they were, with nothing to roll back because nothing was ever
/// published ahead of persistence.
/// </summary>
public sealed class TrustStore : ITrustStore
{
    /// <summary>The persistence adapter every mutation writes the complete record set through to.</summary>
    private readonly ITrustStorePersistence persistence;

    /// <summary>Serializes the full read-mutate-persist sequence of <see cref="UpsertAsync"/> calls so persisted writes never land out of mutation order.</summary>
    private readonly SemaphoreSlim mutationSemaphore = new(1, 1);

    /// <summary>Guards <see cref="recordsByClientId"/> against concurrent reads during a mutation.</summary>
    private readonly object recordsLock = new();

    /// <summary>
    /// The linearization point every administrative-mutation publish (<see cref="RevokeAsync"/>,
    /// <see cref="BlockAsync"/>, <see cref="ResetTrustAsync"/>, <see cref="ClearAsync"/>) shares with
    /// <see cref="Sessions.ISessionRegistry"/>'s own active-session check, so a client's trust record
    /// changing and the sessions that change affects becoming unauthorized are always one indivisible
    /// event to every other caller of either type -- see each of those methods' own
    /// <c>onPublished</c> parameter. Deliberately a separate, outer lock from <see cref="recordsLock"/>:
    /// an ordinary read (<see cref="TryGet"/>, <see cref="GetSecuritySnapshot"/>, <see cref="List"/>,
    /// <see cref="SecurityFenceGeneration"/>) or a pairing-commit publish
    /// (<see cref="TryUpsertIfGenerationAsync"/>, <see cref="UpsertAsync"/>) never needs to be ordered
    /// against a session's authorization the way an administrative mutation's own removal of trust
    /// does, so those paths use <see cref="recordsLock"/> alone and never acquire this gate --
    /// avoiding needless contention and, together with <see cref="Pairing.PairingCoordinator"/>'s own
    /// locks never being acquired while this gate is held, keeping lock acquisition consistently
    /// ordered gate-then-<see cref="recordsLock"/> everywhere it participates at all.
    /// </summary>
    private readonly ISecurityStateGate securityStateGate;

    /// <summary>The in-memory index of every known trust record, keyed by client.</summary>
    private readonly Dictionary<ClientId, TrustRecord> recordsByClientId;

    /// <summary>The security fence generation used to invalidate pending pairing operations. See <see cref="ITrustStore.SecurityFenceGeneration"/>.</summary>
    private long securityFenceGeneration;

    /// <summary>The time source used to record when a device becomes blocked.</summary>
    private readonly IClock clock;

    /// <summary>Creates a trust store pre-populated with already-loaded records.</summary>
    /// <param name="persistence">The persistence adapter to write through to.</param>
    /// <param name="clock">The time source used for trust-state timestamps.</param>
    /// <param name="securityStateGate">The linearization point shared with <see cref="Sessions.ISessionRegistry"/>.</param>
    /// <param name="initialRecords">The records loaded from persistence to start from.</param>
    private TrustStore(ITrustStorePersistence persistence, IClock clock, ISecurityStateGate securityStateGate, IReadOnlyList<TrustRecord> initialRecords)
    {
        this.persistence = persistence;
        this.clock = clock;
        this.securityStateGate = securityStateGate;
        recordsByClientId = initialRecords.ToDictionary(record => record.ClientId);
    }

    /// <summary>Creates a trust store, loading its initial contents from <paramref name="persistence"/>.</summary>
    /// <param name="persistence">The persistence adapter to load from and write through to.</param>
    /// <param name="clock">The time source used for trust-state timestamps.</param>
    /// <param name="securityStateGate">The linearization point shared with <see cref="Sessions.ISessionRegistry"/>.</param>
    /// <param name="cancellationToken">The token used to cancel the initial load.</param>
    public static async Task<TrustStore> CreateAsync(
        ITrustStorePersistence persistence,
        IClock clock,
        ISecurityStateGate securityStateGate,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TrustRecord> initialRecords = await persistence.LoadAsync(cancellationToken);
        return new TrustStore(persistence, clock, securityStateGate, initialRecords);
    }

    /// <inheritdoc/>
    public TrustRecord? TryGet(ClientId clientId)
    {
        lock (recordsLock)
        {
            return recordsByClientId.GetValueOrDefault(clientId);
        }
    }

    /// <inheritdoc/>
    public TrustSecuritySnapshot GetSecuritySnapshot(ClientId clientId)
    {
        lock (recordsLock)
        {
            return new TrustSecuritySnapshot(recordsByClientId.GetValueOrDefault(clientId), securityFenceGeneration);
        }
    }

    /// <inheritdoc/>
    public TrustRecord? TryGetByShortId(string shortId)
    {
        lock (recordsLock)
        {
            return recordsByClientId.Values.FirstOrDefault(record => record.ShortId == shortId);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrustRecord> List()
    {
        lock (recordsLock)
        {
            return recordsByClientId.Values.ToList();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            List<TrustRecord> proposedSnapshot;
            lock (recordsLock)
            {
                proposedSnapshot = recordsByClientId.Values
                    .Where(existing => existing.ClientId != record.ClientId)
                    .Append(record)
                    .ToList();
            }

            await persistence.SaveAsync(proposedSnapshot, cancellationToken);

            lock (recordsLock)
            {
                recordsByClientId[record.ClientId] = record;
                securityFenceGeneration++;
            }
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ClearAsync(CancellationToken cancellationToken = default, Action? onPublished = null)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            await persistence.SaveAsync([], cancellationToken);

            securityStateGate.Enter();
            try
            {
                lock (recordsLock)
                {
                    recordsByClientId.Clear();
                    securityFenceGeneration++;
                }

                onPublished?.Invoke();
            }
            finally
            {
                securityStateGate.Exit();
            }
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public long SecurityFenceGeneration
    {
        get
        {
            lock (recordsLock)
            {
                return securityFenceGeneration;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Builds the proposed snapshot without touching the live record set, persists it, and only
    /// then publishes it into <see cref="recordsByClientId"/> and advances the fence -- so a failed
    /// or cancelled write leaves the live records and generation exactly as they were, with nothing
    /// to roll back because nothing was ever mutated ahead of persistence. Refuses the upsert
    /// whenever the client's current record is <see cref="KnownDeviceState.Blocked"/>, even if the
    /// generation happens to match, as defense-in-depth against a currently-blocked record ever
    /// being replaced with a trusted one: the security fence already makes this unreachable through
    /// <see cref="Pairing.PairingCoordinator"/> today, since Block always advances the generation, but
    /// this check does not depend on that caller's own policy holding.
    /// </remarks>
    public async Task<bool> TryUpsertIfGenerationAsync(
        TrustRecord record,
        long expectedGeneration,
        CancellationToken cancellationToken = default)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            List<TrustRecord> proposedSnapshot;
            lock (recordsLock)
            {
                if (securityFenceGeneration != expectedGeneration)
                {
                    return false;
                }

                if (recordsByClientId.GetValueOrDefault(record.ClientId)?.State == KnownDeviceState.Blocked)
                {
                    return false;
                }

                proposedSnapshot = recordsByClientId.Values
                    .Where(existing => existing.ClientId != record.ClientId)
                    .Append(record)
                    .ToList();
            }

            await persistence.SaveAsync(proposedSnapshot, cancellationToken);

            lock (recordsLock)
            {
                recordsByClientId[record.ClientId] = record;
                securityFenceGeneration++;
            }
            return true;
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> RevokeAsync(
        ClientId clientId, CancellationToken cancellationToken = default, string? expectedShortId = null, Action? onPublished = null) =>
        MutateAsync(clientId, record => record.State == KnownDeviceState.Trusted
            ? record with { State = KnownDeviceState.Revoked, CredentialVerifier = string.Empty, BlockedAtUtc = null }
            : null,
            TrustMutationOutcome.NotEligible,
            cancellationToken,
            expectedShortId: expectedShortId,
            onPublished: onPublished);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> BlockAsync(
        ClientId clientId, CancellationToken cancellationToken = default, string? expectedShortId = null, Action? onPublished = null) =>
        MutateAsync(clientId, record => record.State switch
        {
            KnownDeviceState.Blocked => record,
            KnownDeviceState.Trusted or KnownDeviceState.Revoked => record with
            {
                State = KnownDeviceState.Blocked,
                CredentialVerifier = string.Empty,
                BlockedAtUtc = clock.UtcNow,
            },
            _ => null,
        },
            TrustMutationOutcome.AlreadyInState,
            cancellationToken,
            notEligibleOutcome: TrustMutationOutcome.NotEligible,
            expectedShortId: expectedShortId,
            onPublished: onPublished);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default, string? expectedShortId = null) =>
        MutateAsync(clientId, record => record.State == KnownDeviceState.Blocked
            ? record with { State = KnownDeviceState.Unpaired, BlockedAtUtc = null }
            : null,
            TrustMutationOutcome.AlreadyInState,
            cancellationToken,
            expectedShortId: expectedShortId);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default, string? expectedShortId = null) =>
        MutateAsync(clientId, record => record.State is KnownDeviceState.Revoked or KnownDeviceState.Unpaired
            ? null
            : record,
            TrustMutationOutcome.NotEligible,
            cancellationToken,
            removeWhenNull: true,
            expectedShortId: expectedShortId);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClientId>> ResetTrustAsync(
        CancellationToken cancellationToken = default, Action<IReadOnlyList<ClientId>>? onPublished = null)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            List<ClientId> affected;
            List<TrustRecord> proposedSnapshot;
            Dictionary<ClientId, TrustRecord> revokedByClientId;
            lock (recordsLock)
            {
                List<TrustRecord> current = recordsByClientId.Values.ToList();
                affected = current
                    .Where(record => record.State == KnownDeviceState.Trusted)
                    .Select(record => record.ClientId)
                    .ToList();
                revokedByClientId = affected.ToDictionary(
                    clientId => clientId,
                    clientId => recordsByClientId[clientId] with
                    {
                        State = KnownDeviceState.Revoked,
                        CredentialVerifier = string.Empty,
                        BlockedAtUtc = null,
                    });
                proposedSnapshot = affected.Count == 0
                    ? current
                    : current.Select(record => revokedByClientId.GetValueOrDefault(record.ClientId, record)).ToList();
            }

            if (affected.Count > 0)
            {
                await persistence.SaveAsync(proposedSnapshot, cancellationToken);
            }
            // else: no currently trusted record to revoke, so a persistence write here would only
            // re-save an unchanged record set. Reset Trust must still act as a security fence below,
            // advancing the generation and running onPublished, even though nothing was written.

            securityStateGate.Enter();
            try
            {
                lock (recordsLock)
                {
                    foreach ((ClientId clientId, TrustRecord revoked) in revokedByClientId)
                    {
                        recordsByClientId[clientId] = revoked;
                    }
                    securityFenceGeneration++;
                }

                onPublished?.Invoke(affected);
            }
            finally
            {
                securityStateGate.Exit();
            }

            return affected;
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> RenameIfTrustedAsync(
        ClientId clientId, string displayName, CancellationToken cancellationToken = default, string? expectedShortId = null) =>
        MutateAsync(clientId, record => record.State == KnownDeviceState.Trusted
            ? record with { DisplayName = displayName }
            : null,
            TrustMutationOutcome.NotEligible,
            cancellationToken,
            expectedShortId: expectedShortId);

    /// <summary>
    /// Applies one persisted trust mutation, publishing it only once persistence succeeds.
    /// </summary>
    /// <param name="clientId">The known device to mutate.</param>
    /// <param name="mutation">
    /// Produces the replacement record, the exact same reference it was called with when the target
    /// state already holds and nothing needs to change, or <see langword="null"/> when that record
    /// is not eligible for this mutation at all.
    /// </param>
    /// <param name="ineligibleOutcome">
    /// Reported when <paramref name="mutation"/> returns the same reference back, and as the default
    /// for a <see langword="null"/> result when <paramref name="notEligibleOutcome"/> is not supplied.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the persistence write.</param>
    /// <param name="removeWhenNull">
    /// When set, a <see langword="null"/> result deletes the record instead of replacing it, and a
    /// non-null result reports <paramref name="ineligibleOutcome"/> instead of applying it.
    /// </param>
    /// <param name="notEligibleOutcome">
    /// Reported when <paramref name="mutation"/> returns <see langword="null"/>, if distinct from
    /// <paramref name="ineligibleOutcome"/> -- for example a mutation whose eligible states can be
    /// already in the target state (reported via <paramref name="ineligibleOutcome"/>) as well as
    /// genuinely ineligible for it altogether (reported via this parameter instead).
    /// </param>
    /// <param name="expectedShortId">
    /// When supplied, resolved against <paramref name="clientId"/>'s current record inside this exact
    /// serialized mutation -- see the public shortId-precondition parameters' own remarks (for example
    /// <see cref="ITrustStore.RevokeAsync"/>'s) for the incarnation race this closes. A mismatch
    /// reports <see cref="TrustMutationOutcome.NotFound"/> without mutation.
    /// </param>
    /// <param name="onPublished">See <see cref="ITrustStore.RevokeAsync"/>'s own remarks for this callback's contract.</param>
    private async Task<TrustMutationOutcome> MutateAsync(
        ClientId clientId,
        Func<TrustRecord, TrustRecord?> mutation,
        TrustMutationOutcome ineligibleOutcome,
        CancellationToken cancellationToken,
        bool removeWhenNull = false,
        TrustMutationOutcome? notEligibleOutcome = null,
        string? expectedShortId = null,
        Action? onPublished = null)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            TrustRecord? mutatedRecord;
            List<TrustRecord> proposedSnapshot;
            lock (recordsLock)
            {
                if (!recordsByClientId.TryGetValue(clientId, out TrustRecord? previousRecord))
                {
                    return TrustMutationOutcome.NotFound;
                }

                if (expectedShortId is not null && previousRecord.ShortId != expectedShortId)
                {
                    // The current record for this clientId is a different Known Device incarnation
                    // than the one shortId-based administration originally resolved (for example the
                    // prior incarnation was forgotten and this clientId later re-paired under a new
                    // shortId): treat it the same as not found rather than silently mutating the
                    // replacement incarnation the caller never selected.
                    return TrustMutationOutcome.NotFound;
                }

                TrustRecord? mutated = mutation(previousRecord);
                if (!removeWhenNull && ReferenceEquals(mutated, previousRecord))
                {
                    return ineligibleOutcome;
                }
                if (!removeWhenNull && mutated is null)
                {
                    return notEligibleOutcome ?? ineligibleOutcome;
                }
                if (removeWhenNull && mutated is not null)
                {
                    return ineligibleOutcome;
                }

                mutatedRecord = mutated;
                proposedSnapshot = removeWhenNull
                    ? recordsByClientId.Values.Where(existing => existing.ClientId != clientId).ToList()
                    : recordsByClientId.Values.Where(existing => existing.ClientId != clientId).Append(mutated!).ToList();
            }

            await persistence.SaveAsync(proposedSnapshot, cancellationToken);

            securityStateGate.Enter();
            try
            {
                lock (recordsLock)
                {
                    if (removeWhenNull)
                    {
                        recordsByClientId.Remove(clientId);
                    }
                    else
                    {
                        recordsByClientId[clientId] = mutatedRecord!;
                    }
                    securityFenceGeneration++;
                }

                onPublished?.Invoke();
            }
            finally
            {
                securityStateGate.Exit();
            }

            return TrustMutationOutcome.Changed;
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }
}
