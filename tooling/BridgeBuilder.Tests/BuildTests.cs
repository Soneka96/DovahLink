using System.IO.Compression;
using DovahLink.BridgeBuilder.Build;
using DovahLink.BridgeBuilder.Packaging;

namespace DovahLink.BridgeBuilder.Tests;

public sealed class BuildTests
{
    [Fact]
    public void BuildsTheReleaseCommandWithThePinnedToolchainEnvironment()
    {
        string command = BuildCommand.Create(new VisualStudioToolchain(
            @"C:\VS\VC\Auxiliary\Build\vcvarsall.bat",
            @"C:\VS\VC\vcpkg"));

        Assert.Contains("call \"C:\\VS\\VC\\Auxiliary\\Build\\vcvarsall.bat\" x64", command);
        Assert.Contains("set \"VCPKG_ROOT=C:\\VS\\VC\\vcpkg\"", command);
        Assert.Contains("cmake --preset windows-x64-release", command);
        Assert.Contains("cmake --build --preset windows-x64-release --target dovahlink_bridge_plugin", command);
    }

    [Fact]
    public void QuotesToolchainPathsContainingSpaces()
    {
        string command = BuildCommand.Create(new VisualStudioToolchain(
            @"C:\Program Files\Visual Studio\VC\Auxiliary\Build\vcvarsall.bat",
            @"C:\Program Files\Visual Studio\VC\vcpkg"));

        Assert.Contains("call \"C:\\Program Files\\Visual Studio\\VC\\Auxiliary\\Build\\vcvarsall.bat\" x64", command);
        Assert.Contains("set \"VCPKG_ROOT=C:\\Program Files\\Visual Studio\\VC\\vcpkg\"", command);
    }

    [Fact]
    public async Task ForwardsProcessOutputAndReturnsTheExitCode()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var output = new List<string>();

        int exitCode = await new ProcessCommandRunner().RunAsync(
            "echo stdout && echo stderr 1>&2",
            temporaryDirectory.Path,
            output.Add);

        Assert.Equal(0, exitCode);
        Assert.Contains(output, line => line.Trim() == "stdout");
        Assert.Contains(output, line => line.Trim() == "stderr");
    }

    [Fact]
    public async Task ExecutesAQuotedBatchFilePathContainingSpaces()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string batchFile = Path.Combine(temporaryDirectory.Path, "build helper.bat");
        File.WriteAllText(batchFile, "@echo off\necho quoted-path-ok\n");
        var output = new List<string>();

        int exitCode = await new ProcessCommandRunner().RunAsync(
            $"call \"{batchFile}\"",
            temporaryDirectory.Path,
            output.Add);

        Assert.Equal(0, exitCode);
        Assert.Contains(output, line => line.Trim() == "quoted-path-ok");
    }

    [Fact]
    public async Task BuildsAVortexReadyArchiveFromTheReleaseArtifacts()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var runner = new FakeCommandRunner();
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => new VisualStudioToolchain("vcvarsall.bat", "vcpkg"));

        BridgeBuildResult result = await coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release));

        Assert.Equal("DovahLink-Bridge-0.1.0.zip", result.Plan.ArchiveName);
        Assert.True(File.Exists(result.ArchivePath));
        Assert.StartsWith(
            Path.Combine(temporaryDirectory.Path, "tooling", "out"),
            result.ArchivePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(temporaryDirectory.Path, "bridge"), runner.WorkingDirectory);
        Assert.Contains("windows-x64-release", runner.Command);
        Assert.Empty(Directory.GetDirectories(Path.Combine(temporaryDirectory.Path, "tooling", "out"), ".staging-*"));

        using ZipArchive archive = ZipFile.OpenRead(result.ArchivePath);
        Assert.Equal(
            new[]
            {
                "Data/SKSE/Plugins/dovahlink_bridge_plugin.dll",
                "Data/SKSE/Plugins/boost_json-vc143-mt-x64-1_91.dll",
                "Data/SKSE/Plugins/fmt.dll",
                "Data/SKSE/Plugins/spdlog.dll",
            }.OrderBy(path => path),
            archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).OrderBy(path => path));
    }

    [Fact]
    public async Task BuildsBetaArchivesWithTheVersionedBetaName()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => new VisualStudioToolchain("vcvarsall.bat", "vcpkg"));

        BridgeBuildResult result = await coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Beta));

        Assert.Equal("DovahLink-Bridge-0.1.0-beta.zip", result.Plan.ArchiveName);
    }

    [Fact]
    public async Task FailsWithoutCreatingAnArchiveWhenABuildArtifactIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: false);
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => new VisualStudioToolchain("vcvarsall.bat", "vcpkg"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.Empty(Directory.GetFiles(Path.Combine(temporaryDirectory.Path, "tooling", "out"), "*.zip"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(temporaryDirectory.Path, "tooling", "out"), ".staging-*"));
    }

    [Fact]
    public async Task FailsWithoutPackagingWhenTheBuildCommandFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var runner = new FakeCommandRunner { ExitCode = 1 };
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => new VisualStudioToolchain("vcvarsall.bat", "vcpkg"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out")));
    }

    [Fact]
    public async Task ReportsBuildProgressThroughTheOutputCallback()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var output = new List<string>();
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => new VisualStudioToolchain("vcvarsall.bat", "vcpkg"));

        await coordinator.BuildAsync(
            new BridgeBuildRequest(temporaryDirectory.Path, PackageChannel.Release),
            output.Add);

        Assert.Contains("Building the DovahLink bridge Release target...", output);
        Assert.Contains("fake build output", output);
        Assert.Contains(output, line => line.StartsWith("Created ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsARepositoryWithoutTheBridgeManifest()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => new VisualStudioToolchain("vcvarsall.bat", "vcpkg"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new BridgeBuildRequest(temporaryDirectory.Path, PackageChannel.Release)));
    }

    private static void CreateBuildInputs(string repositoryRoot, bool includeAllArtifacts)
    {
        string bridgeRoot = Path.Combine(repositoryRoot, "bridge");
        string buildOutputRoot = Path.Combine(bridgeRoot, "build", "windows-x64-release");
        Directory.CreateDirectory(buildOutputRoot);
        File.WriteAllText(Path.Combine(bridgeRoot, "vcpkg.json"), "{\"version-string\":\"0.1.0\"}");

        string[] artifactNames =
        [
            "dovahlink_bridge_plugin.dll",
            "boost_json-vc143-mt-x64-1_91.dll",
            "fmt.dll",
            "spdlog.dll",
        ];
        foreach (string artifactName in artifactNames.Take(includeAllArtifacts ? artifactNames.Length : artifactNames.Length - 1))
        {
            File.WriteAllText(Path.Combine(buildOutputRoot, artifactName), artifactName);
        }
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        public int ExitCode { get; init; }
        public string? Command { get; private set; }
        public string? WorkingDirectory { get; private set; }

        public Task<int> RunAsync(
            string command,
            string workingDirectory,
            Action<string>? onOutput,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            WorkingDirectory = workingDirectory;
            onOutput?.Invoke("fake build output");
            return Task.FromResult(ExitCode);
        }
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
