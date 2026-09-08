using System.IO.Compression;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Packaging;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies bridge builds performed by <see cref="BridgeBuildCoordinator"/>.</summary>
public sealed class BuildTests
{
    /// <summary>Builds a Vortex-ready archive from all release artifacts.</summary>
    [Fact]
    public async Task BuildsAVortexReadyArchiveFromTheReleaseArtifacts()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var runner = new FakeCommandRunner();
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        BridgeBuildResult result = await coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release));

        Assert.Equal("DovahLink-Bridge-0.1.0.zip", result.Plan.ArchiveName);
        Assert.True(File.Exists(result.ArchivePath));
        Assert.StartsWith(
            Path.Combine(temporaryDirectory.Path, "tooling", "out"),
            result.ArchivePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, runner.Commands.Count);
        Assert.EndsWith("cmd.exe", runner.Commands[0].ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("cmake", runner.Commands[1].ExecutablePath);
        Assert.Equal(["--fresh", "--preset", "windows-x64-release"], runner.Commands[1].Arguments);
        Assert.Equal("cmake", runner.Commands[2].ExecutablePath);
        Assert.Equal(
            ["--build", "--preset", "windows-x64-release", "--target", "dovahlink_adapter_plugin"],
            runner.Commands[2].Arguments);
        PapyrusToolchain papyrusToolchain = CreatePapyrusToolchain(temporaryDirectory.Path);
        Assert.Equal(papyrusToolchain.CompilerPath, runner.Commands[3].ExecutablePath);
        Assert.Equal(
            [
                Path.GetFullPath(Path.Combine(temporaryDirectory.Path, "console-admin", "DovahLinkAdmin.psc")),
                $"-i={Path.GetFullPath(Path.Combine(temporaryDirectory.Path, "console-admin"))};{papyrusToolchain.ImportDirectory}",
                $"-f={papyrusToolchain.FlagsFilePath}",
                $"-o={Path.GetFullPath(Path.Combine(temporaryDirectory.Path, "bridge", "build", "windows-x64-release"))}",
            ],
            runner.Commands[3].Arguments);
        Assert.Equal(Path.GetDirectoryName(papyrusToolchain.CompilerPath), runner.Commands[3].WorkingDirectory);
        Assert.Equal(Path.GetDirectoryName(CreateToolchain(temporaryDirectory.Path).VcvarsallPath), runner.Commands[0].WorkingDirectory);
        Assert.All(
            runner.Commands.Skip(1).Take(2),
            command => Assert.Equal(Path.Combine(temporaryDirectory.Path, "bridge"), command.WorkingDirectory));
        Assert.Equal(runner.Commands[1].EnvironmentVariables, runner.Commands[2].EnvironmentVariables);
        Assert.Equal(CreateToolchain(temporaryDirectory.Path).VcpkgRoot, runner.Commands[1].EnvironmentVariables["VCPKG_ROOT"]);
        Assert.Empty(Directory.GetDirectories(Path.Combine(temporaryDirectory.Path, "tooling", "out"), ".staging-*"));

        using ZipArchive archive = ZipFile.OpenRead(result.ArchivePath);
        Assert.Equal(
            new[]
            {
                "Data/SKSE/Plugins/dovahlink_bridge_plugin.dll",
                "Data/SKSE/Plugins/boost_json-vc143-mt-x64-1_91.dll",
                "Data/SKSE/Plugins/fmt.dll",
                "Data/SKSE/Plugins/spdlog.dll",
                "Data/Scripts/DovahLinkAdmin.pex",
                "Data/SKSE/CustomConsole/dovahlink.yaml",
            }.OrderBy(path => path),
            archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).OrderBy(path => path));
    }

    /// <summary>Uses the versioned beta name when building a beta archive.</summary>
    [Fact]
    public async Task BuildsBetaArchivesWithTheVersionedBetaName()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        BridgeBuildResult result = await coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Beta));

        Assert.Equal("DovahLink-Bridge-0.1.0-beta.zip", result.Plan.ArchiveName);
    }

    /// <summary>Fails without creating an archive when a build artifact is missing.</summary>
    [Fact]
    public async Task FailsWithoutCreatingAnArchiveWhenABuildArtifactIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: false);
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.Empty(Directory.GetFiles(Path.Combine(temporaryDirectory.Path, "tooling", "out"), "*.zip"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(temporaryDirectory.Path, "tooling", "out"), ".staging-*"));
    }

    /// <summary>Fails without packaging when either direct CMake command returns an error.</summary>
    /// <param name="failingInvocation">The configure or build invocation to fail after environment import.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailsWithoutPackagingWhenACMakeCommandFails(int failingInvocation)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var runner = new FakeCommandRunner { FailingInvocation = failingInvocation };
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out")));
        Assert.Equal(failingInvocation + 1, runner.Commands.Count);
    }

    /// <summary>Fails without packaging when the Papyrus compile returns an error.</summary>
    [Fact]
    public async Task FailsWithoutPackagingWhenThePapyrusCompileFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var runner = new FakeCommandRunner { FailingInvocation = 3 };
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out")));
        Assert.Equal(4, runner.Commands.Count);
    }

    /// <summary>
    /// Fails before any build command runs when the console-admin YAML config source is missing.
    /// </summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenTheConsoleAdminYamlIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        File.Delete(Path.Combine(temporaryDirectory.Path, "console-admin", "dovahlink.yaml"));
        var runner = new FakeCommandRunner();
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.Empty(runner.Commands);
        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out")));
    }

    /// <summary>
    /// Fails before any build command runs when the console-admin Papyrus script source is missing.
    /// </summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenTheConsoleAdminScriptIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        File.Delete(Path.Combine(temporaryDirectory.Path, "console-admin", "DovahLinkAdmin.psc"));
        var runner = new FakeCommandRunner();
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.Empty(runner.Commands);
        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out")));
    }

    /// <summary>Fails before any build command runs when the Papyrus compiler cannot be found.</summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenThePapyrusCompilerIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        PapyrusToolchain papyrusToolchain = CreatePapyrusToolchain(temporaryDirectory.Path);
        File.Delete(papyrusToolchain.CompilerPath);
        var runner = new FakeCommandRunner();
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => papyrusToolchain);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.Empty(runner.Commands);
        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out")));
    }

    /// <summary>Fails before any build command runs when the Visual Studio toolchain cannot be found.</summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenTheVisualStudioToolchainIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        VisualStudioToolchain toolchain = CreateToolchain(temporaryDirectory.Path);
        File.Delete(toolchain.VcvarsallPath);
        var runner = new FakeCommandRunner();
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => toolchain,
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.Empty(runner.Commands);
        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out")));
    }

    /// <summary>Fails before CMake when Visual Studio environment initialization returns an error.</summary>
    [Fact]
    public async Task FailsBeforeCMakeWhenEnvironmentInitializationFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var runner = new FakeCommandRunner { FailingInvocation = 0 };
        var coordinator = new BridgeBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(new BridgeBuildRequest(
            temporaryDirectory.Path,
            PackageChannel.Release)));

        Assert.Single(runner.Commands);
        Assert.EndsWith("cmd.exe", runner.Commands[0].ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out")));
    }

    /// <summary>Reports the Release binary configuration and package channel through the output callback.</summary>
    [Theory]
    [InlineData(PackageChannel.Beta, "Beta")]
    [InlineData(PackageChannel.Release, "Release")]
    public async Task ReportsBuildProgressThroughTheOutputCallback(
        PackageChannel channel,
        string channelLabel)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var output = new List<string>();
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        BridgeBuildResult result = await coordinator.BuildAsync(
            new BridgeBuildRequest(temporaryDirectory.Path, channel),
            output.Add);

        Assert.Equal(channel, result.Plan.Channel);
        Assert.Contains(
            $"Building the DovahLink bridge Release binary for the {channelLabel} package...",
            output);
        Assert.Contains("Compiling the DovahLink admin console script...", output);
        Assert.Contains("fake build output", output);
        Assert.Contains(output, line => line.StartsWith("Created ", StringComparison.Ordinal));
    }

    /// <summary>Rejects a repository that does not contain the bridge manifest.</summary>
    [Fact]
    public async Task RejectsARepositoryWithoutTheBridgeManifest()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = CreateToolchain(temporaryDirectory.Path);
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => toolchain,
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new BridgeBuildRequest(temporaryDirectory.Path, PackageChannel.Release)));
    }

    /// <summary>Creates the repository files required by the build coordinator tests.</summary>
    /// <param name="repositoryRoot">The temporary repository root.</param>
    /// <param name="includeAllArtifacts">Whether to create every expected build artifact.</param>
    private static void CreateBuildInputs(string repositoryRoot, bool includeAllArtifacts)
    {
        string bridgeRoot = Path.Combine(repositoryRoot, "bridge");
        string buildOutputRoot = Path.Combine(bridgeRoot, "build", "windows-x64-release");
        Directory.CreateDirectory(buildOutputRoot);
        File.WriteAllText(Path.Combine(bridgeRoot, "vcpkg.json"), "{\"version-string\":\"0.1.0\"}");

        string consoleAdminRoot = Path.Combine(repositoryRoot, "console-admin");
        Directory.CreateDirectory(consoleAdminRoot);
        File.WriteAllText(Path.Combine(consoleAdminRoot, "DovahLinkAdmin.psc"), "Scriptname DovahLinkAdmin Hidden");
        File.WriteAllText(Path.Combine(consoleAdminRoot, "dovahlink.yaml"), "name: dovahlink");

        string[] artifactNames =
        [
            "dovahlink_bridge_plugin.dll",
            "boost_json-vc143-mt-x64-1_91.dll",
            "fmt.dll",
            "spdlog.dll",
            "DovahLinkAdmin.pex",
        ];
        foreach (string artifactName in artifactNames.Take(includeAllArtifacts ? artifactNames.Length : artifactNames.Length - 1))
        {
            File.WriteAllText(Path.Combine(buildOutputRoot, artifactName), artifactName);
        }
    }

    /// <summary>Creates the validated toolchain files used by coordinator and environment tests.</summary>
    /// <param name="repositoryRoot">The temporary root under which to create the installation.</param>
    /// <param name="installationName">The installation directory name, including any path characters under test.</param>
    /// <returns>The created environment-script and vcpkg paths.</returns>
    private static VisualStudioToolchain CreateToolchain(
        string repositoryRoot,
        string installationName = "Visual Studio")
    {
        string installationRoot = Path.Combine(repositoryRoot, installationName);
        string vcvarsallPath = Path.Combine(installationRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(installationRoot, "VC", "vcpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off\n");
        return new VisualStudioToolchain(vcvarsallPath, vcpkgRoot);
    }

    /// <summary>Creates the validated Papyrus toolchain files used by compile-command tests.</summary>
    /// <param name="repositoryRoot">The temporary root under which to create the installation.</param>
    /// <returns>The created compiler, import directory, and flags file paths.</returns>
    private static PapyrusToolchain CreatePapyrusToolchain(string repositoryRoot)
    {
        string installationRoot = Path.Combine(repositoryRoot, "Skyrim Special Edition");
        string compilerPath = Path.Combine(installationRoot, "Papyrus Compiler", "PapyrusCompiler.exe");
        string importDirectory = Path.Combine(installationRoot, "Data", "Scripts", "Source");
        string flagsFilePath = Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg");
        Directory.CreateDirectory(Path.GetDirectoryName(compilerPath)!);
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(compilerPath, "compiler");
        File.WriteAllText(flagsFilePath, "flags");
        return new PapyrusToolchain(compilerPath, importDirectory, flagsFilePath);
    }

    /// <summary>Records command-runner inputs and returns a configured exit code.</summary>
    private sealed class FakeCommandRunner : ICommandRunner
    {
        /// <summary>Gets the zero-based invocation that returns failure, or minus one for success.</summary>
        public int FailingInvocation { get; init; } = -1;

        /// <summary>Gets the ordered commands supplied to the runner.</summary>
        public List<BuildCommand> Commands { get; } = [];

        /// <summary>Records the command invocation and emits fake build output.</summary>
        public Task<int> RunAsync(
            BuildCommand command,
            Action<string>? onStandardOutput,
            Action<string>? onStandardError,
            CancellationToken cancellationToken = default)
        {
            int invocation = Commands.Count;
            Commands.Add(command);
            if (invocation == 0)
            {
                onStandardOutput?.Invoke(@"PATH=C:\Visual Studio\bin");
                onStandardOutput?.Invoke("VSCMD_ARG_TGT_ARCH=x64");
            }
            else
            {
                onStandardOutput?.Invoke("fake build output");
            }
            return Task.FromResult(invocation == FailingInvocation ? 1 : 0);
        }
    }

    /// <summary>Creates and removes an isolated temporary directory for a test.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>Creates a unique temporary directory.</summary>
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DovahLinkBuilderTests",
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
