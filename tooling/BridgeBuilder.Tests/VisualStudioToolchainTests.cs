using DovahLink.BridgeBuilder.Build;

namespace DovahLink.BridgeBuilder.Tests;

public sealed class VisualStudioToolchainTests
{
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

    [Fact]
    public void RejectsInstallationsWithoutTheRequiredToolchainFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() =>
            VisualStudioToolchainLocator.Find([temporaryDirectory.Path]));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DovahLinkBridgeBuilderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
