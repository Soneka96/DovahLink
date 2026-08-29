using DovahLink.Host.Identity;

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

    /// <summary>The number of successful trust mutations applied by this store instance.</summary>
    long MutationGeneration { get; }

    /// <summary>Upserts a record only when the store still has the supplied generation.</summary>
    /// <param name="record">The record to store.</param>
    /// <param name="expectedGeneration">The generation observed before a pending operation began.</param>
    /// <param name="cancellationToken">The token used to cancel the persistence write.</param>
    /// <returns><see langword="true"/> when the record was persisted; otherwise the generation changed first.</returns>
    Task<bool> TryUpsertIfGenerationAsync(
        TrustRecord record,
        long expectedGeneration,
        CancellationToken cancellationToken = default);
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

    /// <summary>The successful mutation count used to fence pending pairing operations.</summary>
    private long mutationGeneration;

    /// <summary>Creates a trust store pre-populated with already-loaded records.</summary>
    /// <param name="persistence">The persistence adapter to write through to.</param>
    /// <param name="initialRecords">The records loaded from persistence to start from.</param>
    private TrustStore(ITrustStorePersistence persistence, IReadOnlyList<TrustRecord> initialRecords)
    {
        this.persistence = persistence;
        recordsByClientId = initialRecords.ToDictionary(record => record.ClientId);
    }

    /// <summary>Creates a trust store, loading its initial contents from <paramref name="persistence"/>.</summary>
    /// <param name="persistence">The persistence adapter to load from and write through to.</param>
    /// <param name="cancellationToken">The token used to cancel the initial load.</param>
    public static async Task<TrustStore> CreateAsync(ITrustStorePersistence persistence, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TrustRecord> initialRecords = await persistence.LoadAsync(cancellationToken);
        return new TrustStore(persistence, initialRecords);
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
                    mutationGeneration++;
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
                    mutationGeneration++;
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
    public long MutationGeneration
    {
        get
        {
            lock (recordsLock)
            {
                return mutationGeneration;
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
                if (mutationGeneration != expectedGeneration)
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
                    mutationGeneration++;
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
}
