using System.Security.Cryptography;
using System.Text.Json;
using DovahLink.Host.Identity;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="WindowsDpapiTrustStorePersistence"/>.</summary>
public class WindowsDpapiTrustStorePersistenceTests : IDisposable
{
    private readonly string filePath = Path.Combine(Path.GetTempPath(), $"dovahlink-trust-store-test-{Guid.NewGuid():N}.dat");

    /// <summary>Verifies that loading before any save has happened returns an empty list.</summary>
    [Fact]
    public async Task LoadAsync_NoFileYet_ReturnsEmpty()
    {
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);

        IReadOnlyList<TrustRecord> records = await persistence.LoadAsync();

        Assert.Empty(records);
    }

    /// <summary>Verifies that saved records can be loaded back unchanged.</summary>
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsRecords()
    {
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);
        var record = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);

        await persistence.SaveAsync([record]);
        IReadOnlyList<TrustRecord> loaded = await persistence.LoadAsync();

        TrustRecord loadedRecord = Assert.Single(loaded);
        Assert.Equal(record, loadedRecord);
    }

    /// <summary>Verifies that a file that is not validly DPAPI-protected fails loudly rather than being treated as empty.</summary>
    [Fact]
    public async Task LoadAsync_UndecryptableFile_ThrowsInvalidDataException()
    {
        await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4, 5]);
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => persistence.LoadAsync());
    }

    /// <summary>
    /// Verifies that a file which decrypts successfully but does not contain valid JSON fails
    /// loudly with the same exception type as an undecryptable file, rather than propagating a
    /// raw <see cref="System.Text.Json.JsonException"/>.
    /// </summary>
    [Fact]
    public async Task LoadAsync_DecryptableButNotJson_ThrowsInvalidDataException()
    {
        byte[] notJson = ProtectedData.Protect([1, 2, 3, 4, 5], optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, notJson);
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => persistence.LoadAsync());
    }

    /// <summary>Verifies that a decryptable JSON null root is rejected rather than treated as an empty store.</summary>
    [Fact]
    public async Task LoadAsync_DecryptableNullRoot_ThrowsInvalidDataException()
    {
        byte[] jsonNull = ProtectedData.Protect("null"u8.ToArray(), optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, jsonNull);
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => persistence.LoadAsync());
    }

    /// <summary>Verifies that structurally valid JSON with missing record fields fails closed.</summary>
    [Fact]
    public async Task LoadAsync_RecordWithMissingFields_ThrowsInvalidDataException()
    {
        byte[] malformedRecord = ProtectedData.Protect("[{}]"u8.ToArray(), optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, malformedRecord);
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => persistence.LoadAsync());
    }

    /// <summary>Verifies that duplicate administration short IDs fail closed.</summary>
    [Fact]
    public async Task LoadAsync_DuplicateShortIds_ThrowsInvalidDataException()
    {
        var first = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "12345", "Bedroom Tablet", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);
        byte[] encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new[] { first, second }), optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, encrypted);
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => persistence.LoadAsync());
    }

    /// <summary>Verifies that duplicate client identities fail closed.</summary>
    [Fact]
    public async Task LoadAsync_DuplicateClientIds_ThrowsInvalidDataException()
    {
        ClientId clientId = ClientId.NewId();
        var first = new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);
        var second = new TrustRecord(clientId, "54321", "Bedroom Tablet", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);
        await WriteEncryptedRecordsAsync([first, second]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that non-UTC or default blocked timestamps fail closed.</summary>
    [Fact]
    public async Task LoadAsync_InvalidBlockedTimestamp_ThrowsInvalidDataException()
    {
        var record = new TrustRecord(
            ClientId.NewId(),
            "12345",
            "Blocked Device",
            KnownDeviceState.Blocked,
            string.Empty,
            DateTimeOffset.UtcNow,
            new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Unspecified), TimeSpan.FromHours(2)));
        byte[] encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new[] { record }), optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, encrypted);
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => persistence.LoadAsync());
    }

    /// <summary>Verifies that an invalid enum value and empty client identity fail closed.</summary>
    [Fact]
    public async Task LoadAsync_InvalidIdentityOrState_ThrowsInvalidDataException()
    {
        byte[] invalidStateJson = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ClientId = Guid.Empty,
            ShortId = "12345",
            DisplayName = (string?)null,
            State = 99,
            CredentialVerifier = new string('a', 64),
            PairedAtUtc = DateTimeOffset.UtcNow,
            BlockedAtUtc = (DateTimeOffset?)null,
        });
        await File.WriteAllBytesAsync(filePath, ProtectedData.Protect(invalidStateJson, optionalEntropy: null, DataProtectionScope.CurrentUser));

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that trust states cannot carry inconsistent credential verifier data.</summary>
    [Fact]
    public async Task LoadAsync_InvalidVerifierStateCombination_ThrowsInvalidDataException()
    {
        var invalidRecord = new TrustRecord(ClientId.NewId(), "12345", null, KnownDeviceState.Revoked, new string('a', 64), DateTimeOffset.UtcNow);
        await WriteEncryptedRecordsAsync([invalidRecord]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that malformed short IDs fail closed.</summary>
    [Fact]
    public async Task LoadAsync_InvalidShortId_ThrowsInvalidDataException()
    {
        var invalidRecord = new TrustRecord(ClientId.NewId(), "ABCDE", null, KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);
        await WriteEncryptedRecordsAsync([invalidRecord]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that trusted records require a valid fixed-length hexadecimal verifier.</summary>
    [Fact]
    public async Task LoadAsync_InvalidTrustedVerifier_ThrowsInvalidDataException()
    {
        var invalidRecord = new TrustRecord(ClientId.NewId(), "12345", null, KnownDeviceState.Trusted, "not-a-verifier", DateTimeOffset.UtcNow);
        await WriteEncryptedRecordsAsync([invalidRecord]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that a blocked record must carry a valid block timestamp.</summary>
    [Fact]
    public async Task LoadAsync_BlockedWithoutTimestamp_ThrowsInvalidDataException()
    {
        var invalidRecord = new TrustRecord(ClientId.NewId(), "12345", null, KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow);
        await WriteEncryptedRecordsAsync([invalidRecord]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that a default pairing timestamp fails closed.</summary>
    [Fact]
    public async Task LoadAsync_DefaultPairedTimestamp_ThrowsInvalidDataException()
    {
        var invalidRecord = new TrustRecord(ClientId.NewId(), "12345", null, KnownDeviceState.Revoked, string.Empty, default);
        await WriteEncryptedRecordsAsync([invalidRecord]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that pairing timestamps must be UTC.</summary>
    [Fact]
    public async Task LoadAsync_NonUtcPairedTimestamp_ThrowsInvalidDataException()
    {
        DateTime unspecified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var invalidRecord = new TrustRecord(ClientId.NewId(), "12345", null, KnownDeviceState.Revoked, string.Empty, new DateTimeOffset(unspecified, TimeSpan.FromHours(2)));
        await WriteEncryptedRecordsAsync([invalidRecord]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that a default blocked timestamp is rejected.</summary>
    [Fact]
    public async Task LoadAsync_DefaultBlockedTimestamp_ThrowsInvalidDataException()
    {
        var invalidRecord = new TrustRecord(ClientId.NewId(), "12345", null, KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.MinValue);
        await WriteEncryptedRecordsAsync([invalidRecord]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
    }

    /// <summary>Verifies that verifier and block-timestamp combinations are valid only for their state.</summary>
    [Fact]
    public async Task LoadAsync_InvalidStateVerifierAndTimestampCombinations_ThrowInvalidDataException()
    {
        var invalidRecords = new[]
        {
            new TrustRecord(ClientId.NewId(), "12345", null, KnownDeviceState.Trusted, string.Empty, DateTimeOffset.UtcNow),
            new TrustRecord(ClientId.NewId(), "54321", null, KnownDeviceState.Revoked, new string('a', 64), DateTimeOffset.UtcNow),
            new TrustRecord(ClientId.NewId(), "67890", null, KnownDeviceState.Unpaired, new string('a', 64), DateTimeOffset.UtcNow),
            new TrustRecord(ClientId.NewId(), "09876", null, KnownDeviceState.Blocked, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-1)),
        };

        foreach (TrustRecord invalidRecord in invalidRecords)
        {
            await WriteEncryptedRecordsAsync([invalidRecord]);
            await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsDpapiTrustStorePersistence(filePath).LoadAsync());
        }
    }

    /// <summary>Verifies that saving an empty list round-trips as an empty list, not as "no file saved yet".</summary>
    [Fact]
    public async Task SaveAsync_EmptyList_RoundTripsAsEmpty()
    {
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);

        await persistence.SaveAsync([]);
        IReadOnlyList<TrustRecord> loaded = await persistence.LoadAsync();

        Assert.Empty(loaded);
        Assert.True(File.Exists(filePath));
    }

    /// <summary>Verifies that multiple records round-trip together.</summary>
    [Fact]
    public async Task SaveAsync_MultipleRecords_RoundTripsAll()
    {
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);
        var first = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "54321", "Bedroom Tablet", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);

        await persistence.SaveAsync([first, second]);
        IReadOnlyList<TrustRecord> loaded = await persistence.LoadAsync();

        Assert.Equal([first, second], loaded);
    }

    /// <summary>Verifies that a second save replaces the previous contents rather than appending to them.</summary>
    [Fact]
    public async Task SaveAsync_CalledAgain_ReplacesPreviousContents()
    {
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);
        var first = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "54321", "Bedroom Tablet", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);

        await persistence.SaveAsync([first]);
        await persistence.SaveAsync([second]);
        IReadOnlyList<TrustRecord> loaded = await persistence.LoadAsync();

        TrustRecord loadedRecord = Assert.Single(loaded);
        Assert.Equal(second, loadedRecord);
    }

    /// <summary>Verifies that concurrent persistence instances still leave one complete valid snapshot.</summary>
    [Fact]
    public async Task SaveAsync_ConcurrentInstances_LeaveCompleteSnapshot()
    {
        var firstPersistence = new WindowsDpapiTrustStorePersistence(filePath);
        var secondPersistence = new WindowsDpapiTrustStorePersistence(filePath);
        var first = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "54321", "Bedroom Tablet", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);

        await Task.WhenAll(firstPersistence.SaveAsync([first]), secondPersistence.SaveAsync([second]));

        IReadOnlyList<TrustRecord> loaded = await firstPersistence.LoadAsync();
        Assert.Single(loaded);
        Assert.Contains(loaded[0], new[] { first, second });
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(filePath)!, $"{Path.GetFileName(filePath)}.*.tmp"));
    }

    /// <summary>Verifies that saving creates any missing parent directories rather than failing.</summary>
    [Fact]
    public async Task SaveAsync_MissingParentDirectory_CreatesIt()
    {
        string nestedPath = Path.Combine(Path.GetTempPath(), $"dovahlink-trust-store-test-dir-{Guid.NewGuid():N}", "trust-store.dat");
        var persistence = new WindowsDpapiTrustStorePersistence(nestedPath);
        var record = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);

        try
        {
            await persistence.SaveAsync([record]);

            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            string directory = Path.GetDirectoryName(nestedPath)!;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Verifies that persistence rejects paths that could escape the configured absolute location contract.</summary>
    [Fact]
    public void Constructor_RelativeOrEmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new WindowsDpapiTrustStorePersistence("relative-trust-store.dat"));
        Assert.Throws<ArgumentException>(() => new WindowsDpapiTrustStorePersistence("C:relative-trust-store.dat"));
        Assert.Throws<ArgumentException>(() => new WindowsDpapiTrustStorePersistence(@"\relative-trust-store.dat"));
        Assert.Throws<ArgumentException>(() => new WindowsDpapiTrustStorePersistence(" "));
    }

    /// <summary>Verifies that a failed replacement leaves the existing target intact and cleans its temporary file.</summary>
    [Fact]
    public async Task SaveAsync_ReplacementFails_CleansTemporaryFileAndPreservesTarget()
    {
        string targetDirectory = Path.Combine(Path.GetTempPath(), $"dovahlink-trust-store-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDirectory);
        var persistence = new WindowsDpapiTrustStorePersistence(targetDirectory);

        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() => persistence.SaveAsync([]));

            Assert.True(Directory.Exists(targetDirectory));
            string parent = Path.GetDirectoryName(targetDirectory)!;
            string targetName = Path.GetFileName(targetDirectory);
            Assert.Empty(Directory.GetFiles(parent, $"{targetName}.*.tmp"));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
    }

    /// <summary>Verifies that a locked existing target survives a failed replacement.</summary>
    [Fact]
    public async Task SaveAsync_LockedTarget_PreservesPreviousContents()
    {
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);
        var first = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "54321", "Bedroom Tablet", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);
        await persistence.SaveAsync([first]);

        using (var targetLock = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => persistence.SaveAsync([second]));
        }

        Assert.Equal([first], await persistence.LoadAsync());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(filePath)!, $"{Path.GetFileName(filePath)}.*.tmp"));
    }

    /// <summary>Verifies that cancellation before replacement leaves the previous durable contents unchanged.</summary>
    [Fact]
    public async Task SaveAsync_CanceledReplacement_PreservesPreviousContents()
    {
        var first = new TrustRecord(ClientId.NewId(), "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "54321", "Bedroom Tablet", KnownDeviceState.Revoked, string.Empty, DateTimeOffset.UtcNow);
        using var cancellation = new CancellationTokenSource();
        bool cancelBeforeReplacement = false;
        var persistence = new WindowsDpapiTrustStorePersistence(filePath, () =>
        {
            if (cancelBeforeReplacement)
            {
                cancellation.Cancel();
            }
        });
        await persistence.SaveAsync([first]);
        cancelBeforeReplacement = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => persistence.SaveAsync([second], cancellation.Token));

        Assert.Equal([first], await persistence.LoadAsync());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(filePath)!, $"{Path.GetFileName(filePath)}.*.tmp"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private async Task WriteEncryptedRecordsAsync(IReadOnlyList<TrustRecord> records)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(records);
        byte[] encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, encrypted);
    }
}
