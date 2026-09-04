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

    /// <summary>Verifies that a conditional upsert never publishes its record or advances the generation when persistence fails.</summary>
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

    /// <summary>Verifies that a failed conditional replacement leaves the prior record and generation untouched.</summary>
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

    /// <summary>
    /// Verifies that a matching security fence generation advances
    /// <see cref="TrustStore.SecurityFenceGeneration"/> by exactly one once persistence succeeds.
    /// </summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_MatchingGeneration_AdvancesSecurityFenceGenerationExactlyOnce()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        long initialGeneration = store.SecurityFenceGeneration;
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        bool committed = await store.TryUpsertIfGenerationAsync(record, initialGeneration);

        Assert.True(committed);
        Assert.Equal(initialGeneration + 1, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies that a record a conditional upsert is establishing never becomes visible through
    /// <see cref="TrustStore.TryGet"/>, <see cref="TrustStore.List"/>, or
    /// <see cref="TrustStore.TryGetByShortId"/> while its persistence write is still in flight, and
    /// becomes visible through all three at once as soon as that write succeeds -- the transient-trust
    /// window a concurrent reader could otherwise observe before durability was actually established.
    /// </summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_WhilePersistenceBlocked_DoesNotExposeProposedRecordUntilPersistenceSucceeds()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        long initialGeneration = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<bool> upsert = store.TryUpsertIfGenerationAsync(record, initialGeneration);
        await enteredSave.Task;

        Assert.Null(store.TryGet(record.ClientId));
        Assert.DoesNotContain(record, store.List());
        Assert.Null(store.TryGetByShortId(record.ShortId));
        Assert.Equal(initialGeneration, store.SecurityFenceGeneration);

        releaseSave.SetResult();
        Assert.True(await upsert);

        Assert.Equal(record, store.TryGet(record.ClientId));
        Assert.Contains(record, store.List());
        Assert.Equal(record, store.TryGetByShortId(record.ShortId));
        Assert.Equal(initialGeneration + 1, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies the same transient-visibility guarantee as
    /// <see cref="TryUpsertIfGenerationAsync_WhilePersistenceBlocked_DoesNotExposeProposedRecordUntilPersistenceSucceeds"/>
    /// when the conditional upsert replaces an already-known record rather than inserting a new one:
    /// while persistence is blocked, readers must keep seeing the prior record, not the proposed
    /// replacement -- and if persistence then fails, the prior record must still be exactly what they
    /// see afterward, since the replacement was never published.
    /// </summary>
    [Fact]
    public async Task TryUpsertIfGenerationAsync_ExistingRecordWhilePersistenceBlocked_KeepsExposingPriorRecordUntilResolved()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        ClientId clientId = ClientId.NewId();
        TrustRecord original = new(clientId, "12345", "Living Room PC", KnownDeviceState.Revoked, "oldhash", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        TrustRecord replacement = original with { State = KnownDeviceState.Trusted, CredentialVerifier = "newhash" };
        long generationBeforeReplacement = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<bool> upsert = store.TryUpsertIfGenerationAsync(replacement, generationBeforeReplacement);
        await enteredSave.Task;

        Assert.Equal(original, store.TryGet(clientId));
        Assert.Contains(original, store.List());
        Assert.Equal(generationBeforeReplacement, store.SecurityFenceGeneration);

        releaseSave.SetException(new IOException("disk full"));
        await Assert.ThrowsAsync<IOException>(() => upsert);

        Assert.Equal(original, store.TryGet(clientId));
        Assert.Equal(generationBeforeReplacement, store.SecurityFenceGeneration);
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

    /// <summary>
    /// Verifies that an unpaired known device is never eligible for Block, per the canonical Stage 3.2
    /// contract in <c>ai/context/protocol/security.md</c>: Block applies only to a Trusted or Revoked
    /// device.
    /// </summary>
    [Fact]
    public async Task BlockAsync_UnpairedRecord_ReturnsNotEligibleWithoutMutation()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero) };
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence, clock);
        TrustRecord original = new(ClientId.NewId(), "12345", null, KnownDeviceState.Unpaired, string.Empty, clock.UtcNow.AddDays(-1));
        await store.UpsertAsync(original);
        long generationBeforeBlock = store.SecurityFenceGeneration;

        Assert.Equal(TrustMutationOutcome.NotEligible, await store.BlockAsync(original.ClientId));

        Assert.Equal(original, store.TryGet(original.ClientId));
        Assert.Equal(generationBeforeBlock, store.SecurityFenceGeneration);
        Assert.Equal(1, persistence.SaveCallCount);
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

    /// <summary>Verifies that blocking an already-blocked device is a truthful, unmutated no-op.</summary>
    [Fact]
    public async Task BlockAsync_AlreadyBlockedRecord_ReturnsAlreadyInStateWithoutMutation()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero) };
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence, clock);
        TrustRecord original = new(ClientId.NewId(), "12345", null, KnownDeviceState.Blocked, string.Empty, clock.UtcNow.AddDays(-1), clock.UtcNow.AddDays(-1));
        await store.UpsertAsync(original);
        long generationBeforeBlock = store.SecurityFenceGeneration;

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TrustMutationOutcome.AlreadyInState, await store.BlockAsync(original.ClientId));

        Assert.Equal(original, store.TryGet(original.ClientId));
        Assert.Equal(generationBeforeBlock, store.SecurityFenceGeneration);
        Assert.Equal(1, persistence.SaveCallCount);
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
        TrustRecord original = new(ClientId.NewId(), "12345", null, KnownDeviceState.Trusted, "deadbeef", clock.UtcNow.AddDays(-1));
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

    /// <summary>
    /// Verifies the same transient-visibility guarantee every other mutation already proves, for
    /// <see cref="TrustStore.ForgetAsync"/>'s distinct persist-before-delete branch: while its
    /// persistence write is in flight, the record being forgotten must remain observably present to
    /// every reader, rather than transiently appearing gone before durability is established.
    /// </summary>
    [Fact]
    public async Task ForgetAsync_WhilePersistenceBlocked_KeepsExposingRecordUntilPersistenceSucceeds()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);
        await store.UpsertAsync(record);
        long generationBeforeForget = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> forget = store.ForgetAsync(record.ClientId);
        await enteredSave.Task;

        Assert.Equal(record, store.TryGet(record.ClientId));
        Assert.Contains(record, store.List());
        Assert.Equal(record, store.TryGetByShortId(record.ShortId));
        Assert.Equal(generationBeforeForget, store.SecurityFenceGeneration);

        releaseSave.SetResult();
        Assert.Equal(TrustMutationOutcome.Changed, await forget);

        Assert.Null(store.TryGet(record.ClientId));
        Assert.Equal(generationBeforeForget + 1, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies the other half of the transient-visibility guarantee: if persistence then fails, the
    /// record must still be present -- never having been exposed as gone in the meantime -- and the
    /// security fence must not have moved.
    /// </summary>
    [Fact]
    public async Task ForgetAsync_WhilePersistenceBlocked_PersistenceFails_RecordSurvives()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Unpaired, string.Empty, DateTimeOffset.UtcNow);
        await store.UpsertAsync(record);
        long generationBeforeForget = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> forget = store.ForgetAsync(record.ClientId);
        await enteredSave.Task;

        Assert.Equal(record, store.TryGet(record.ClientId));
        Assert.Contains(record, store.List());

        releaseSave.SetException(new IOException("disk full"));
        await Assert.ThrowsAsync<IOException>(() => forget);

        Assert.Equal(record, store.TryGet(record.ClientId));
        Assert.Contains(record, store.List());
        Assert.Equal(generationBeforeForget, store.SecurityFenceGeneration);
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

    /// <summary>
    /// Verifies that a brand-new record an upsert is establishing never becomes visible through
    /// <see cref="TrustStore.TryGet"/> while its persistence write is still in flight, closing the
    /// same transient-visibility window <see cref="TrustStore.TryUpsertIfGenerationAsync"/>'s own
    /// gated tests prove, but for the unconditional <see cref="TrustStore.UpsertAsync"/> path.
    /// </summary>
    [Fact]
    public async Task UpsertAsync_WhilePersistenceBlocked_DoesNotExposeNewRecordUntilPersistenceSucceeds()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord record = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        long initialGeneration = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task upsert = store.UpsertAsync(record);
        await enteredSave.Task;

        Assert.Null(store.TryGet(record.ClientId));
        Assert.DoesNotContain(record, store.List());
        Assert.Null(store.TryGetByShortId(record.ShortId));
        Assert.Equal(initialGeneration, store.SecurityFenceGeneration);

        releaseSave.SetResult();
        await upsert;

        Assert.Equal(record, store.TryGet(record.ClientId));
        Assert.Contains(record, store.List());
        Assert.Equal(record, store.TryGetByShortId(record.ShortId));
        Assert.Equal(initialGeneration + 1, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies the confirmed Unblock fail-open race is closed: while <see cref="TrustStore.UnblockAsync"/>'s
    /// persistence write is in flight, a Blocked client must remain observably Blocked to every reader
    /// -- <see cref="TrustStore.TryGet"/>, <see cref="TrustStore.List"/>, and
    /// <see cref="TrustStore.TryGetByShortId"/> alike -- rather than transiently reporting Unpaired
    /// before durability is established. Only once persistence actually succeeds does the client become
    /// Unpaired and the security fence advance.
    /// </summary>
    [Fact]
    public async Task UnblockAsync_WhilePersistenceBlocked_KeepsExposingBlockedAcrossAllReadersUntilPersistenceSucceeds()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero) };
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence, clock);
        TrustRecord blocked = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Blocked, string.Empty, clock.UtcNow.AddDays(-1), clock.UtcNow.AddDays(-1));
        await store.UpsertAsync(blocked);
        long generationBeforeUnblock = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> unblock = store.UnblockAsync(blocked.ClientId);
        await enteredSave.Task;

        Assert.Equal(blocked, store.TryGet(blocked.ClientId));
        Assert.Contains(blocked, store.List());
        Assert.Equal(blocked, store.TryGetByShortId(blocked.ShortId));
        Assert.Equal(generationBeforeUnblock, store.SecurityFenceGeneration);

        releaseSave.SetResult();
        Assert.Equal(TrustMutationOutcome.Changed, await unblock);

        Assert.Equal(KnownDeviceState.Unpaired, store.TryGet(blocked.ClientId)!.State);
        Assert.Equal(generationBeforeUnblock + 1, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies the other half of the Unblock fail-open race: if persistence then fails, the client
    /// must remain Blocked -- never having been exposed as Unpaired in the meantime -- and the security
    /// fence must not have moved, since a moved fence together with a transiently-exposed Unpaired
    /// state is exactly what would let a pending pairing credential created during the transient window
    /// later ACK past a Block that was supposed to have prevented it.
    /// </summary>
    [Fact]
    public async Task UnblockAsync_WhilePersistenceBlocked_PersistenceFails_RemainsBlocked()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero) };
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence, clock);
        TrustRecord blocked = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Blocked, string.Empty, clock.UtcNow.AddDays(-1), clock.UtcNow.AddDays(-1));
        await store.UpsertAsync(blocked);
        long generationBeforeUnblock = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> unblock = store.UnblockAsync(blocked.ClientId);
        await enteredSave.Task;

        Assert.Equal(blocked, store.TryGet(blocked.ClientId));
        Assert.Contains(blocked, store.List());
        Assert.Equal(blocked, store.TryGetByShortId(blocked.ShortId));

        releaseSave.SetException(new IOException("disk full"));
        await Assert.ThrowsAsync<IOException>(() => unblock);

        Assert.Equal(blocked, store.TryGet(blocked.ClientId));
        Assert.Contains(blocked, store.List());
        Assert.Equal(blocked, store.TryGetByShortId(blocked.ShortId));
        Assert.Equal(generationBeforeUnblock, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies the confirmed Factory Reset/Clear race is closed: while <see cref="TrustStore.ClearAsync"/>'s
    /// persistence write is in flight, every previously known record -- Blocked included -- must remain
    /// observably present to every reader, rather than transiently appearing unknown before durability
    /// is established. Only once persistence actually succeeds does the store become empty and the
    /// security fence advance.
    /// </summary>
    [Fact]
    public async Task ClearAsync_WhilePersistenceBlocked_KeepsExposingRecordsUntilPersistenceSucceeds()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord blocked = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        TrustRecord trusted = new(ClientId.NewId(), "CD34", "Bedroom Tablet", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await store.UpsertAsync(blocked);
        await store.UpsertAsync(trusted);
        long generationBeforeClear = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task clear = store.ClearAsync();
        await enteredSave.Task;

        Assert.Equal(blocked, store.TryGet(blocked.ClientId));
        Assert.Contains(trusted, store.List());
        Assert.Equal(blocked, store.TryGetByShortId(blocked.ShortId));
        Assert.Equal(generationBeforeClear, store.SecurityFenceGeneration);

        releaseSave.SetResult();
        await clear;

        Assert.Empty(store.List());
        Assert.Equal(generationBeforeClear + 1, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies the other half of the Factory Reset/Clear race: if persistence then fails, every
    /// previously known record must still be present -- never having been exposed as gone in the
    /// meantime -- and the security fence must not have moved, since a transiently-empty store is
    /// exactly the window a client could otherwise use to begin pairing as though never known.
    /// </summary>
    [Fact]
    public async Task ClearAsync_WhilePersistenceBlocked_PersistenceFails_RecordsSurvive()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord blocked = new(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await store.UpsertAsync(blocked);
        long generationBeforeClear = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task clear = store.ClearAsync();
        await enteredSave.Task;

        Assert.Equal(blocked, store.TryGet(blocked.ClientId));
        Assert.Contains(blocked, store.List());
        Assert.Equal(blocked, store.TryGetByShortId(blocked.ShortId));

        releaseSave.SetException(new IOException("disk full"));
        await Assert.ThrowsAsync<IOException>(() => clear);

        Assert.Equal(blocked, store.TryGet(blocked.ClientId));
        Assert.Contains(blocked, store.List());
        Assert.Equal(blocked, store.TryGetByShortId(blocked.ShortId));
        Assert.Equal(generationBeforeClear, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies the same transient-visibility guarantee for <see cref="TrustStore.ResetTrustAsync"/>'s
    /// affected-record path: while its persistence write is in flight, a currently Trusted record must
    /// remain observably Trusted rather than transiently appearing Revoked before durability is
    /// established.
    /// </summary>
    [Fact]
    public async Task ResetTrustAsync_WhilePersistenceBlocked_KeepsExposingTrustedUntilPersistenceSucceeds()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord trusted = new(ClientId.NewId(), "12345", "Trusted", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        await store.UpsertAsync(trusted);
        long generationBeforeReset = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<IReadOnlyList<ClientId>> reset = store.ResetTrustAsync();
        await enteredSave.Task;

        Assert.Equal(trusted, store.TryGet(trusted.ClientId));
        Assert.Contains(trusted, store.List());
        Assert.Equal(trusted, store.TryGetByShortId(trusted.ShortId));
        Assert.Equal(generationBeforeReset, store.SecurityFenceGeneration);

        releaseSave.SetResult();
        IReadOnlyList<ClientId> affected = await reset;

        Assert.Equal([trusted.ClientId], affected);
        Assert.Equal(KnownDeviceState.Revoked, store.TryGet(trusted.ClientId)!.State);
        Assert.Equal(generationBeforeReset + 1, store.SecurityFenceGeneration);
    }

    /// <summary>Verifies that renaming a trusted device updates only the display name, persists it, and advances the security fence.</summary>
    [Fact]
    public async Task RenameIfTrustedAsync_TrustedRecord_UpdatesDisplayNamePreservingIdentity()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        DateTimeOffset pairedAt = DateTimeOffset.UtcNow.AddDays(-1);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", pairedAt);
        await store.UpsertAsync(original);
        long generationBeforeRename = store.SecurityFenceGeneration;

        TrustMutationOutcome outcome = await store.RenameIfTrustedAsync(original.ClientId, "Bedroom PC");

        Assert.Equal(TrustMutationOutcome.Changed, outcome);
        TrustRecord renamed = store.TryGet(original.ClientId)!;
        Assert.Equal("Bedroom PC", renamed.DisplayName);
        Assert.Equal(original.ShortId, renamed.ShortId);
        Assert.Equal(original.CredentialVerifier, renamed.CredentialVerifier);
        Assert.Equal(original.PairedAtUtc, renamed.PairedAtUtc);
        Assert.Equal(original.State, renamed.State);
        Assert.Equal(generationBeforeRename + 1, store.SecurityFenceGeneration);
        Assert.Equal(renamed, Assert.Single(persistence.SavedRecords));
    }

    /// <summary>
    /// Verifies the same transient-visibility guarantee every other mutation already proves for
    /// <see cref="TrustStore.RenameIfTrustedAsync"/>: while its persistence write is in flight, every
    /// reader must keep seeing the prior display name, not the proposed replacement.
    /// </summary>
    [Fact]
    public async Task RenameIfTrustedAsync_WhilePersistenceBlocked_KeepsExposingOriginalNameUntilPersistenceSucceeds()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        long generationBeforeRename = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> rename = store.RenameIfTrustedAsync(original.ClientId, "Bedroom PC");
        await enteredSave.Task;

        Assert.Equal(original, store.TryGet(original.ClientId));
        Assert.Contains(original, store.List());
        Assert.Equal(original, store.TryGetByShortId(original.ShortId));
        Assert.Equal(generationBeforeRename, store.SecurityFenceGeneration);

        releaseSave.SetResult();
        Assert.Equal(TrustMutationOutcome.Changed, await rename);

        Assert.Equal("Bedroom PC", store.TryGet(original.ClientId)!.DisplayName);
        Assert.Equal(generationBeforeRename + 1, store.SecurityFenceGeneration);
    }

    /// <summary>
    /// Verifies the other half of the transient-visibility guarantee: if persistence then fails, the
    /// device must keep its original display name -- never having been exposed with the proposed name
    /// in the meantime -- and the security fence must not have moved.
    /// </summary>
    [Fact]
    public async Task RenameIfTrustedAsync_WhilePersistenceBlocked_PersistenceFails_KeepsOriginalName()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        long generationBeforeRename = store.SecurityFenceGeneration;
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> rename = store.RenameIfTrustedAsync(original.ClientId, "Bedroom PC");
        await enteredSave.Task;

        releaseSave.SetException(new IOException("disk full"));
        await Assert.ThrowsAsync<IOException>(() => rename);

        Assert.Equal(original, store.TryGet(original.ClientId));
        Assert.Equal(generationBeforeRename, store.SecurityFenceGeneration);
    }

    /// <summary>Verifies that renaming an unknown client reports not found without mutating persistence.</summary>
    [Fact]
    public async Task RenameIfTrustedAsync_UnknownClient_ReturnsNotFound()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);

        Assert.Equal(TrustMutationOutcome.NotFound, await store.RenameIfTrustedAsync(ClientId.NewId(), "New Name"));
        Assert.Empty(persistence.SavedRecords);
    }

    /// <summary>Verifies that a non-trusted device cannot be renamed and is left untouched.</summary>
    [Theory]
    [InlineData(KnownDeviceState.Revoked)]
    [InlineData(KnownDeviceState.Blocked)]
    [InlineData(KnownDeviceState.Unpaired)]
    public async Task RenameIfTrustedAsync_NonTrustedRecord_ReturnsNotEligibleWithoutMutation(KnownDeviceState state)
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", state, string.Empty, DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        long generationBeforeRename = store.SecurityFenceGeneration;

        Assert.Equal(TrustMutationOutcome.NotEligible, await store.RenameIfTrustedAsync(original.ClientId, "New Name"));

        Assert.Equal(original, store.TryGet(original.ClientId));
        Assert.Equal(generationBeforeRename, store.SecurityFenceGeneration);
    }

    /// <summary>Verifies that a failed rename persistence write restores the original display name.</summary>
    [Fact]
    public async Task RenameIfTrustedAsync_PersistenceFails_RestoresOriginalRecord()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "hash", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        persistence.ThrowOnSave = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => store.RenameIfTrustedAsync(original.ClientId, "New Name"));

        Assert.Equal(original, store.TryGet(original.ClientId));
    }

    /// <summary>
    /// Proves the confirmed Rename-resurrection defect is closed for Revoke: a rename that reaches
    /// this store's serialized mutation only after a concurrent Revoke has already committed must
    /// observe the now-Revoked record, not the Trusted snapshot it would have read had it captured
    /// state before acquiring the store's own mutation serialization -- so it reports
    /// <see cref="TrustMutationOutcome.NotEligible"/> and never resurrects Trusted state or the
    /// destroyed credential verifier.
    /// </summary>
    [Fact]
    public async Task RenameIfTrustedAsync_QueuedBehindConcurrentRevoke_DoesNotResurrectTrust()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> revoke = store.RevokeAsync(original.ClientId);
        await enteredSave.Task;
        Task<TrustMutationOutcome> rename = store.RenameIfTrustedAsync(original.ClientId, "New Name");

        releaseSave.SetResult();
        TrustMutationOutcome[] outcomes = await Task.WhenAll(revoke, rename);

        Assert.Equal(TrustMutationOutcome.Changed, outcomes[0]);
        Assert.Equal(TrustMutationOutcome.NotEligible, outcomes[1]);
        TrustRecord final = store.TryGet(original.ClientId)!;
        Assert.Equal(KnownDeviceState.Revoked, final.State);
        Assert.Empty(final.CredentialVerifier);
        Assert.Equal(original.DisplayName, final.DisplayName);
    }

    /// <summary>Proves the same Rename-resurrection defect is closed for Block, per <see cref="RenameIfTrustedAsync_QueuedBehindConcurrentRevoke_DoesNotResurrectTrust"/>.</summary>
    [Fact]
    public async Task RenameIfTrustedAsync_QueuedBehindConcurrentBlock_DoesNotResurrectTrust()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> block = store.BlockAsync(original.ClientId);
        await enteredSave.Task;
        Task<TrustMutationOutcome> rename = store.RenameIfTrustedAsync(original.ClientId, "New Name");

        releaseSave.SetResult();
        TrustMutationOutcome[] outcomes = await Task.WhenAll(block, rename);

        Assert.Equal(TrustMutationOutcome.Changed, outcomes[0]);
        Assert.Equal(TrustMutationOutcome.NotEligible, outcomes[1]);
        TrustRecord final = store.TryGet(original.ClientId)!;
        Assert.Equal(KnownDeviceState.Blocked, final.State);
        Assert.Empty(final.CredentialVerifier);
        Assert.Equal(original.DisplayName, final.DisplayName);
    }

    /// <summary>Proves the same Rename-resurrection defect is closed for Reset Trust, per <see cref="RenameIfTrustedAsync_QueuedBehindConcurrentRevoke_DoesNotResurrectTrust"/>.</summary>
    [Fact]
    public async Task RenameIfTrustedAsync_QueuedBehindConcurrentResetTrust_DoesNotResurrectTrust()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<IReadOnlyList<ClientId>> resetTrust = store.ResetTrustAsync();
        await enteredSave.Task;
        Task<TrustMutationOutcome> rename = store.RenameIfTrustedAsync(original.ClientId, "New Name");

        releaseSave.SetResult();
        await resetTrust;
        TrustMutationOutcome renameOutcome = await rename;

        Assert.Equal(TrustMutationOutcome.NotEligible, renameOutcome);
        TrustRecord final = store.TryGet(original.ClientId)!;
        Assert.Equal(KnownDeviceState.Revoked, final.State);
        Assert.Empty(final.CredentialVerifier);
        Assert.Equal(original.DisplayName, final.DisplayName);
    }

    /// <summary>
    /// Proves the same Rename-resurrection defect is closed for Factory Reset: a rename queued behind
    /// a concurrent <see cref="TrustStore.ClearAsync"/> finds no record left to rename at all -- the
    /// deleted record must never be resurrected by a stale replacement either.
    /// </summary>
    [Fact]
    public async Task RenameIfTrustedAsync_QueuedBehindConcurrentClear_ReturnsNotFound()
    {
        var persistence = new FakeTrustStorePersistence();
        TrustStore store = await CreateStoreAsync(persistence);
        TrustRecord original = new(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        await store.UpsertAsync(original);
        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task clear = store.ClearAsync();
        await enteredSave.Task;
        Task<TrustMutationOutcome> rename = store.RenameIfTrustedAsync(original.ClientId, "New Name");

        releaseSave.SetResult();
        await clear;
        TrustMutationOutcome renameOutcome = await rename;

        Assert.Equal(TrustMutationOutcome.NotFound, renameOutcome);
        Assert.Null(store.TryGet(original.ClientId));
    }

    /// <summary>Creates a trust store with a controllable clock for tests.</summary>
    private static Task<TrustStore> CreateStoreAsync(
        FakeTrustStorePersistence persistence,
        FakeClock? clock = null) =>
        TrustStore.CreateAsync(persistence, clock ?? new FakeClock());
}
