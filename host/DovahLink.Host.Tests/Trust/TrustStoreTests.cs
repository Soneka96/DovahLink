using DovahLink.Host.Identity;
using DovahLink.Host.Trust;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="TrustStore"/>.</summary>
public class TrustStoreTests
{
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
