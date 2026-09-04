using DovahLink.Host.Identity;
using DovahLink.Host.Trust;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="TrustStore"/>.</summary>
public class TrustStoreTests
{
    /// <summary>Verifies that clearing removes every record and persists an empty store.</summary>
    [Fact]
    public async Task ClearAsync_RemovesEveryRecordAndPersistsEmptySet()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord first = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        TrustRecord second = new(ClientId.NewId(), "CD34", "Bedroom Tablet", KnownDeviceState.Trusted, "beefdead", DateTimeOffset.UtcNow);
        await store.UpsertAsync(first);
        await store.UpsertAsync(second);

        await store.ClearAsync();

        Assert.Empty(store.List());
        Assert.Empty(persistence.SavedRecords);
    }

    /// <summary>Verifies that a failed clear restores the complete in-memory record set.</summary>
    [Fact]
    public async Task ClearAsync_PersistenceFails_RestoresPreviousRecords()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord first = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        TrustRecord second = new(ClientId.NewId(), "CD34", "Bedroom Tablet", KnownDeviceState.Revoked, "beefdead", DateTimeOffset.UtcNow);
        await store.UpsertAsync(first);
        await store.UpsertAsync(second);
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.ClearAsync());

        Assert.Equal(
            new[] { first, second }.OrderBy(record => record.ClientId.Value),
            store.List().OrderBy(record => record.ClientId.Value));
        Assert.Equal(
            new[] { first, second }.OrderBy(record => record.ClientId.Value),
            persistence.SavedRecords.OrderBy(record => record.ClientId.Value));
    }

    /// <summary>Verifies that a clear and an upsert serialize into a persisted snapshot matching the final store.</summary>
    [Fact]
    public async Task ClearAsync_ConcurrentUpsert_PersistsTheFinalSerializedState()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        await Task.WhenAll(store.ClearAsync(), store.UpsertAsync(record));

        Assert.Equal(
            store.List().OrderBy(saved => saved.ClientId.Value),
            persistence.SavedRecords.OrderBy(saved => saved.ClientId.Value));
    }

    /// <summary>Verifies that a matching security fence generation permits a conditional upsert.</summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_MatchingGeneration_PersistsRecord()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        bool committed = await store.TryUpsertIfGenerationAsync(record, store.SecurityFenceGeneration);

        Assert.True(committed);
        Assert.Equal(record, store.TryGet(record.ClientId));
    }

    /// <summary>Verifies that a stale security fence generation cannot recreate trust after another mutation.</summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_StaleGeneration_DoesNotMutateStore()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        long initialGeneration = store.SecurityFenceGeneration;
        TrustRecord existing = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        TrustRecord attempted = new(ClientId.NewId(), "CD34", "Bedroom Tablet", KnownDeviceState.Trusted, "beefdead", DateTimeOffset.UtcNow);
        await store.UpsertAsync(existing);

        bool committed = await store.TryUpsertIfGenerationAsync(attempted, initialGeneration);

        Assert.False(committed);
        Assert.Equal(existing, store.TryGet(existing.ClientId));
        Assert.Null(store.TryGet(attempted.ClientId));
    }

    /// <summary>Verifies that a conditional upsert rolls back on persistence failure without changing its generation.</summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_PersistenceFails_RestoresRecordAndGeneration()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        long generation = store.SecurityFenceGeneration;
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.TryUpsertIfGenerationAsync(record, generation));

        Assert.Null(store.TryGet(record.ClientId));
        Assert.Equal(generation, store.SecurityFenceGeneration);
    }

    /// <summary>Verifies that a failed conditional replacement restores the prior record and generation.</summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_ExistingRecordSaveFails_RestoresPriorRecord()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        ClientId clientId = ClientId.NewId();
        TrustRecord original = new(clientId, "12345", "Living Room PC", KnownDeviceState.Revoked, "oldhash", DateTimeOffset.UtcNow);
        TrustRecord replacement = original with { State = KnownDeviceState.Trusted, CredentialVerifier = "newhash" };
        await store.UpsertAsync(original);
        long generation = store.SecurityFenceGeneration;
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.TryUpsertIfGenerationAsync(replacement, generation));

        Assert.Equal(original, store.TryGet(clientId));
        Assert.Equal(generation, store.SecurityFenceGeneration);
    }

    /// <summary>Verifies that administration lookup returns the record matching its stable short ID.</summary>
    [Fact]
    public async Task TryGetByShortId_ReturnsMatchingRecordOnly()
    {
        TrustStore store = await CreateStoreAsync(new FakeTrustStorePersistence());
        TrustRecord record = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        await store.UpsertAsync(record);

        Assert.Equal(record, store.TryGetByShortId("12345"));
        Assert.Null(store.TryGetByShortId("54321"));
    }

    /// <summary>Verifies that revocation destroys the verifier while preserving device metadata.</summary>
    [Fact]
    public async Task RevokeAsync_TrustedRecord_ClearsVerifierAndPreservesMetadata()
    {
        TrustStore store = await CreateStoreAsync(new FakeTrustStorePersistence());
        DateTimeOffset pairedAt = DateTimeOffset.UtcNow.AddDays(-1);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", pairedAt);
        await store.UpsertAsync(original);

        TrustMutationOutcome outcome = await store.RevokeAsync(original.ClientId);
        TrustRecord revoked = store.TryGet(original.ClientId)!;

        Assert.Equal(TrustMutationOutcome.Changed, outcome);
        Assert.Equal(KnownDeviceState.Revoked, revoked.State);
        Assert.Empty(revoked.CredentialVerifier);
        Assert.Equal(original.ShortId, revoked.ShortId);
        Assert.Equal(original.DisplayName, revoked.DisplayName);
        Assert.Equal(original.PairedAtUtc, revoked.PairedAtUtc);
    }

    /// <summary>Verifies that blocking is allowed for an unpaired known device and is idempotent.</summary>
    [Fact]
    public async Task BlockAsync_UnpairedRecord_BlocksThenReportsAlreadyInState()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero) };
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence, clock);
        TrustRecord original = new(ClientId.NewId(), "12345", null, KnownDeviceState.Unpaired, string.Empty, clock.UtcNow.AddDays(-1));
        await store.UpsertAsync(original);

        Assert.Equal(TrustMutationOutcome.Changed, await store.BlockAsync(original.ClientId));
        TrustRecord blocked = store.TryGet(original.ClientId)!;
        Assert.Equal(clock.UtcNow, blocked.BlockedAtUtc);
        Assert.Equal(blocked, Assert.Single(persistence.SavedRecords));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TrustMutationOutcome.AlreadyInState, await store.BlockAsync(original.ClientId));
        Assert.Equal(KnownDeviceState.Blocked, store.TryGet(original.ClientId)!.State);
        Assert.Equal(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero), store.TryGet(original.ClientId)!.BlockedAtUtc);
    }

    /// <summary>Verifies that trusted and revoked records receive the injected block timestamp.</summary>
    [Theory]
    [InlineData(KnownDeviceState.Trusted, "hash")]
    [InlineData(KnownDeviceState.Revoked, "")]
    public async Task BlockAsync_TrustedOrRevokedRecord_UsesInjectedTimestamp(KnownDeviceState state, string verifier)
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero) };
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence, clock);
        TrustRecord original = new(ClientId.NewId(), "12345", null, state, verifier, clock.UtcNow.AddDays(-1));
        await store.UpsertAsync(original);

        Assert.Equal(TrustMutationOutcome.Changed, await store.BlockAsync(original.ClientId));

        TrustRecord blocked = store.TryGet(original.ClientId)!;
        Assert.Equal(KnownDeviceState.Blocked, blocked.State);
        Assert.Equal(clock.UtcNow, blocked.BlockedAtUtc);
        Assert.Equal(blocked, Assert.Single(persistence.SavedRecords));
    }

    /// <summary>Verifies that blocking an unknown client does not mutate persistence.</summary>
    [Fact]
    public async Task BlockAsync_UnknownClient_ReturnsNotFound()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);

        Assert.Equal(TrustMutationOutcome.NotFound, await store.BlockAsync(ClientId.NewId()));
        Assert.Empty(persistence.SavedRecords);
    }

    /// <summary>Verifies that a failed block persistence write restores the original record.</summary>
    [Fact]
    public async Task BlockAsync_PersistenceFails_RestoresOriginalRecord()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero) };
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence, clock);
        TrustRecord original = new(ClientId.NewId(), "12345", null, KnownDeviceState.Unpaired, string.Empty, clock.UtcNow.AddDays(-1));
        await store.UpsertAsync(original);
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.BlockAsync(original.ClientId));

        Assert.Equal(original, store.TryGet(original.ClientId));
    }

    /// <summary>Verifies that unblocking clears block metadata and forgetting removes the record.</summary>
    [Fact]
    public async Task UnblockThenForgetAsync_RemovesKnownDevice()
    {
        TrustStore store = await CreateStoreAsync(new FakeTrustStorePersistence());
        TrustRecord blocked = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await store.UpsertAsync(blocked);

        Assert.Equal(TrustMutationOutcome.Changed, await store.UnblockAsync(blocked.ClientId));
        Assert.Equal(KnownDeviceState.Unpaired, store.TryGet(blocked.ClientId)!.State);
        Assert.Null(store.TryGet(blocked.ClientId)!.BlockedAtUtc);
        Assert.Equal(TrustMutationOutcome.Changed, await store.ForgetAsync(blocked.ClientId));
        Assert.Null(store.TryGet(blocked.ClientId));
    }

    /// <summary>Verifies that Reset Trust revokes only trusted records and preserves other states.</summary>
    [Fact]
    public async Task ResetTrustAsync_RevokesTrustedOnly()
    {
        TrustStore store = await CreateStoreAsync(new FakeTrustStorePersistence());
        TrustRecord trusted = new(ClientId.NewId(), "12345", "Trusted", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        TrustRecord revoked = new(ClientId.NewId(), "12346", "Revoked", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);
        TrustRecord blocked = new(ClientId.NewId(), "12347", "Blocked", KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await store.UpsertAsync(trusted);
        await store.UpsertAsync(revoked);
        await store.UpsertAsync(blocked);

        IReadOnlyList<ClientId> affected = await store.ResetTrustAsync();

        Assert.Equal([trusted.ClientId], affected);
        Assert.Equal(KnownDeviceState.Revoked, store.TryGet(trusted.ClientId)!.State);
        Assert.Empty(store.TryGet(trusted.ClientId)!.CredentialVerifier);
        Assert.Equal(revoked, store.TryGet(revoked.ClientId));
        Assert.Equal(blocked, store.TryGet(blocked.ClientId));
    }

    /// <summary>
    /// Verifies that Reset Trust with no currently trusted records still advances the security fence:
    /// without this, an in-flight pending pairing credential whose captured fence generation still
    /// matches could be persisted after a concurrent Reset Trust believed it had invalidated
    /// everything, since nothing else would have moved the fence. Also verifies this fence-only
    /// advance never writes to persistence, since nothing in the record set actually changed.
    /// </summary>
    [Fact]
    public async Task ResetTrustAsync_NoTrustedRecords_StillAdvancesSecurityFenceGeneration()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        long initialGeneration = store.SecurityFenceGeneration;

        IReadOnlyList<ClientId> affected = await store.ResetTrustAsync();

        Assert.Empty(affected);
        Assert.Equal(initialGeneration + 1, store.SecurityFenceGeneration);
        Assert.Equal(0, persistence.SaveCallCount);
    }

    /// <summary>Verifies that a failed Reset Trust persistence write restores the prior state.</summary>
    [Fact]
    public async Task ResetTrustAsync_PersistenceFails_RestoresTrustedRecord()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "12345", "Trusted", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        await store.UpsertAsync(record);
        long generation = store.SecurityFenceGeneration;
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.ResetTrustAsync());

        Assert.Equal(record, store.TryGet(record.ClientId));
        Assert.Equal(generation, store.SecurityFenceGeneration);
    }

    /// <summary>Verifies that a failed forget restores the removed record and security fence generation.</summary>
    [Fact]
    public async Task ForgetAsync_PersistenceFails_RestoresRecord()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "12345", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);
        await store.UpsertAsync(record);
        long generation = store.SecurityFenceGeneration;
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.ForgetAsync(record.ClientId));

        Assert.Equal(record, store.TryGet(record.ClientId));
        Assert.Equal(generation, store.SecurityFenceGeneration);
    }

    /// <summary>Verifies that a successful clear advances the security fence generation.</summary>
    [Fact]
    public async Task ClearAsync_Success_AdvancesSecurityFenceGeneration()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        long initialGeneration = store.SecurityFenceGeneration;

        await store.ClearAsync();

        Assert.Equal(initialGeneration + 1, store.SecurityFenceGeneration);
    }

    /// <summary>Verifies that a store constructed over an empty persistence starts with no records.</summary>
    [Fact]
    public async Task CreateAsync_EmptyPersistence_StartsEmpty()
    {
        TrustStore store = await CreateStoreAsync(new FakeTrustStorePersistence());

        Assert.Empty(store.List());
    }

    /// <summary>Verifies that a store constructed over persistence with existing records loads them.</summary>
    [Fact]
    public async Task CreateAsync_PersistenceHasRecords_LoadsThem()
    {
        var persistence = new FakeTrustStorePersistence();
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await persistence.SaveAsync([record]);

        TrustStore store = await CreateStoreAsync(persistence);

        Assert.Equal(record, store.TryGet(record.ClientId));
    }

    /// <summary>Verifies that looking up a client that was never stored returns null.</summary>
    [Fact]
    public async Task TryGet_UnknownClient_ReturnsNull()
    {
        TrustStore store = await CreateStoreAsync(new FakeTrustStorePersistence());

        Assert.Null(store.TryGet(ClientId.NewId()));
    }

    /// <summary>Verifies that upserting a new client both stores it in memory and writes it through to persistence.</summary>
    [Fact]
    public async Task UpsertAsync_NewClient_StoresAndWritesThrough()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        await store.UpsertAsync(record);

        Assert.Equal(record, store.TryGet(record.ClientId));
        Assert.Equal(1, persistence.SaveCallCount);
        Assert.Equal([record], persistence.SavedRecords);
    }

    /// <summary>Verifies that upserting an existing client's id replaces its record rather than adding a second one.</summary>
    [Fact]
    public async Task UpsertAsync_ExistingClient_ReplacesRecord()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        ClientId clientId = ClientId.NewId();
        var original = new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        var revoked = original with { State = KnownDeviceState.Revoked };

        await store.UpsertAsync(original);
        await store.UpsertAsync(revoked);

        Assert.Equal(revoked, store.TryGet(clientId));
        Assert.Single(store.List());
    }

    /// <summary>
    /// Verifies that a second store built over the same persistence instance sees a prior store's
    /// upserts, proving trust survives a host restart per ai/context/host/architecture.md.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AfterPriorStoreUpserted_SeesItsRecords()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore firstStoreInstance = await CreateStoreAsync(persistence);
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await firstStoreInstance.UpsertAsync(record);

        TrustStore restartedStore = await CreateStoreAsync(persistence);

        Assert.Equal(record, restartedStore.TryGet(record.ClientId));
    }

    /// <summary>Verifies that a load failure at construction propagates rather than starting an empty store silently.</summary>
    [Fact]
    public async Task CreateAsync_PersistenceLoadFails_Propagates()
    {
        var persistence = new FakeTrustStorePersistence { ThrowOnLoad = new InvalidDataException("corrupt") };

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateStoreAsync(persistence));
    }

    /// <summary>Verifies that a failed persistence write rolls back the in-memory mutation rather than reporting an unsaved value as current.</summary>
    [Fact]
    public async Task UpsertAsync_PersistenceSaveFails_RollsBackInMemoryValue()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        ClientId clientId = ClientId.NewId();
        var original = new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);

        persistence.ThrowOnSave = new IOException("disk full");
        var attemptedUpdate = original with { State = KnownDeviceState.Revoked };
        await Assert.ThrowsAsync<IOException>(() => store.UpsertAsync(attemptedUpdate));

        Assert.Equal(original, store.TryGet(clientId));
    }

    /// <summary>Verifies that a failed persistence write for a brand-new client removes it from memory rather than leaving a phantom entry.</summary>
    [Fact]
    public async Task UpsertAsync_PersistenceSaveFailsForNewClient_RemovesPhantomEntry()
    {
        var persistence = new FakeTrustStorePersistence { ThrowOnSave = new IOException("disk full") };
        TrustStore store = await CreateStoreAsync(persistence);
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<IOException>(() => store.UpsertAsync(record));

        Assert.Null(store.TryGet(record.ClientId));
    }

    /// <summary>Verifies that List() reports every distinct client that has been upserted.</summary>
    [Fact]
    public async Task List_MultipleDistinctClients_ReturnsAll()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        var first = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "CD34", "Bedroom Tablet", KnownDeviceState.Trusted, "beefdead", DateTimeOffset.UtcNow);

        await store.UpsertAsync(first);
        await store.UpsertAsync(second);

        Assert.Equal(2, store.List().Count);
        Assert.Contains(first, store.List());
        Assert.Contains(second, store.List());
    }

    /// <summary>Verifies that concurrent upserts for distinct clients all persist without losing any of them to the race a naive read-mutate-save sequence would allow.</summary>
    [Fact]
    public async Task UpsertAsync_ConcurrentDistinctClients_AllSurvive()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord[] records = Enumerable.Range(0, 20)
            .Select(i => new TrustRecord(ClientId.NewId(), $"C{i}", $"Device {i}", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow))
            .ToArray();

        await Task.WhenAll(records.Select(record => store.UpsertAsync(record)));

        Assert.Equal(records.Length, store.List().Count);
        Assert.Equal(records.Length, persistence.SaveCallCount);
        Assert.Equal(records.Length, persistence.SavedRecords.Count);
        foreach (TrustRecord record in records)
        {
            Assert.Equal(record, store.TryGet(record.ClientId));
        }
    }

    /// <summary>Creates a trust store with a controllable clock for tests.</summary>
    private static Task<TrustStore> CreateStoreAsync(
        FakeTrustStorePersistence persistence,
        FakeClock? clock = null) =>
        TrustStore.CreateAsync(persistence, clock ?? new FakeClock());
}
