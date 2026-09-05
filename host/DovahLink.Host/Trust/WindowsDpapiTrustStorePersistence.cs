using System.Security.Cryptography;
using System.Text;
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
/// owning Windows user profile. Survives a host restart while keeping the in-memory trust domain
/// separate from the persistence mechanism.
/// </summary>
public sealed class WindowsDpapiTrustStorePersistence : ITrustStorePersistence
{
    /// <summary>The file the trust store is read from and written to.</summary>
    private readonly string filePath;

    /// <summary>Serializes replacement writes across all instances targeting local files.</summary>
    private static readonly SemaphoreSlim saveSemaphore = new(1, 1);

    /// <summary>Test-only callback invoked after durable temp-file flush and before replacement.</summary>
    private readonly Action? beforeReplacement;

    /// <summary>Creates a persistence adapter backed by the repository's default trust-store file path.</summary>
    public WindowsDpapiTrustStorePersistence()
        : this(Constants.TrustStoreFilePath)
    {
    }

    /// <summary>Creates a persistence adapter backed by an explicit file path.</summary>
    /// <param name="filePath">The file the trust store is read from and written to.</param>
    public WindowsDpapiTrustStorePersistence(string filePath)
        : this(filePath, beforeReplacement: null)
    {
    }

    /// <summary>Creates persistence with an internal replacement seam for deterministic cancellation tests.</summary>
    internal WindowsDpapiTrustStorePersistence(string filePath, Action? beforeReplacement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException("The trust-store path must be absolute.", nameof(filePath));
        }

        this.filePath = Path.GetFullPath(filePath);
        this.beforeReplacement = beforeReplacement;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidDataException">The persisted file exists but could not be decrypted or parsed.</exception>
    public async Task<IReadOnlyList<TrustRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        byte[] protectedBytes;
        try
        {
            protectedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return Array.Empty<TrustRecord>();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<TrustRecord>();
        }

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

        if (records is null || records.Any(record => record is null))
        {
            throw new InvalidDataException($"The trust store at '{filePath}' did not contain a record list.");
        }

        ValidateRecords(records);

        return records;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(IReadOnlyList<TrustRecord> records, CancellationToken cancellationToken = default)
    {
        ValidateRecords(records);
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(records);
        byte[] protectedBytes = ProtectedData.Protect(jsonBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("The trust-store path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        await saveSemaphore.WaitAsync(cancellationToken);
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var temporaryFile = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await temporaryFile.WriteAsync(protectedBytes, cancellationToken);
                await temporaryFile.FlushAsync(cancellationToken);
                temporaryFile.Flush(flushToDisk: true);
            }
            beforeReplacement?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(filePath))
            {
                File.Replace(temporaryPath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
        }
        finally
        {
            saveSemaphore.Release();
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Rejects structurally valid JSON that cannot represent a valid trust-store snapshot.</summary>
    private static void ValidateRecords(IReadOnlyList<TrustRecord> records)
    {
        if (records.Select(record => record.ClientId).Distinct().Count() != records.Count)
        {
            throw new InvalidDataException("The trust store contains duplicate client records.");
        }

        if (records.Select(record => record.ShortId).Distinct(StringComparer.Ordinal).Count() != records.Count)
        {
            throw new InvalidDataException("The trust store contains duplicate short IDs.");
        }

        foreach (TrustRecord record in records)
        {
            if (record.ClientId.Value == Guid.Empty ||
                record.Incarnation.Value == Guid.Empty ||
                record.ShortId is null ||
                record.CredentialVerifier is null ||
                record.ShortId.Length != Constants.PairingShortIdDigits ||
                record.ShortId.Any(character => character is < '0' or > '9') ||
                record.PairedAtUtc == default ||
                record.PairedAtUtc.Offset != TimeSpan.Zero ||
                !Enum.IsDefined(record.State))
            {
                throw new InvalidDataException("The trust store contains an invalid device record.");
            }

            if (record.DisplayName is not null &&
                (record.DisplayName.Any(char.IsControl) || Encoding.UTF8.GetByteCount(record.DisplayName) > Constants.MaxDisplayNameLengthBytes))
            {
                throw new InvalidDataException("The trust store contains an invalid display name.");
            }

            bool hasVerifier = !string.IsNullOrEmpty(record.CredentialVerifier);
            bool hasBlockedAt = record.BlockedAtUtc.HasValue;
            if (record.BlockedAtUtc is { } blockedAt && (blockedAt == default ||
                blockedAt.Offset != TimeSpan.Zero ||
                blockedAt < record.PairedAtUtc))
            {
                throw new InvalidDataException("The trust store contains an invalid blocked timestamp.");
            }
            if (record.State == KnownDeviceState.Trusted)
            {
                if (!hasVerifier || record.CredentialVerifier.Length != 64 ||
                    record.CredentialVerifier.Any(character => !Uri.IsHexDigit(character)) || hasBlockedAt)
                {
                    throw new InvalidDataException("The trust store contains an invalid trusted record.");
                }
            }
            else if (hasVerifier || (record.State == KnownDeviceState.Blocked) != hasBlockedAt)
            {
                throw new InvalidDataException("The trust store contains an invalid non-trusted record.");
            }
        }
    }
}
