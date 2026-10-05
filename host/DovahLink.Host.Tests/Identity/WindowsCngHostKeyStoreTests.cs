using System.Security.Cryptography;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>
/// Tests the persistent Host identity key against the real per-user Windows software key storage
/// provider. Every test uses a unique key-name prefix and record directory and deletes every key it
/// may have created.
/// </summary>
public sealed class WindowsCngHostKeyStoreTests : IDisposable
{
    /// <summary>The provider the store under test uses.</summary>
    private static readonly CngProvider Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;

    /// <summary>The isolated public-key record directory.</summary>
    private readonly string recordDirectory = Path.Combine(Path.GetTempPath(), $"dovahlink-host-key-{Guid.NewGuid():N}");

    /// <summary>The isolated persisted key-name prefix.</summary>
    private readonly string keyNamePrefix = $"DovahLink.Host.Tests.Identity.{Guid.NewGuid():N}.";

    /// <summary>The first Host ID used by a test.</summary>
    private readonly HostId hostId = HostId.NewId();

    /// <summary>A second Host ID, standing for the identity after a deliberate reset.</summary>
    private readonly HostId resetHostId = HostId.NewId();

    /// <summary>Verifies first use creates a non-exportable ECDSA P-256 signing key and records its exact public key.</summary>
    [Fact]
    public void LoadOrProvision_FirstUse_ProvisionsNonExportableKeyAndRecordsSpki()
    {
        HostKeyLoadResult result = CreateStore().LoadOrProvision(hostId);

        Assert.Equal(HostKeyStatus.Provisioned, result.Status);
        Assert.Equal(91, result.PublicKey!.SubjectPublicKeyInfo.Length);
        Assert.Equal(result.PublicKey.SubjectPublicKeyInfo.ToArray(), File.ReadAllBytes(RecordPath(hostId)));
        using CngKey key = CngKey.Open(KeyName(hostId), Provider);
        Assert.Equal(CngAlgorithm.ECDsaP256, key.Algorithm);
        Assert.Equal(CngExportPolicies.None, key.ExportPolicy);
        Assert.Equal(CngKeyUsages.Signing, key.KeyUsage);
        Assert.Empty(Directory.GetFiles(recordDirectory, "*.tmp"));
    }

    /// <summary>Verifies a restarted Host (a new store instance) loads the same key instead of creating another.</summary>
    [Fact]
    public void LoadOrProvision_AfterRestart_LoadsSameKey()
    {
        P256PublicKey first = CreateStore().LoadOrProvision(hostId).PublicKey!;

        HostKeyLoadResult restarted = CreateStore().LoadOrProvision(hostId);

        Assert.Equal(HostKeyStatus.Loaded, restarted.Status);
        Assert.Equal(first, restarted.PublicKey);
    }

    /// <summary>
    /// Verifies a separate OS process opens the same persisted key, sees the same public point, and is
    /// refused private-key export.
    /// </summary>
    [Fact]
    public void LoadOrProvision_SeparateProcess_SeesSamePublicKeyAndCannotExportPrivateKey()
    {
        P256PublicKey publicKey = CreateStore().LoadOrProvision(hostId).PublicKey!;

        string[] output = RunPowerShell(
            "$k = [System.Security.Cryptography.CngKey]::Open($args[0], [System.Security.Cryptography.CngProvider]::MicrosoftSoftwareKeyStorageProvider);" +
            "$b = $k.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob);" +
            "[Console]::Out.WriteLine([BitConverter]::ToString($b, 8).Replace('-', ''));" +
            "try { $null = $k.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob); [Console]::Out.WriteLine('EXPORTED') }" +
            "catch { [Console]::Out.WriteLine('REFUSED') }",
            KeyName(hostId));

        // The ECC public blob is an 8-byte header followed by X and Y, the same point the SPKI ends with.
        Assert.Equal(Convert.ToHexString(publicKey.SubjectPublicKeyInfo[27..]), output[0]);
        Assert.Equal("REFUSED", output[1]);
    }

    /// <summary>Verifies every ordinary private-key export path is refused by the provider's export policy.</summary>
    [Fact]
    public void LoadOrProvision_ProvisionedKey_RefusesPrivateExport()
    {
        CreateStore().LoadOrProvision(hostId);
        using CngKey key = CngKey.Open(KeyName(hostId), Provider);
        using var ecdsa = new ECDsaCng(key);

        Assert.ThrowsAny<CryptographicException>(() => key.Export(CngKeyBlobFormat.EccPrivateBlob));
        Assert.ThrowsAny<CryptographicException>(() => key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob));
        Assert.ThrowsAny<CryptographicException>(() => ecdsa.ExportParameters(includePrivateParameters: true));
        Assert.ThrowsAny<CryptographicException>(() => ecdsa.ExportECPrivateKey());
        Assert.ThrowsAny<CryptographicException>(() => ecdsa.ExportPkcs8PrivateKey());
    }

    /// <summary>Verifies a lost key whose record survives fails closed and is never regenerated.</summary>
    [Fact]
    public void LoadOrProvision_RecordedKeyDeleted_FailsClosedWithoutRegenerating()
    {
        byte[] record = CreateStore().LoadOrProvision(hostId).PublicKey!.SubjectPublicKeyInfo.ToArray();
        DeleteKey(KeyName(hostId));

        HostKeyLoadResult first = CreateStore().LoadOrProvision(hostId);
        HostKeyLoadResult second = CreateStore().LoadOrProvision(hostId);

        Assert.Equal(HostKeyStatus.ProvisionedKeyMissing, first.Status);
        Assert.Equal(HostKeyStatus.ProvisionedKeyMissing, second.Status);
        Assert.Null(first.PublicKey);
        Assert.False(CngKey.Exists(KeyName(hostId), Provider));
        Assert.Equal(record, File.ReadAllBytes(RecordPath(hostId)));
    }

    /// <summary>Verifies a key that exists before its record (an interrupted provisioning) is adopted and recorded.</summary>
    [Fact]
    public void LoadOrProvision_KeyWithoutRecord_LoadsAndRecordsIt()
    {
        byte[] spki = CreateKey(KeyName(hostId), CngExportPolicies.None, CngAlgorithm.ECDsaP256);

        HostKeyLoadResult result = CreateStore().LoadOrProvision(hostId);

        Assert.Equal(HostKeyStatus.Loaded, result.Status);
        Assert.Equal(spki, result.PublicKey!.SubjectPublicKeyInfo.ToArray());
        Assert.Equal(spki, File.ReadAllBytes(RecordPath(hostId)));
    }

    /// <summary>Verifies a record that is not one canonical SubjectPublicKeyInfo fails closed and is left untouched.</summary>
    /// <param name="recordHex">The corrupt record content.</param>
    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("3039301306072a8648ce3d020106082a8648ce3d03010703220002fdf05d25acd08029eabaf4dbafefda88f9df6acc278a88cff9d67934b71e15cb")]
    [InlineData("3059301306072a8648ce3d020106082a8648ce3d03010703420004f2422a662eb6e5065e3ea5587ed92dd959deff9b9e4115bb76dcb02abf07144a68e354b81cc01714608a8ecd61f8d9ac453cda8b0d20056623db09859432498b")]
    public void LoadOrProvision_CorruptRecord_FailsClosedWithoutTouchingIt(string recordHex)
    {
        byte[] corrupt = Convert.FromHexString(recordHex);
        WriteRecord(hostId, corrupt);

        HostKeyLoadResult result = CreateStore().LoadOrProvision(hostId);

        Assert.Equal(HostKeyStatus.RecordCorrupt, result.Status);
        Assert.False(CngKey.Exists(KeyName(hostId), Provider));
        Assert.Equal(corrupt, File.ReadAllBytes(RecordPath(hostId)));
    }

    /// <summary>Verifies a key whose public half differs from its record fails closed.</summary>
    [Fact]
    public void LoadOrProvision_RecordForDifferentKey_FailsClosed()
    {
        CreateStore().LoadOrProvision(hostId);
        byte[] otherKey = Fixtures.BuildP256SubjectPublicKeyInfo();
        WriteRecord(hostId, otherKey);

        HostKeyLoadResult result = CreateStore().LoadOrProvision(hostId);

        Assert.Equal(HostKeyStatus.PublicKeyMismatch, result.Status);
        Assert.Equal(otherKey, File.ReadAllBytes(RecordPath(hostId)));
    }

    /// <summary>Verifies an exportable key or a key on another curve under the Host key's name is refused.</summary>
    /// <param name="exportable">Whether to plant an exportable P-256 key instead of a non-exportable P-384 key.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadOrProvision_KeyViolatingPolicy_FailsClosed(bool exportable)
    {
        CreateKey(
            KeyName(hostId),
            exportable ? CngExportPolicies.AllowPlaintextExport : CngExportPolicies.None,
            exportable ? CngAlgorithm.ECDsaP256 : CngAlgorithm.ECDsaP384);

        HostKeyLoadResult result = CreateStore().LoadOrProvision(hostId);

        Assert.Equal(HostKeyStatus.KeyPolicyInvalid, result.Status);
        Assert.False(File.Exists(RecordPath(hostId)));
    }

    /// <summary>Verifies a deliberate identity reset (a new Host ID) retires the old key and record together.</summary>
    [Fact]
    public void LoadOrProvision_NewHostId_RetiresPreviousKeyAndRecord()
    {
        P256PublicKey previous = CreateStore().LoadOrProvision(hostId).PublicKey!;
        string leftoverTemporary = RecordPath(hostId) + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(leftoverTemporary, [0x01]);

        HostKeyLoadResult reset = CreateStore().LoadOrProvision(resetHostId);

        Assert.Equal(HostKeyStatus.Provisioned, reset.Status);
        Assert.NotEqual(previous, reset.PublicKey);
        Assert.False(CngKey.Exists(KeyName(hostId), Provider));
        Assert.False(File.Exists(RecordPath(hostId)));
        Assert.False(File.Exists(leftoverTemporary));
        Assert.True(File.Exists(RecordPath(resetHostId)));
    }

    /// <summary>Verifies files this store does not write are never mistaken for stale records.</summary>
    [Fact]
    public void LoadOrProvision_UnrelatedFiles_AreLeftAlone()
    {
        Directory.CreateDirectory(recordDirectory);
        string[] unrelated =
        [
            Path.Combine(recordDirectory, "host-id.dat"),
            Path.Combine(recordDirectory, "host-key-not-a-host-id.spki"),
            Path.Combine(recordDirectory, $"host-key-{hostId.Value.ToString("D").ToUpperInvariant()}.spki"),
            Path.Combine(recordDirectory, $"host-key-{hostId}.spki.backup"),
        ];
        foreach (string path in unrelated)
        {
            File.WriteAllBytes(path, [0x01]);
        }

        Assert.Equal(HostKeyStatus.Provisioned, CreateStore().LoadOrProvision(resetHostId).Status);
        Assert.All(unrelated, path => Assert.True(File.Exists(path)));
    }

    /// <summary>
    /// Verifies a stale identity that cannot be retired blocks the new identity's key, and that retiring
    /// succeeds once the obstruction is gone.
    /// </summary>
    [Fact]
    public void LoadOrProvision_StaleRecordCannotBeDeleted_FailsClosedThenRecovers()
    {
        CreateStore().LoadOrProvision(hostId);

        HostKeyLoadResult blocked;
        using (new FileStream(RecordPath(hostId), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            blocked = CreateStore().LoadOrProvision(resetHostId);
        }

        HostKeyLoadResult recovered = CreateStore().LoadOrProvision(resetHostId);

        Assert.Equal(HostKeyStatus.StaleIdentityRetirementFailed, blocked.Status);
        Assert.Equal(HostKeyStatus.Provisioned, recovered.Status);
        Assert.False(File.Exists(RecordPath(hostId)));
    }

    /// <summary>Verifies a stale record the user cannot delete (read-only) fails retirement closed without creating the new key.</summary>
    [Fact]
    public void LoadOrProvision_StaleRecordReadOnly_FailsClosedWithoutNewKey()
    {
        CreateStore().LoadOrProvision(hostId);
        File.SetAttributes(RecordPath(hostId), FileAttributes.ReadOnly);

        HostKeyLoadResult result = CreateStore().LoadOrProvision(resetHostId);

        Assert.Equal(HostKeyStatus.StaleIdentityRetirementFailed, result.Status);
        Assert.False(CngKey.Exists(KeyName(resetHostId), Provider));
        Assert.True(File.Exists(RecordPath(hostId)));
    }

    /// <summary>
    /// Verifies the record is still read while another handle with delete access is open on it, as
    /// during an atomic rename of the same record.
    /// </summary>
    [Fact]
    public void LoadOrProvision_RecordOpenWithDeleteAccess_StillLoads()
    {
        P256PublicKey provisioned = CreateStore().LoadOrProvision(hostId).PublicKey!;

        HostKeyLoadResult result;
        using (new FileStream(RecordPath(hostId), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.DeleteOnClose))
        {
            result = CreateStore().LoadOrProvision(hostId);
        }

        Assert.Equal(HostKeyStatus.Loaded, result.Status);
        Assert.Equal(provisioned, result.PublicKey);
    }

    /// <summary>
    /// Verifies a record that cannot be written propagates the I/O failure, and that the created key is
    /// recorded by the next load instead of being replaced.
    /// </summary>
    [Fact]
    public void LoadOrProvision_RecordPathBlocked_ThrowsThenRecordsSameKeyLater()
    {
        Directory.CreateDirectory(RecordPath(hostId));

        Assert.ThrowsAny<IOException>(() => CreateStore().LoadOrProvision(hostId));
        byte[] created = ExportSpki(KeyName(hostId));
        Directory.Delete(RecordPath(hostId));
        HostKeyLoadResult retried = CreateStore().LoadOrProvision(hostId);

        Assert.Equal(HostKeyStatus.Loaded, retried.Status);
        Assert.Equal(created, retried.PublicKey!.SubjectPublicKeyInfo.ToArray());
        Assert.Equal(created, File.ReadAllBytes(RecordPath(hostId)));
    }

    /// <summary>Verifies concurrent Host processes provisioning one Host ID converge on exactly one key.</summary>
    [Fact]
    public void LoadOrProvision_ConcurrentStores_ConvergeOnOneKey()
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<HostKeyLoadResult>();
        using var start = new Barrier(16);

        // Dedicated threads, not the thread pool: waiting on the key lock blocks each worker, and
        // blocking pool threads would starve unrelated asynchronous tests running in parallel.
        Thread[] workers = Enumerable.Range(0, 16)
            .Select(_ => new Thread(() =>
            {
                start.SignalAndWait();
                results.Add(CreateStore().LoadOrProvision(hostId));
            }))
            .ToArray();
        foreach (Thread worker in workers)
        {
            worker.Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        Assert.All(results, result => Assert.True(result.IsAvailable, result.Status.ToString()));
        Assert.Single(results.Select(result => result.PublicKey).Distinct());
        Assert.Single(results, result => result.Status == HostKeyStatus.Provisioned);
        Assert.Equal(results.First().PublicKey, CreateStore().LoadOrProvision(hostId).PublicKey);
    }

    /// <summary>Verifies the empty Host ID never names a key.</summary>
    [Fact]
    public void LoadOrProvision_DefaultHostId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateStore().LoadOrProvision(default));
    }

    /// <summary>Verifies the store rejects a relative record directory or an empty key-name prefix.</summary>
    /// <param name="directory">The record directory to pass.</param>
    /// <param name="prefix">The key-name prefix to pass.</param>
    [Theory]
    [InlineData("relative-dir", "DovahLink.Host.Tests.")]
    [InlineData("", "DovahLink.Host.Tests.")]
    [InlineData("C:\\absolute", "")]
    public void Constructor_InvalidArguments_Throw(string directory, string prefix)
    {
        Assert.Throws<ArgumentException>(() => new WindowsCngHostKeyStore(directory, prefix, TimeSpan.FromSeconds(1)));
    }

    /// <summary>Verifies a negative lock timeout is rejected.</summary>
    [Fact]
    public void Constructor_NegativeLockTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsCngHostKeyStore(recordDirectory, keyNamePrefix, TimeSpan.FromMilliseconds(-1)));
    }

    /// <summary>Verifies a load fails closed, creating nothing, while another Host process holds the key lock past the timeout.</summary>
    [Fact]
    public void LoadOrProvision_LockHeldByAnotherProcess_TimesOutWithoutCreatingKey()
    {
        Directory.CreateDirectory(recordDirectory);
        HostKeyLoadResult result;
        using (new FileStream(Path.Combine(recordDirectory, "host-key.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            result = new WindowsCngHostKeyStore(recordDirectory, keyNamePrefix, TimeSpan.FromMilliseconds(100)).LoadOrProvision(hostId);
        }

        Assert.Equal(HostKeyStatus.KeyLockTimedOut, result.Status);
        Assert.False(CngKey.Exists(KeyName(hostId), Provider));
        Assert.False(File.Exists(RecordPath(hostId)));
        Assert.Equal(HostKeyStatus.Provisioned, CreateStore().LoadOrProvision(hostId).Status);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        DeleteKey(KeyName(hostId));
        DeleteKey(KeyName(resetHostId));
        if (Directory.Exists(recordDirectory))
        {
            foreach (string path in Directory.GetFiles(recordDirectory))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            Directory.Delete(recordDirectory, recursive: true);
        }
    }

    /// <summary>Runs a Windows PowerShell command in a separate process and returns its output lines.</summary>
    /// <param name="command">The command text, reading its one argument from <c>$args[0]</c>.</param>
    /// <param name="argument">The single argument passed as structured process data.</param>
    /// <returns>The trimmed, non-empty output lines.</returns>
    private static string[] RunPowerShell(string command, string argument)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string item in new[] { "-NoProfile", "-NonInteractive", "-Command", $"& {{ {command} }}", argument })
        {
            startInfo.ArgumentList.Add(item);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "The PowerShell key check did not exit.");
        Assert.True(process.ExitCode == 0, error);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Creates a persisted key directly in the provider, bypassing the store.</summary>
    /// <param name="keyName">The key name.</param>
    /// <param name="exportPolicy">The export policy to create it with.</param>
    /// <param name="algorithm">The key algorithm.</param>
    /// <returns>The key's SubjectPublicKeyInfo.</returns>
    private static byte[] CreateKey(string keyName, CngExportPolicies exportPolicy, CngAlgorithm algorithm)
    {
        var parameters = new CngKeyCreationParameters
        {
            Provider = Provider,
            ExportPolicy = exportPolicy,
            KeyUsage = CngKeyUsages.Signing,
        };
        using CngKey key = CngKey.Create(algorithm, keyName, parameters);
        using var ecdsa = new ECDsaCng(key);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }

    /// <summary>Exports the SubjectPublicKeyInfo of a persisted key.</summary>
    /// <param name="keyName">The key name.</param>
    /// <returns>The key's SubjectPublicKeyInfo.</returns>
    private static byte[] ExportSpki(string keyName)
    {
        using CngKey key = CngKey.Open(keyName, Provider);
        using var ecdsa = new ECDsaCng(key);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }

    /// <summary>Deletes a persisted key if it exists.</summary>
    /// <param name="keyName">The key name.</param>
    private static void DeleteKey(string keyName)
    {
        if (CngKey.Exists(keyName, Provider))
        {
            using CngKey key = CngKey.Open(keyName, Provider);
            key.Delete();
        }
    }

    /// <summary>Creates a store over this test's isolated directory and key names.</summary>
    /// <returns>The store under test.</returns>
    private WindowsCngHostKeyStore CreateStore() => new(recordDirectory, keyNamePrefix, TimeSpan.FromSeconds(10));

    /// <summary>Builds the persisted key name the store uses for a Host ID.</summary>
    /// <param name="id">The Host ID.</param>
    /// <returns>The key name.</returns>
    private string KeyName(HostId id) => keyNamePrefix + id;

    /// <summary>Builds the public-key record path the store uses for a Host ID.</summary>
    /// <param name="id">The Host ID.</param>
    /// <returns>The record path.</returns>
    private string RecordPath(HostId id) => Path.Combine(recordDirectory, $"host-key-{id}.spki");

    /// <summary>Writes a record file directly, bypassing the store.</summary>
    /// <param name="id">The Host ID the record belongs to.</param>
    /// <param name="bytes">The record content.</param>
    private void WriteRecord(HostId id, byte[] bytes)
    {
        Directory.CreateDirectory(recordDirectory);
        File.WriteAllBytes(RecordPath(id), bytes);
    }
}
