using DovahLink.BridgeBuilder.Build;

namespace DovahLink.BridgeBuilder.Tests;

/// <summary>Verifies Visual Studio toolchain discovery.</summary>
public sealed class VisualStudioToolchainTests
{
    /// <summary>Finds an installation containing the required toolchain files.</summary>
    [Fact]
    public void FindsTheFirstInstallationWithTheRequiredFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string installationRoot = Path.Combine(temporaryDirectory.Path, "Community");
        string vcvarsallPath = Path.Combine(installationRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(installationRoot, "VC", "vcpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off");

        VisualStudioToolchain toolchain = VisualStudioToolchainLocator.Find([installationRoot]);

        Assert.Equal(vcvarsallPath, toolchain.VcvarsallPath);
        Assert.Equal(vcpkgRoot, toolchain.VcpkgRoot);
    }

    /// <summary>Rejects installations missing required toolchain files.</summary>
    [Fact]
    public void RejectsInstallationsWithoutTheRequiredToolchainFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() =>
            VisualStudioToolchainLocator.Find([temporaryDirectory.Path]));
    }

    /// <summary>Rejects a direct toolchain value whose environment script is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingEnvironmentScript()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string vcpkgRoot = Path.Combine(temporaryDirectory.Path, "vcpkg");
        Directory.CreateDirectory(vcpkgRoot);

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(Path.Combine(temporaryDirectory.Path, "missing.bat"), vcpkgRoot)));
    }

    /// <summary>Rejects an existing batch file that is not the supported Visual Studio environment script.</summary>
    [Fact]
    public void ValidateRejectsAnUnexpectedEnvironmentScriptName()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string batchPath = Path.Combine(temporaryDirectory.Path, "other.bat");
        string vcpkgRoot = Path.Combine(temporaryDirectory.Path, "vcpkg");
        File.WriteAllText(batchPath, "@echo off");
        Directory.CreateDirectory(vcpkgRoot);

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(batchPath, vcpkgRoot)));
    }

    /// <summary>Rejects a direct toolchain value whose bundled vcpkg directory is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingVcpkgDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string vcvarsallPath = Path.Combine(temporaryDirectory.Path, "vcvarsall.bat");
        File.WriteAllText(vcvarsallPath, "@echo off");

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(vcvarsallPath, Path.Combine(temporaryDirectory.Path, "missing-vcpkg"))));
    }

    /// <summary>Creates and removes an isolated temporary directory for a test.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>Creates a unique temporary directory.</summary>
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DovahLinkBridgeBuilderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the temporary directory path.</summary>
        public string Path { get; }

        /// <summary>Removes the temporary directory when the test completes.</summary>
        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
