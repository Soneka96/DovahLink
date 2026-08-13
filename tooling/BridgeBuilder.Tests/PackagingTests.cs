using DovahLink.BridgeBuilder.Packaging;

namespace DovahLink.BridgeBuilder.Tests;

/// <summary>Verifies bridge version parsing and artifact planning.</summary>
public sealed class PackagingTests
{
    /// <summary>Reads a valid bridge version from a vcpkg manifest.</summary>
    [Fact]
    public void ReadsTheBridgeVersionFromTheVcpkgManifest()
    {
        BridgeVersion version = BridgeVersion.FromVcpkgManifest("{\"version-string\":\"0.1.0\"}");

        Assert.Equal(new BridgeVersion(0, 1, 0), version);
        Assert.Equal("0.1.0", version.ToString());
    }

    /// <summary>Rejects manifest versions outside the supported format.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"version-string\":\"0.1\"}")]
    [InlineData("{\"version-string\":\"0.1.0-preview\"}")]
    [InlineData("{\"version-string\":\"-1.1.0\"}")]
    public void RejectsUnsupportedBridgeVersions(string manifest)
    {
        Assert.Throws<FormatException>(() => BridgeVersion.FromVcpkgManifest(manifest));
    }

    /// <summary>Rejects malformed manifest JSON.</summary>
    [Fact]
    public void RejectsMalformedManifestJson()
    {
        Assert.Throws<FormatException>(() => BridgeVersion.FromVcpkgManifest("not json"));
    }

    /// <summary>Rejects a version property whose value is not a string.</summary>
    [Fact]
    public void RejectsANonStringVersionProperty()
    {
        Assert.Throws<FormatException>(() => BridgeVersion.FromVcpkgManifest("{\"version-string\":1}"));
    }

    /// <summary>Creates a beta archive name and Vortex-compatible file layout.</summary>
    [Fact]
    public void CreatesTheBetaArtifactNameAndVortexLayout()
    {
        ArtifactPlan plan = ArtifactPlan.Create(new BridgeVersion(0, 1, 0), PackageChannel.Beta);

        Assert.Equal("DovahLink-Bridge-0.1.0-beta.zip", plan.ArchiveName);
        Assert.Equal(
            [
                "dovahlink_bridge_plugin.dll",
                "boost_json-vc143-mt-x64-1_91.dll",
                "fmt.dll",
                "spdlog.dll",
            ],
            plan.Files.Select(file => file.SourceName));
        Assert.Equal(
            [
                Path.Combine("Data", "SKSE", "Plugins", "dovahlink_bridge_plugin.dll"),
                Path.Combine("Data", "SKSE", "Plugins", "boost_json-vc143-mt-x64-1_91.dll"),
                Path.Combine("Data", "SKSE", "Plugins", "fmt.dll"),
                Path.Combine("Data", "SKSE", "Plugins", "spdlog.dll"),
            ],
            plan.Files.Select(file => file.ArchivePath));
    }

    /// <summary>Creates a release archive name without the beta suffix.</summary>
    [Fact]
    public void CreatesTheReleaseArtifactNameWithoutTheBetaSuffix()
    {
        ArtifactPlan plan = ArtifactPlan.Create(new BridgeVersion(0, 1, 0), PackageChannel.Release);

        Assert.Equal("DovahLink-Bridge-0.1.0.zip", plan.ArchiveName);
        Assert.All(plan.Files, file => Assert.Equal(Path.GetFileName(file.ArchivePath), file.SourceName));
    }

    /// <summary>Rejects package channel values outside the defined enum members.</summary>
    [Fact]
    public void RejectsUnknownPackageChannels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ArtifactPlan.Create(new BridgeVersion(0, 1, 0), (PackageChannel)99));
    }
}
