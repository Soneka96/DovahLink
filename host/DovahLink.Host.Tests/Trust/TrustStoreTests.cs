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
        TrustStore store = await TrustStore.CreateAsync(persistence);
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
        TrustStore store = await TrustStore.CreateAsync(persistence);
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
        TrustStore store = await TrustStore.CreateAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        await Task.WhenAll(store.ClearAsync(), store.UpsertAsync(record));

        Assert.Equal(
            store.List().OrderBy(saved => saved.ClientId.Value),
            persistence.SavedRecords.OrderBy(saved => saved.ClientId.Value));
    }

    /// <summary>Verifies that a matching mutation generation permits a conditional upsert.</summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_MatchingGeneration_PersistsRecord()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await TrustStore.CreateAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        bool committed = await store.TryUpsertIfGenerationAsync(record, store.MutationGeneration);

        Assert.True(committed);
        Assert.Equal(record, store.TryGet(record.ClientId));
    }

    /// <summary>Verifies that a stale mutation generation cannot recreate trust after another mutation.</summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_StaleGeneration_DoesNotMutateStore()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await TrustStore.CreateAsync(persistence);
        long initialGeneration = store.MutationGeneration;
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
        TrustStore store = await TrustStore.CreateAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        long generation = store.MutationGeneration;
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.TryUpsertIfGenerationAsync(record, generation));

        Assert.Null(store.TryGet(record.ClientId));
        Assert.Equal(generation, store.MutationGeneration);
    }

    /// <summary>Verifies that a failed conditional replacement restores the prior record and generation.</summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_ExistingRecordSaveFails_RestoresPriorRecord()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await TrustStore.CreateAsync(persistence);
        ClientId clientId = ClientId.NewId();
        TrustRecord original = new(clientId, "12345", "Living Room PC", KnownDeviceState.Revoked, "oldhash", DateTimeOffset.UtcNow);
        TrustRecord replacement = original with { State = KnownDeviceState.Trusted, CredentialVerifier = "newhash" };
        await store.UpsertAsync(original);
        long generation = store.MutationGeneration;
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.TryUpsertIfGenerationAsync(replacement, generation));

        Assert.Equal(original, store.TryGet(clientId));
        Assert.Equal(generation, store.MutationGeneration);
    }

    /// <summary>Verifies that a successful clear advances the mutation generation.</summary>
    [Fact]
    public async Task ClearAsync_Success_AdvancesMutationGeneration()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await TrustStore.CreateAsync(persistence);
        long initialGeneration = store.MutationGeneration;

        await store.ClearAsync();

        Assert.Equal(initialGeneration + 1, store.MutationGeneration);
    }

    /// <summary>Verifies that a store constructed over an empty persistence starts with no records.</summary>
    [Fact]
    public async Task CreateAsync_EmptyPersistence_StartsEmpty()
    {
        TrustStore store = await TrustStore.CreateAsync(new FakeTrustStorePersistence());

        Assert.Empty(store.List());
    }

    /// <summary>Verifies that a store constructed over persistence with existing records loads them.</summary>
    [Fact]
    public async Task CreateAsync_PersistenceHasRecords_LoadsThem()
    {
        var persistence = new FakeTrustStorePersistence();
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await persistence.SaveAsync([record]);

        TrustStore store = await TrustStore.CreateAsync(persistence);

        Assert.Equal(record, store.TryGet(record.ClientId));
    }

    /// <summary>Verifies that looking up a client that was never stored returns null.</summary>
    [Fact]
    public async Task TryGet_UnknownClient_ReturnsNull()
    {
        TrustStore store = await TrustStore.CreateAsync(new FakeTrustStorePersistence());

        Assert.Null(store.TryGet(ClientId.NewId()));
    }

    /// <summary>Verifies that upserting a new client both stores it in memory and writes it through to persistence.</summary>
    [Fact]
    public async Task UpsertAsync_NewClient_StoresAndWritesThrough()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await TrustStore.CreateAsync(persistence);
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
        TrustStore store = await TrustStore.CreateAsync(persistence);
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
        TrustStore firstStoreInstance = await TrustStore.CreateAsync(persistence);
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await firstStoreInstance.UpsertAsync(record);

        TrustStore restartedStore = await TrustStore.CreateAsync(persistence);

        Assert.Equal(record, restartedStore.TryGet(record.ClientId));
    }

    /// <summary>Verifies that a load failure at construction propagates rather than starting an empty store silently.</summary>
    [Fact]
    public async Task CreateAsync_PersistenceLoadFails_Propagates()
    {
        var persistence = new FakeTrustStorePersistence { ThrowOnLoad = new InvalidDataException("corrupt") };

        await Assert.ThrowsAsync<InvalidDataException>(() => TrustStore.CreateAsync(persistence));
    }

    /// <summary>Verifies that a failed persistence write rolls back the in-memory mutation rather than reporting an unsaved value as current.</summary>
    [Fact]
    public async Task UpsertAsync_PersistenceSaveFails_RollsBackInMemoryValue()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await TrustStore.CreateAsync(persistence);
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
        TrustStore store = await TrustStore.CreateAsync(persistence);
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<IOException>(() => store.UpsertAsync(record));

        Assert.Null(store.TryGet(record.ClientId));
    }

    /// <summary>Verifies that List() reports every distinct client that has been upserted.</summary>
    [Fact]
    public async Task List_MultipleDistinctClients_ReturnsAll()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await TrustStore.CreateAsync(persistence);
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
        TrustStore store = await TrustStore.CreateAsync(persistence);
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
}
