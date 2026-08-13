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
