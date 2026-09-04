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

    /// <summary>When set, <see cref="TryGet"/> throws this instead of looking up the record.</summary>
    public Exception? ThrowOnTryGet { get; set; }

    /// <summary>The number of times <see cref="ClearAsync"/> has succeeded.</summary>
    public int ClearCallCount { get; private set; }

    /// <summary>When set, <see cref="ClearAsync"/> throws this instead of clearing the records.</summary>
    public Exception? ThrowOnClear { get; set; }

    /// <summary>The security fence generation exposed to pending-pairing tests. See <see cref="ITrustStore.SecurityFenceGeneration"/>.</summary>
    public long SecurityFenceGeneration { get; private set; }

    /// <summary>Optional asynchronous work used to hold a clear in flight during concurrency tests.</summary>
    public Func<Task>? BeforeClear { get; set; }

    /// <summary>Optional asynchronous work used to hold a conditional upsert in flight during concurrency tests.</summary>
    public Func<Task>? BeforeConditionalUpsert { get; set; }

    /// <summary>
    /// Optional hook invoked with a short label immediately after a mutation successfully applies
    /// (<c>"Revoke"</c>, <c>"Block"</c>, <c>"Unblock"</c>, <c>"Forget"</c>, <c>"ResetTrust"</c>, or
    /// <c>"Clear"</c>), letting a test build a cross-collaborator call-order timeline together with
    /// <see cref="FakePairingCoordinator.OnMutationApplied"/> and
    /// <see cref="FakeSessionRegistry.OnMutationApplied"/>.
    /// </summary>
    public Action<string>? OnMutationApplied { get; set; }

    /// <summary>Seeds the fake with a record as if it had already been upserted.</summary>
    /// <param name="record">The record to seed.</param>
    public void Seed(TrustRecord record) => recordsByClientId[record.ClientId] = record;

    /// <inheritdoc/>
    public TrustRecord? TryGet(ClientId clientId) =>
        ThrowOnTryGet is { } exception ? throw exception : recordsByClientId.GetValueOrDefault(clientId);

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
        SecurityFenceGeneration++;
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
        SecurityFenceGeneration++;
        OnMutationApplied?.Invoke("Clear");
    }

    /// <inheritdoc/>
    public Task<bool> TryUpsertIfGenerationAsync(
        TrustRecord record,
        long expectedGeneration,
        CancellationToken cancellationToken = default)
    {
        if (SecurityFenceGeneration != expectedGeneration)
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
        SecurityFenceGeneration++;
        return Task.FromResult(true);
    }

    /// <summary>Completes a conditional upsert after its test-controlled wait finishes.</summary>
    private async Task<bool> ConditionalUpsertAfterWaitAsync(
        TrustRecord record, Func<Task> beforeConditionalUpsert)
    {
        await beforeConditionalUpsert();
        recordsByClientId[record.ClientId] = record;
        UpsertCallCount++;
        SecurityFenceGeneration++;
        return true;
    }

    /// <inheritdoc/>
    public TrustRecord? TryGetByShortId(string shortId) =>
        recordsByClientId.Values.FirstOrDefault(record => record.ShortId == shortId);

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpsert is { } exception)
        {
            return Task.FromException<TrustMutationOutcome>(exception);
        }
        if (!recordsByClientId.TryGetValue(clientId, out TrustRecord? record))
        {
            return Task.FromResult(TrustMutationOutcome.NotFound);
        }
        if (record.State != KnownDeviceState.Trusted)
        {
            return Task.FromResult(TrustMutationOutcome.NotEligible);
        }

        recordsByClientId[clientId] = record with
        {
            State = KnownDeviceState.Revoked,
            CredentialVerifier = string.Empty,
            BlockedAtUtc = null,
        };
        SecurityFenceGeneration++;
        OnMutationApplied?.Invoke("Revoke");
        return Task.FromResult(TrustMutationOutcome.Changed);
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> BlockAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpsert is { } exception)
        {
            return Task.FromException<TrustMutationOutcome>(exception);
        }
        if (!recordsByClientId.TryGetValue(clientId, out TrustRecord? record))
        {
            return Task.FromResult(TrustMutationOutcome.NotFound);
        }
        if (record.State == KnownDeviceState.Blocked)
        {
            return Task.FromResult(TrustMutationOutcome.AlreadyInState);
        }

        recordsByClientId[clientId] = record with
        {
            State = KnownDeviceState.Blocked,
            CredentialVerifier = string.Empty,
            BlockedAtUtc = DateTimeOffset.UtcNow,
        };
        SecurityFenceGeneration++;
        OnMutationApplied?.Invoke("Block");
        return Task.FromResult(TrustMutationOutcome.Changed);
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpsert is { } exception)
        {
            return Task.FromException<TrustMutationOutcome>(exception);
        }
        if (!recordsByClientId.TryGetValue(clientId, out TrustRecord? record))
        {
            return Task.FromResult(TrustMutationOutcome.NotFound);
        }
        if (record.State != KnownDeviceState.Blocked)
        {
            return Task.FromResult(TrustMutationOutcome.AlreadyInState);
        }

        recordsByClientId[clientId] = record with
        {
            State = KnownDeviceState.Unpaired,
            BlockedAtUtc = null,
        };
        SecurityFenceGeneration++;
        OnMutationApplied?.Invoke("Unblock");
        return Task.FromResult(TrustMutationOutcome.Changed);
    }

    /// <inheritdoc/>
    public Task<TrustMutationOutcome> ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpsert is { } exception)
        {
            return Task.FromException<TrustMutationOutcome>(exception);
        }
        if (!recordsByClientId.TryGetValue(clientId, out TrustRecord? record))
        {
            return Task.FromResult(TrustMutationOutcome.NotFound);
        }
        if (record.State is not (KnownDeviceState.Revoked or KnownDeviceState.Unpaired))
        {
            return Task.FromResult(TrustMutationOutcome.NotEligible);
        }

        recordsByClientId.Remove(clientId);
        SecurityFenceGeneration++;
        OnMutationApplied?.Invoke("Forget");
        return Task.FromResult(TrustMutationOutcome.Changed);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpsert is { } exception)
        {
            return Task.FromException<IReadOnlyList<ClientId>>(exception);
        }
        List<ClientId> affected = recordsByClientId.Values
            .Where(record => record.State == KnownDeviceState.Trusted)
            .Select(record => record.ClientId)
            .ToList();
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
        // Mirrors the real store: the fence advances even when nothing was affected, so a Reset
        // Trust with zero currently trusted records still invalidates an in-flight pending pairing.
        SecurityFenceGeneration++;
        OnMutationApplied?.Invoke("ResetTrust");
        return Task.FromResult<IReadOnlyList<ClientId>>(affected);
    }
}
