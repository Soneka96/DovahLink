using System.Security.Cryptography;
using System.Text.Json;

namespace DovahLink.Host.Trust;

/// <summary>Loads and saves the complete set of trust records the host currently knows about.</summary>
public interface ITrustStorePersistence
{
    /// <summary>Loads every currently persisted trust record.</summary>
    /// <param name="cancellationToken">The token used to cancel the load.</param>
    /// <returns>The persisted records, or an empty list if none have been saved yet.</returns>
    Task<IReadOnlyList<TrustRecord>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the complete persisted set of trust records.</summary>
    /// <param name="records">The complete set of records to persist.</param>
    /// <param name="cancellationToken">The token used to cancel the save.</param>
    Task SaveAsync(IReadOnlyList<TrustRecord> records, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists trust records to a single per-Windows-user file, protected with DPAPI's
/// <see cref="DataProtectionScope.CurrentUser"/> scope so the file is unreadable outside the
/// owning Windows user profile. Survives a host restart, matching
/// <c>ai/context/host/architecture.md</c>'s "Restart behavior".
/// </summary>
public sealed class WindowsDpapiTrustStorePersistence : ITrustStorePersistence
{
    /// <summary>The file the trust store is read from and written to.</summary>
    private readonly string filePath;

    /// <summary>Creates a persistence adapter backed by the repository's default trust-store file path.</summary>
    public WindowsDpapiTrustStorePersistence()
        : this(Constants.TrustStoreFilePath)
    {
    }

    /// <summary>Creates a persistence adapter backed by an explicit file path.</summary>
    /// <param name="filePath">The file the trust store is read from and written to.</param>
    public WindowsDpapiTrustStorePersistence(string filePath)
    {
        this.filePath = filePath;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidDataException">The persisted file exists but could not be decrypted or parsed.</exception>
    public async Task<IReadOnlyList<TrustRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<TrustRecord>();
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        byte[] jsonBytes;
        try
        {
            jsonBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException($"The trust store at '{filePath}' could not be decrypted.", ex);
        }

        List<TrustRecord>? records;
        try
        {
            records = JsonSerializer.Deserialize<List<TrustRecord>>(jsonBytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The trust store at '{filePath}' could not be parsed.", ex);
        }

        return records ?? [];
    }

    /// <inheritdoc/>
    public async Task SaveAsync(IReadOnlyList<TrustRecord> records, CancellationToken cancellationToken = default)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(records);
        byte[] protectedBytes = ProtectedData.Protect(jsonBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(filePath, protectedBytes, cancellationToken);
    }
}
