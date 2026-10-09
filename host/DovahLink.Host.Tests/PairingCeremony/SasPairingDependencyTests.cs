using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SasPairing;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// Proves the SasPairing package and native library the dormant integration builds against are
/// exactly the artifacts built from the pinned sas-pairing commit, and that the native library loads
/// through the public package API from an explicit absolute path.
/// </summary>
[Collection(SasPairingNativeTestCollection.Name)]
public sealed class SasPairingDependencyTests
{
    /// <summary>Verifies the acquisition record describes exactly the committed pin.</summary>
    [Fact]
    public void AcquisitionRecord_MatchesCommittedPin()
    {
        JsonElement pin = SasPairingTestArtifacts.LoadPin();
        JsonElement record = SasPairingTestArtifacts.LoadRecord();

        foreach (string field in new[] { "repository", "commit", "package_id", "package_version", "platform" })
        {
            Assert.Equal(pin.GetProperty(field).GetString(), record.GetProperty(field).GetString());
        }

        Assert.Equal(1, record.GetProperty("native_abi_version").GetInt32());
        Assert.Matches("^[0-9a-f]{40}$", record.GetProperty("commit").GetString()!);
    }

    /// <summary>
    /// Verifies the SasPairing assembly these tests run against is byte-identical to the one packed
    /// from the pinned commit, so a stale package cache or another build can never stand in for it.
    /// </summary>
    [Fact]
    public void LoadedSasPairingAssembly_IsThePinnedBuild()
    {
        JsonElement record = SasPairingTestArtifacts.LoadRecord();
        Assembly assembly = typeof(SasPairingRuntime).Assembly;

        string loadedHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));

        Assert.Equal(record.GetProperty("assembly_sha256").GetString(), loadedHash);
        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.EndsWith("+" + record.GetProperty("commit").GetString(), informationalVersion);
    }

    /// <summary>
    /// Verifies the acquired native library is the recorded build and loads through the public
    /// package API from its explicit absolute path, then is released again.
    /// </summary>
    [Fact]
    public void AcquiredNativeLibrary_LoadsThroughPublicApi()
    {
        JsonElement record = SasPairingTestArtifacts.LoadRecord();
        string nativeLibrary = SasPairingTestArtifacts.NativeLibraryPath();

        Assert.True(Path.IsPathFullyQualified(nativeLibrary));
        Assert.Equal(
            record.GetProperty("native_library_sha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativeLibrary))));

        using SasPairingRuntime runtime = SasPairingRuntime.Create(nativeLibrary);

        Assert.False(runtime.IsDisposed);
    }
}
