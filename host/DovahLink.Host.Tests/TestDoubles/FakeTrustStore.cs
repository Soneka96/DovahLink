using DovahLink.Host.Identity;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>An in-memory stand-in for <see cref="ITrustStore"/> that consumers can seed and fail on demand.</summary>
public sealed class FakeTrustStore : ITrustStore
{
    /// <summary>The records currently held by the fake.</summary>
    private readonly Dictionary<ClientId, TrustRecord> recordsByClientId = new();

    /// <summary>The number of times <see cref="UpsertAsync"/> has succeeded.</summary>
    public int UpsertCallCount { get; private set; }

    /// <summary>When set, <see cref="UpsertAsync"/> throws this instead of storing the record.</summary>
    public Exception? ThrowOnUpsert { get; set; }

    /// <summary>Seeds the fake with a record as if it had already been upserted.</summary>
    /// <param name="record">The record to seed.</param>
    public void Seed(TrustRecord record) => recordsByClientId[record.ClientId] = record;

    /// <inheritdoc/>
    public TrustRecord? TryGet(ClientId clientId) => recordsByClientId.GetValueOrDefault(clientId);

    /// <inheritdoc/>
    public IReadOnlyList<TrustRecord> List() => recordsByClientId.Values.ToList();

    /// <inheritdoc/>
    public Task UpsertAsync(TrustRecord record, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpsert is { } exception)
        {
            throw exception;
        }

        recordsByClientId[record.ClientId] = record;
        UpsertCallCount++;
        return Task.CompletedTask;
    }
}
