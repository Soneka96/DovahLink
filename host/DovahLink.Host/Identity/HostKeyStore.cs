using System.Diagnostics;
using System.Security.Cryptography;

namespace DovahLink.Host.Identity;

/// <summary>Loads, or creates exactly once, the persistent cryptographic identity key of a Host installation.</summary>
public interface IHostKeyStore
{
    /// <summary>
    /// Retires every earlier Host ID's key and record, then loads this Host ID's key, creating it only
    /// when neither the key nor its public-key record exists. A recorded key that is missing,
    /// inaccessible, policy-invalid, or different fails closed and is never regenerated.
    /// </summary>
    /// <param name="hostId">The current Host installation ID the key belongs to.</param>
    /// <returns>The available key's public half, or the fail-closed reason.</returns>
    /// <exception cref="ArgumentException"><paramref name="hostId"/> is the default, empty ID.</exception>
    /// <exception cref="IOException">The public-key record could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The record directory denies access.</exception>
    HostKeyLoadResult LoadOrProvision(HostId hostId);
}

/// <inheritdoc cref="IHostKeyStore"/>
/// <remarks>
/// The private key is a persisted, per-Windows-user ECDSA P-256 signing key in the Microsoft
/// Software Key Storage Provider, created with export disabled; it is named from the Host ID and never
/// leaves the provider through this type. That export policy is a provider guarantee against ordinary
/// key export, not protection from code already running as the same Windows user. Beside the Host ID
/// file, a public-key record holds the key's exact SubjectPublicKeyInfo and nothing secret; it is
/// what distinguishes a key never provisioned from one that was lost. A deliberate identity reset
/// (a new Host ID) retires the previous ID's key and record together on the next load. Every load
/// holds an exclusive per-user lock file, because the provider does not reliably refuse two processes
/// creating the same key name at once and would otherwise let a second key replace a recorded one.
/// Known limitation: a key whose record write never happened (a crash between the two) cannot be
/// found for retirement after a reset and stays in the provider unused.
/// </remarks>
public sealed class WindowsCngHostKeyStore : IHostKeyStore
{
    /// <summary>The per-user software key storage provider holding every Host identity key.</summary>
    private static readonly CngProvider Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;

    /// <summary>The directory holding the public-key records.</summary>
    private readonly string recordDirectory;

    /// <summary>The persisted key-name prefix the Host ID is appended to.</summary>
    private readonly string keyNamePrefix;

    /// <summary>How long a load waits for another Host process's key lock.</summary>
    private readonly TimeSpan lockTimeout;

    /// <summary>Creates the persisted key when it is absent.</summary>
    private readonly Func<string, bool> tryCreateKey;

    /// <summary>Creates a store for the current Windows user's Host identity directory and key names.</summary>
    public WindowsCngHostKeyStore()
        : this(Path.GetDirectoryName(Constants.HostIdentityFilePath)!, Constants.HostKeyNamePrefix, Constants.HostKeyLockTimeout, TryCreateKey)
    {
    }

    /// <summary>Creates a store over an explicit record directory and key-name prefix for isolated tests.</summary>
    /// <param name="recordDirectory">The absolute directory holding public-key records.</param>
    /// <param name="keyNamePrefix">The non-empty persisted key-name prefix.</param>
    /// <param name="lockTimeout">How long a load waits for another Host process's key lock.</param>
    /// <exception cref="ArgumentException">The directory is not absolute or the prefix is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lockTimeout"/> is negative.</exception>
    internal WindowsCngHostKeyStore(string recordDirectory, string keyNamePrefix, TimeSpan lockTimeout)
        : this(recordDirectory, keyNamePrefix, lockTimeout, TryCreateKey)
    {
    }

    /// <summary>Creates a store over isolated records and a controlled key-creation operation.</summary>
    /// <param name="recordDirectory">The absolute directory holding public-key records.</param>
    /// <param name="keyNamePrefix">The non-empty persisted key-name prefix.</param>
    /// <param name="lockTimeout">How long a load waits for another Host process's key lock.</param>
    /// <param name="tryCreateKey">Creates the persisted key and reports whether this call created it.</param>
    /// <exception cref="ArgumentException">The directory is not absolute or the prefix is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lockTimeout"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tryCreateKey"/> is <see langword="null"/>.</exception>
    internal WindowsCngHostKeyStore(
        string recordDirectory, string keyNamePrefix, TimeSpan lockTimeout, Func<string, bool> tryCreateKey)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lockTimeout, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyNamePrefix);
        ArgumentNullException.ThrowIfNull(tryCreateKey);
        if (!Path.IsPathFullyQualified(recordDirectory))
        {
            throw new ArgumentException("The Host key record directory must be absolute.", nameof(recordDirectory));
        }

        this.recordDirectory = Path.GetFullPath(recordDirectory);
        this.keyNamePrefix = keyNamePrefix;
        this.lockTimeout = lockTimeout;
        this.tryCreateKey = tryCreateKey;
    }

    /// <inheritdoc/>
    public HostKeyLoadResult LoadOrProvision(HostId hostId)
    {
        if (hostId.Value == Guid.Empty)
        {
            throw new ArgumentException("A Host key needs a non-empty Host ID.", nameof(hostId));
        }

        Directory.CreateDirectory(recordDirectory);
        using FileStream? hostKeyLock = TryAcquireLock();
        if (hostKeyLock is null)
        {
            return HostKeyLoadResult.Unavailable(HostKeyStatus.KeyLockTimedOut);
        }

        if (!TryRetireStaleIdentities(hostId))
        {
            return HostKeyLoadResult.Unavailable(HostKeyStatus.StaleIdentityRetirementFailed);
        }

        string keyName = KeyName(hostId);
        string recordPath = RecordPath(hostId);
        P256PublicKey? recorded = null;
        if (File.Exists(recordPath) && !TryReadRecord(recordPath, out recorded))
        {
            return HostKeyLoadResult.Unavailable(HostKeyStatus.RecordCorrupt);
        }

        bool keyExists;
        try
        {
            keyExists = CngKey.Exists(keyName, Provider);
        }
        catch (CryptographicException)
        {
            return HostKeyLoadResult.Unavailable(HostKeyStatus.KeyInaccessible);
        }

        if (recorded is not null)
        {
            if (!keyExists)
            {
                return HostKeyLoadResult.Unavailable(HostKeyStatus.ProvisionedKeyMissing);
            }

            HostKeyStatus? failure = ReadPublicKey(keyName, out P256PublicKey? current);
            if (failure is not null)
            {
                return HostKeyLoadResult.Unavailable(failure.Value);
            }

            return current!.Equals(recorded)
                ? HostKeyLoadResult.Loaded(current)
                : HostKeyLoadResult.Unavailable(HostKeyStatus.PublicKeyMismatch);
        }

        // No record: this Host ID never finished provisioning. Create the key unless a crash happened
        // after creating it but before recording it, then record it.
        bool created;
        try
        {
            created = !keyExists && tryCreateKey(keyName);
        }
        catch (CryptographicException)
        {
            return HostKeyLoadResult.Unavailable(HostKeyStatus.KeyInaccessible);
        }

        HostKeyStatus? readFailure = ReadPublicKey(keyName, out P256PublicKey? publicKey);
        if (readFailure is not null)
        {
            return HostKeyLoadResult.Unavailable(readFailure.Value);
        }

        if (!WriteRecordOrMatchExisting(recordPath, publicKey!))
        {
            return HostKeyLoadResult.Unavailable(HostKeyStatus.PublicKeyMismatch);
        }

        return created ? HostKeyLoadResult.Provisioned(publicKey!) : HostKeyLoadResult.Loaded(publicKey!);
    }

    /// <summary>Creates the persisted non-exportable signing key, tolerating another process creating it first.</summary>
    /// <param name="keyName">The persisted key name.</param>
    /// <returns>Whether this call created the key.</returns>
    private static bool TryCreateKey(string keyName)
    {
        var parameters = new CngKeyCreationParameters
        {
            Provider = Provider,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
            KeyCreationOptions = CngKeyCreationOptions.None,
        };
        try
        {
            using CngKey created = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, parameters);
            return true;
        }
        catch (CryptographicException) when (KeyExists(keyName))
        {
            // Defensive: the key lock already serializes Host processes, but never overwrite a key that exists.
            return false;
        }
    }

    /// <summary>Opens the persisted key, checks its policy, and exports only its public SubjectPublicKeyInfo.</summary>
    /// <param name="keyName">The persisted key name.</param>
    /// <param name="publicKey">The validated public key, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="null"/> on success; otherwise the fail-closed status.</returns>
    private static HostKeyStatus? ReadPublicKey(string keyName, out P256PublicKey? publicKey)
    {
        publicKey = null;
        try
        {
            using CngKey key = CngKey.Open(keyName, Provider);
            if (key.Algorithm != CngAlgorithm.ECDsaP256 || key.ExportPolicy != CngExportPolicies.None)
            {
                return HostKeyStatus.KeyPolicyInvalid;
            }

            using var ecdsa = new ECDsaCng(key);
            return P256PublicKey.TryFromSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo(), out publicKey)
                ? null
                : HostKeyStatus.KeyPolicyInvalid;
        }
        catch (CryptographicException)
        {
            return HostKeyStatus.KeyInaccessible;
        }
    }

    /// <summary>Checks whether a persisted key exists, treating an unreadable provider as absent.</summary>
    /// <param name="keyName">The persisted key name.</param>
    /// <returns>Whether the key exists.</returns>
    private static bool KeyExists(string keyName)
    {
        try
        {
            return CngKey.Exists(keyName, Provider);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Reads a public-key record that must hold exactly one canonical SubjectPublicKeyInfo.</summary>
    /// <param name="recordPath">The record file path.</param>
    /// <param name="publicKey">The recorded key, or <see langword="null"/> when the record is corrupt.</param>
    /// <returns>Whether the record is valid.</returns>
    private static bool TryReadRecord(string recordPath, out P256PublicKey? publicKey)
    {
        publicKey = null;

        // Share delete so a concurrent atomic rename of the same record cannot cause a sharing violation.
        using var stream = new FileStream(
            recordPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length != Constants.P256SubjectPublicKeyInfoLength)
        {
            return false;
        }

        byte[] bytes = new byte[Constants.P256SubjectPublicKeyInfoLength];
        stream.ReadExactly(bytes);
        return P256PublicKey.TryFromSubjectPublicKeyInfo(bytes, out publicKey);
    }

    /// <summary>
    /// Writes the public-key record atomically through a temporary file and rename. If another Host
    /// process wrote it first, the existing record must hold the same key.
    /// </summary>
    /// <param name="recordPath">The record file path.</param>
    /// <param name="publicKey">The key's public half.</param>
    /// <returns>Whether the record now holds exactly <paramref name="publicKey"/>.</returns>
    private bool WriteRecordOrMatchExisting(string recordPath, P256PublicKey publicKey)
    {
        string temporaryPath = $"{recordPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, options: FileOptions.WriteThrough))
            {
                stream.Write(publicKey.SubjectPublicKeyInfo);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, recordPath);
                return true;
            }
            catch (IOException) when (File.Exists(recordPath))
            {
                return TryReadRecord(recordPath, out P256PublicKey? existing) && publicKey.Equals(existing);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// Deletes the key, record, and leftover temporary records of every Host ID other than the current
    /// one, deleting each key before its record so a failure leaves the record to retry from.
    /// </summary>
    /// <param name="current">The current Host ID, whose state is left untouched.</param>
    /// <returns>Whether every stale identity was retired.</returns>
    private bool TryRetireStaleIdentities(HostId current)
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(recordDirectory, $"{Constants.HostKeyRecordFilePrefix}*"))
            {
                if (!TryParseRecordHostId(Path.GetFileName(path), out HostId recordHostId, out bool temporary) ||
                    recordHostId == current)
                {
                    continue;
                }

                if (!temporary)
                {
                    string staleKeyName = KeyName(recordHostId);
                    if (CngKey.Exists(staleKeyName, Provider))
                    {
                        using CngKey stale = CngKey.Open(staleKeyName, Provider);
                        stale.Delete();
                    }
                }

                File.Delete(path);
            }

            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a record or temporary-record file name written by this store. Only the canonical
    /// lowercase Host ID form is recognized, so no other file is ever mistaken for a record.
    /// </summary>
    /// <param name="fileName">The file name without its directory.</param>
    /// <param name="hostId">The Host ID the file belongs to.</param>
    /// <param name="temporary">Whether the file is a leftover temporary record.</param>
    /// <returns>Whether the name is one this store writes.</returns>
    private static bool TryParseRecordHostId(string fileName, out HostId hostId, out bool temporary)
    {
        hostId = default;
        temporary = false;
        const int GuidLength = 36;
        int prefixLength = Constants.HostKeyRecordFilePrefix.Length;
        if (fileName.Length < prefixLength + GuidLength + Constants.HostKeyRecordFileExtension.Length)
        {
            return false;
        }

        string idText = fileName.Substring(prefixLength, GuidLength);
        string rest = fileName[(prefixLength + GuidLength)..];
        if (!Guid.TryParseExact(idText, "D", out Guid value) || value == Guid.Empty ||
            value.ToString("D") != idText || !rest.StartsWith(Constants.HostKeyRecordFileExtension, StringComparison.Ordinal))
        {
            return false;
        }

        string suffix = rest[Constants.HostKeyRecordFileExtension.Length..];
        temporary = suffix.Length > 0;
        if (temporary && !(suffix.StartsWith('.') && suffix.EndsWith(".tmp", StringComparison.Ordinal)))
        {
            return false;
        }

        hostId = new HostId(value);
        return true;
    }

    /// <summary>
    /// Takes the per-user Host key lock, retrying until <see cref="lockTimeout"/> elapses. Holding it
    /// serializes every load, create, record write, and retirement across Host processes.
    /// </summary>
    /// <returns>The open lock file, released by disposing it; or <see langword="null"/> after the timeout.</returns>
    private FileStream? TryAcquireLock()
    {
        string lockPath = Path.Combine(recordDirectory, Constants.HostKeyLockFileName);
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < lockTimeout)
            {
                Thread.Sleep(Constants.HostKeyLockRetryInterval);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    /// <summary>Builds the persisted key name of a Host ID.</summary>
    /// <param name="hostId">The Host ID.</param>
    /// <returns>The key name.</returns>
    private string KeyName(HostId hostId) => keyNamePrefix + hostId.ToString();

    /// <summary>Builds the public-key record path of a Host ID.</summary>
    /// <param name="hostId">The Host ID.</param>
    /// <returns>The record file path.</returns>
    private string RecordPath(HostId hostId) =>
        Path.Combine(recordDirectory, $"{Constants.HostKeyRecordFilePrefix}{hostId}{Constants.HostKeyRecordFileExtension}");
}
