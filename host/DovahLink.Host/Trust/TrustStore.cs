using DovahLink.Host.Identity;
using DovahLink.Host.Time;

namespace DovahLink.Host.Trust;

/// <summary>The host's in-memory, persistence-backed index of every known device's trust record.</summary>
public interface ITrustStore
{
    /// <summary>Looks up the trust record for a client, if one exists.</summary>
    /// <param name="clientId">The client to look up.</param>
    /// <returns>The client's trust record, or <see langword="null"/> if the client is not known.</returns>
    TrustRecord? TryGet(ClientId clientId);

    /// <summary>Lists every currently known trust record.</summary>
    IReadOnlyList<TrustRecord> List();

    /// <summary>Inserts a new trust record or replaces an existing one for the same client, then persists the change.</summary>
    /// <param name="record">The record to store.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    Task UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default);

    /// <summary>Deletes every trust record and persists the empty store as one mutation.</summary>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The monotonically advancing security fence used to invalidate an in-flight pending pairing
    /// operation against a concurrent administrative trust mutation. It advances on every mutation
    /// this store applies, including one that changed zero records (for example a Reset Trust with no
    /// currently trusted devices): the fence's purpose is to guarantee that no pairing operation which
    /// began before an administrative mutation can still complete after it, not to literally count
    /// records changed.
    /// </summary>
    long SecurityFenceGeneration { get; }

    /// <summary>Upserts a record only when the store still has the supplied fence generation.</summary>
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

    /// <summary>Revokes a trusted device and destroys its credential verifier.</summary>
    Task<TrustMutationOutcome> RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Blocks any existing non-blocked known device and destroys its credential verifier.</summary>
    Task<TrustMutationOutcome> BlockAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Unblocks a blocked device and returns it to the unpaired state.</summary>
    Task<TrustMutationOutcome> UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Forgets an eligible revoked or unpaired device completely.</summary>
    Task<TrustMutationOutcome> ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default);

    /// <summary>Applies Reset Trust to every trusted device and returns affected identities.</summary>
    Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// An in-memory cache of trust records backed by <see cref="ITrustStorePersistence"/>. Loaded once
/// at construction via <see cref="CreateAsync"/>; every subsequent mutation writes the complete set
/// through to persistence before returning, so trust survives a host restart.
/// </summary>
public sealed class TrustStore : ITrustStore
{
    /// <summary>The persistence adapter every mutation writes the complete record set through to.</summary>
    private readonly ITrustStorePersistence persistence;

    /// <summary>Serializes the full read-mutate-persist sequence of <see cref="UpsertAsync"/> calls so persisted writes never land out of mutation order.</summary>
    private readonly SemaphoreSlim mutationSemaphore = new(1, 1);

    /// <summary>Guards <see cref="recordsByClientId"/> against concurrent reads during a mutation.</summary>
    private readonly object recordsLock = new();

    /// <summary>The in-memory index of every known trust record, keyed by client.</summary>
    private readonly Dictionary<ClientId, TrustRecord> recordsByClientId;

    /// <summary>The security fence generation used to invalidate pending pairing operations. See <see cref="ITrustStore.SecurityFenceGeneration"/>.</summary>
    private long securityFenceGeneration;

    /// <summary>The time source used to record when a device becomes blocked.</summary>
    private readonly IClock clock;

    /// <summary>Creates a trust store pre-populated with already-loaded records.</summary>
    /// <param name="persistence">The persistence adapter to write through to.</param>
    /// <param name="clock">The time source used for trust-state timestamps.</param>
    /// <param name="initialRecords">The records loaded from persistence to start from.</param>
    private TrustStore(ITrustStorePersistence persistence, IClock clock, IReadOnlyList<TrustRecord> initialRecords)
    {
        this.persistence = persistence;
        this.clock = clock;
        recordsByClientId = initialRecords.ToDictionary(record => record.ClientId);
    }

    /// <summary>Creates a trust store, loading its initial contents from <paramref name="persistence"/>.</summary>
    /// <param name="persistence">The persistence adapter to load from and write through to.</param>
    /// <param name="clock">The time source used for trust-state timestamps.</param>
    /// <param name="cancellationToken">The token used to cancel the initial load.</param>
    public static async Task<TrustStore> CreateAsync(
        ITrustStorePersistence persistence,
        IClock clock,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TrustRecord> initialRecords = await persistence.LoadAsync(cancellationToken);
        return new TrustStore(persistence, clock, initialRecords);
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
    /// <remarks>
    /// If the persistence write fails, the in-memory mutation is rolled back before the exception
    /// propagates, so a failed upsert never leaves the store reporting a value it did not actually
    /// persist.
    /// </remarks>
    public async Task UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            TrustRecord? previousRecord;
            List<TrustRecord> snapshot;
            lock (recordsLock)
            {
                recordsByClientId.TryGetValue(record.ClientId, out previousRecord);
                recordsByClientId[record.ClientId] = record;
                snapshot = recordsByClientId.Values.ToList();
            }

            try
            {
                await persistence.SaveAsync(snapshot, cancellationToken);
                lock (recordsLock)
                {
                    securityFenceGeneration++;
                }
            }
            catch
            {
                lock (recordsLock)
                {
                    if (previousRecord is null)
                    {
                        recordsByClientId.Remove(record.ClientId);
                    }
                    else
                    {
                        recordsByClientId[record.ClientId] = previousRecord;
                    }
                }

                throw;
            }
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// If persistence fails, the complete previous record set is restored in memory before the
    /// exception propagates, so a failed clear never leaves the store reporting a deletion that
    /// was not persisted.
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            List<TrustRecord> previousRecords;
            lock (recordsLock)
            {
                previousRecords = recordsByClientId.Values.ToList();
                recordsByClientId.Clear();
            }

            try
            {
                await persistence.SaveAsync([], cancellationToken);
                lock (recordsLock)
                {
                    securityFenceGeneration++;
                }
            }
            catch
            {
                lock (recordsLock)
                {
                    recordsByClientId.Clear();
                    foreach (TrustRecord record in previousRecords)
                    {
                        recordsByClientId[record.ClientId] = record;
                    }
                }

                throw;
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
    public async Task<bool> TryUpsertIfGenerationAsync(
        TrustRecord record,
        long expectedGeneration,
        CancellationToken cancellationToken = default)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            TrustRecord? previousRecord;
            List<TrustRecord> snapshot;
            lock (recordsLock)
            {
                if (securityFenceGeneration != expectedGeneration)
                {
                    return false;
                }

                recordsByClientId.TryGetValue(record.ClientId, out previousRecord);
                recordsByClientId[record.ClientId] = record;
                snapshot = recordsByClientId.Values.ToList();
            }

            try
            {
                await persistence.SaveAsync(snapshot, cancellationToken);
                lock (recordsLock)
                {
                    securityFenceGeneration++;
                }
                return true;
            }
            catch
            {
                lock (recordsLock)
                {
                    if (previousRecord is null)
                    {
                        recordsByClientId.Remove(record.ClientId);
                    }
                    else
                    {
                        recordsByClientId[record.ClientId] = previousRecord;
                    }
                }

                throw;
            }
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default) =>
        MutateAsync(clientId, record => record.State == KnownDeviceState.Trusted
            ? record with { State = KnownDeviceState.Revoked, CredentialVerifier = string.Empty, BlockedAtUtc = null }
            : null,
            TrustMutationOutcome.NotEligible,
            cancellationToken);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> BlockAsync(ClientId clientId, CancellationToken cancellationToken = default) =>
        MutateAsync(clientId, record => record.State == KnownDeviceState.Blocked
            ? record
            : record with
            {
                State = KnownDeviceState.Blocked,
                CredentialVerifier = string.Empty,
                BlockedAtUtc = clock.UtcNow,
            },
            TrustMutationOutcome.AlreadyInState,
            cancellationToken);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default) =>
        MutateAsync(clientId, record => record.State == KnownDeviceState.Blocked
            ? record with { State = KnownDeviceState.Unpaired, BlockedAtUtc = null }
            : null,
            TrustMutationOutcome.AlreadyInState,
            cancellationToken);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default) =>
        MutateAsync(clientId, record => record.State is KnownDeviceState.Revoked or KnownDeviceState.Unpaired
            ? null
            : record,
            TrustMutationOutcome.NotEligible,
            cancellationToken,
            removeWhenNull: true);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            List<TrustRecord> previousRecords;
            List<ClientId> affected;
            lock (recordsLock)
            {
                previousRecords = recordsByClientId.Values.ToList();
                affected = previousRecords
                    .Where(record => record.State == KnownDeviceState.Trusted)
                    .Select(record => record.ClientId)
                    .ToList();
                if (affected.Count == 0)
                {
                    // No currently trusted record to revoke, but Reset Trust must still act as a
                    // security fence: advancing it here still invalidates any pending pairing
                    // operation that began before this call, even though there is nothing to write
                    // to persistence -- pending pairing state cannot survive a host restart anyway,
                    // so a persistence write here would only re-save an unchanged record set.
                    securityFenceGeneration++;
                    return affected;
                }

                foreach (ClientId clientId in affected)
                {
                    TrustRecord record = recordsByClientId[clientId];
                    recordsByClientId[clientId] = record with
                    {
                        State = KnownDeviceState.Revoked,
                        CredentialVerifier = string.Empty,
                        BlockedAtUtc = null,
                    };
                }
            }

            try
            {
                await persistence.SaveAsync(List(), cancellationToken);
                lock (recordsLock)
                {
                    securityFenceGeneration++;
                }
                return affected;
            }
            catch
            {
                lock (recordsLock)
                {
                    recordsByClientId.Clear();
                    foreach (TrustRecord record in previousRecords)
                    {
                        recordsByClientId[record.ClientId] = record;
                    }
                }

                throw;
            }
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }

    /// <summary>Applies one persisted trust mutation and restores the prior record on failure.</summary>
    private async Task<TrustMutationOutcome> MutateAsync(
        ClientId clientId,
        Func<TrustRecord, TrustRecord?> mutation,
        TrustMutationOutcome ineligibleOutcome,
        CancellationToken cancellationToken,
        bool removeWhenNull = false)
    {
        await mutationSemaphore.WaitAsync(cancellationToken);
        try
        {
            TrustRecord? previousRecord;
            List<TrustRecord> snapshot;
            TrustMutationOutcome outcome;
            lock (recordsLock)
            {
                if (!recordsByClientId.TryGetValue(clientId, out previousRecord))
                {
                    return TrustMutationOutcome.NotFound;
                }

                TrustRecord? mutated = mutation(previousRecord);
                if (!removeWhenNull && ReferenceEquals(mutated, previousRecord))
                {
                    return ineligibleOutcome;
                }
                if (!removeWhenNull && mutated is null)
                {
                    return ineligibleOutcome;
                }
                if (removeWhenNull && mutated is not null)
                {
                    return ineligibleOutcome;
                }

                if (removeWhenNull)
                {
                    recordsByClientId.Remove(clientId);
                }
                else
                {
                    recordsByClientId[clientId] = mutated!;
                }
                snapshot = recordsByClientId.Values.ToList();
                outcome = TrustMutationOutcome.Changed;
            }

            try
            {
                await persistence.SaveAsync(snapshot, cancellationToken);
                lock (recordsLock)
                {
                    securityFenceGeneration++;
                }
                return outcome;
            }
            catch
            {
                lock (recordsLock)
                {
                    if (removeWhenNull)
                    {
                        recordsByClientId[clientId] = previousRecord;
                    }
                    else
                    {
                        recordsByClientId[clientId] = previousRecord;
                    }
                }

                throw;
            }
        }
        finally
        {
            mutationSemaphore.Release();
        }
    }
}
