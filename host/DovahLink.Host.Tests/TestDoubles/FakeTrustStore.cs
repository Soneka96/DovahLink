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

    /// <summary>The number of times <see cref="ClearAsync"/> has succeeded.</summary>
    public int ClearCallCount { get; private set; }

    /// <summary>When set, <see cref="ClearAsync"/> throws this instead of clearing the records.</summary>
    public Exception? ThrowOnClear { get; set; }

    /// <summary>The successful mutation count exposed to pending-pairing tests.</summary>
    public long MutationGeneration { get; private set; }

    /// <summary>Optional asynchronous work used to hold a clear in flight during concurrency tests.</summary>
    public Func<Task>? BeforeClear { get; set; }

    /// <summary>Optional asynchronous work used to hold a conditional upsert in flight during concurrency tests.</summary>
    public Func<Task>? BeforeConditionalUpsert { get; set; }

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
        MutationGeneration++;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnClear is { } exception)
        {
            throw exception;
        }

        if (BeforeClear is { } beforeClear)
        {
            await beforeClear();
        }

        recordsByClientId.Clear();
        ClearCallCount++;
        MutationGeneration++;
    }

    /// <inheritdoc/>
    public Task<bool> TryUpsertIfGenerationAsync(
        TrustRecord record,
        long expectedGeneration,
        CancellationToken cancellationToken = default)
    {
        if (MutationGeneration != expectedGeneration)
        {
            return Task.FromResult(false);
        }

        if (ThrowOnUpsert is { } exception)
        {
            return Task.FromException<bool>(exception);
        }

        if (BeforeConditionalUpsert is { } beforeConditionalUpsert)
        {
            return ConditionalUpsertAfterWaitAsync(record, beforeConditionalUpsert);
        }

        recordsByClientId[record.ClientId] = record;
        UpsertCallCount++;
        MutationGeneration++;
        return Task.FromResult(true);
    }

    /// <summary>Completes a conditional upsert after its test-controlled wait finishes.</summary>
    private async Task<bool> ConditionalUpsertAfterWaitAsync(
        TrustRecord record, Func<Task> beforeConditionalUpsert)
    {
        await beforeConditionalUpsert();
        recordsByClientId[record.ClientId] = record;
        UpsertCallCount++;
        MutationGeneration++;
        return true;
    }
}
