using System.Security.Cryptography;
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
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

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
        var first = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "CD34", "Bedroom Tablet", KnownDeviceState.Revoked, "beefdead", DateTimeOffset.UtcNow);

        await persistence.SaveAsync([first, second]);
        IReadOnlyList<TrustRecord> loaded = await persistence.LoadAsync();

        Assert.Equal([first, second], loaded);
    }

    /// <summary>Verifies that a second save replaces the previous contents rather than appending to them.</summary>
    [Fact]
    public async Task SaveAsync_CalledAgain_ReplacesPreviousContents()
    {
        var persistence = new WindowsDpapiTrustStorePersistence(filePath);
        var first = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);
        var second = new TrustRecord(ClientId.NewId(), "CD34", "Bedroom Tablet", KnownDeviceState.Revoked, "beefdead", DateTimeOffset.UtcNow);

        await persistence.SaveAsync([first]);
        await persistence.SaveAsync([second]);
        IReadOnlyList<TrustRecord> loaded = await persistence.LoadAsync();

        TrustRecord loadedRecord = Assert.Single(loaded);
        Assert.Equal(second, loadedRecord);
    }

    /// <summary>Verifies that saving creates any missing parent directories rather than failing.</summary>
    [Fact]
    public async Task SaveAsync_MissingParentDirectory_CreatesIt()
    {
        string nestedPath = Path.Combine(Path.GetTempPath(), $"dovahlink-trust-store-test-dir-{Guid.NewGuid():N}", "trust-store.dat");
        var persistence = new WindowsDpapiTrustStorePersistence(nestedPath);
        var record = new TrustRecord(ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
