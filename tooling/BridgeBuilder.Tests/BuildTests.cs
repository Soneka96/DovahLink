using System.Diagnostics;
using System.IO.Compression;
using DovahLink.BridgeBuilder.Build;
using DovahLink.BridgeBuilder.Packaging;

namespace DovahLink.BridgeBuilder.Tests;

/// <summary>Verifies command construction, process execution, and bridge builds.</summary>
public sealed class BuildTests
{
    /// <summary>Builds direct CMake commands with ordered arguments and an inherited environment.</summary>
    [Fact]
    public void BuildsStructuredReleaseCommands()
    {
        var environment = new Dictionary<string, string> { ["VCPKG_ROOT"] = @"C:\VS & tools\vcpkg" };

        IReadOnlyList<BuildCommand> commands = BuildCommand.CreateReleaseBuild(
            @"C:\repository & workspace\bridge",
            environment);

        Assert.Collection(
            commands,
            configure =>
            {
                Assert.Equal("cmake", configure.ExecutablePath);
                Assert.Equal(["--preset", "windows-x64-release"], configure.Arguments);
                Assert.Equal(Path.GetFullPath(@"C:\repository & workspace\bridge"), configure.WorkingDirectory);
                Assert.Same(environment, configure.EnvironmentVariables);
            },
            build =>
            {
                Assert.Equal("cmake", build.ExecutablePath);
                Assert.Equal(
                    ["--build", "--preset", "windows-x64-release", "--target", "dovahlink_bridge_plugin"],
                    build.Arguments);
                Assert.Equal(Path.GetFullPath(@"C:\repository & workspace\bridge"), build.WorkingDirectory);
                Assert.Same(environment, build.EnvironmentVariables);
            });
    }

    /// <summary>Builds a direct Papyrus compiler command with named arguments and the compiler's own directory.</summary>
    [Fact]
    public void BuildsStructuredPapyrusCompileCommand()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        PapyrusToolchain toolchain = CreatePapyrusToolchain(temporaryDirectory.Path);
        string scriptPath = Path.Combine(temporaryDirectory.Path, "console-admin", "DovahLinkAdmin.psc");
        string outputDirectory = Path.Combine(temporaryDirectory.Path, "out");

        BuildCommand command = BuildCommand.CreatePapyrusCompile(scriptPath, toolchain, outputDirectory);

        Assert.Equal(toolchain.CompilerPath, command.ExecutablePath);
        Assert.Equal(
            [
                Path.GetFullPath(scriptPath),
                $"-i={Path.GetDirectoryName(Path.GetFullPath(scriptPath))};{toolchain.ImportDirectory}",
                $"-f={toolchain.FlagsFilePath}",
                $"-o={Path.GetFullPath(outputDirectory)}",
            ],
            command.Arguments);
        Assert.Equal(Path.GetDirectoryName(toolchain.CompilerPath), command.WorkingDirectory);
        Assert.Empty(command.EnvironmentVariables);
    }

    /// <summary>Normalizes a non-canonical script and output path into their full, canonical forms.</summary>
    [Fact]
    public void NormalizesTheScriptAndOutputPathsToFullPaths()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        PapyrusToolchain toolchain = CreatePapyrusToolchain(temporaryDirectory.Path);
        string scriptPath = Path.Combine(temporaryDirectory.Path, "console-admin", "..", "console-admin", "DovahLinkAdmin.psc");
        string outputDirectory = Path.Combine(temporaryDirectory.Path, "out", "..", "out");

        BuildCommand command = BuildCommand.CreatePapyrusCompile(scriptPath, toolchain, outputDirectory);

        Assert.DoesNotContain("..", command.Arguments[0]);
        Assert.Equal(Path.GetFullPath(scriptPath), command.Arguments[0]);
        Assert.Equal($"-o={Path.GetFullPath(outputDirectory)}", command.Arguments[3]);
    }

    /// <summary>Imports a validated batch path containing spaces and shell metacharacters as environment data.</summary>
    [Fact]
    public async Task ImportsToolchainPathsWithoutInterpolatingThemIntoTheShellScript()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = CreateToolchain(
            temporaryDirectory.Path,
            "Visual Studio & tools");
        File.WriteAllText(toolchain.VcvarsallPath, "@echo off\nset DOVAHLINK_TEST_ENV=imported\n");
        BuildCommand command = BuildCommand.CreateEnvironmentImport(toolchain);
        BuildCommand restrictedSearchCommand = command with
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["NoDefaultCurrentDirectoryInExePath"] = "1",
            },
        };
        var output = new List<string>();
        var errors = new List<string>();

        int exitCode = await new ProcessCommandRunner().RunAsync(restrictedSearchCommand, output.Add, errors.Add);

        Assert.True(exitCode == 0, string.Join(Environment.NewLine, errors));
        Assert.DoesNotContain(toolchain.VcvarsallPath, command.Arguments);
        Assert.Empty(command.EnvironmentVariables);
        Assert.Equal(Path.GetDirectoryName(toolchain.VcvarsallPath), command.WorkingDirectory);
        IReadOnlyDictionary<string, string> environment = VisualStudioEnvironment.Create(output, toolchain);
        Assert.Equal("imported", environment["DOVAHLINK_TEST_ENV"]);
        Assert.Equal(toolchain.VcpkgRoot, environment["VCPKG_ROOT"]);
    }

    /// <summary>Forwards process output and returns the process exit code.</summary>
    [Fact]
    public async Task ForwardsProcessOutputAndReturnsTheExitCode()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var output = new List<string>();
        var errors = new List<string>();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "echo stdout && echo stderr 1>&2"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());

        int exitCode = await new ProcessCommandRunner().RunAsync(command, output.Add, errors.Add);

        Assert.Equal(0, exitCode);
        Assert.Contains(output, line => line.Trim() == "stdout");
        Assert.Contains(errors, line => line.Trim() == "stderr");
    }

    /// <summary>Applies structured environment values to the child process without altering their contents.</summary>
    [Fact]
    public async Task PassesEnvironmentValuesToTheChildProcess()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        const string expectedValue = "value with spaces & symbols";
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", "set DOVAHLINK_TEST_VALUE"],
            temporaryDirectory.Path,
            new Dictionary<string, string> { ["DOVAHLINK_TEST_VALUE"] = expectedValue });
        var output = new List<string>();

        int exitCode = await new ProcessCommandRunner().RunAsync(command, output.Add, null);

        Assert.Equal(0, exitCode);
        Assert.Equal([$"DOVAHLINK_TEST_VALUE={expectedValue}"], output);
    }

    /// <summary>Passes paths and arguments containing shell metacharacters directly to the executable.</summary>
    [Fact]
    public async Task KeepsShellMetacharactersAsProcessArgumentData()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string workingDirectory = Path.Combine(temporaryDirectory.Path, "working path & data");
        Directory.CreateDirectory(workingDirectory);
        string inputPath = Path.Combine(workingDirectory, "input & data.txt");
        string comparisonPath = Path.Combine(workingDirectory, "comparison & data.txt");
        File.WriteAllText(inputPath, "literal&value");
        File.WriteAllText(comparisonPath, "literal&value");
        string executablePath = Path.Combine(workingDirectory, "compare & tool.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "fc.exe"), executablePath);
        var output = new List<string>();
        var command = new BuildCommand(
            executablePath,
            ["/b", Path.GetFileName(inputPath), Path.GetFileName(comparisonPath)],
            workingDirectory,
            new Dictionary<string, string>());

        int exitCode = await new ProcessCommandRunner().RunAsync(command, output.Add, null);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output);
    }

    /// <summary>Preserves cancellation when process termination loses a race with natural exit.</summary>
    [Fact]
    public async Task CancellationIsNotMaskedByATerminationRace()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/s", "/c", "ping -n 3 127.0.0.1 >nul"],
            temporaryDirectory.Path,
            new Dictionary<string, string>());
        var runner = new ProcessCommandRunner(_ => throw new InvalidOperationException("process already exited"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(command, null, null, cancellation.Token));
    }

    /// <summary>Terminates a real child process tree promptly while preserving cancellation.</summary>
    [Fact]
    public async Task CancellationTerminatesTheProcessTreeAndThrowsOperationCanceledException()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string startedPath = Path.Combine(temporaryDirectory.Path, "child-started.txt");
        string sentinelPath = Path.Combine(temporaryDirectory.Path, "child-sentinel.txt");
        string batchPath = Path.Combine(temporaryDirectory.Path, "child-tree.bat");
        File.WriteAllText(
            batchPath,
            "@echo off\n" +
            "start \"\" /b powershell.exe -NoProfile -Command \"Set-Content -LiteralPath 'child-started.txt' -Value started; " +
            "Start-Sleep -Milliseconds 500; " +
            "Set-Content -LiteralPath 'child-sentinel.txt' -Value orphan\"\n" +
            "ping -n 30 127.0.0.1 >nul\n");
        var command = new BuildCommand(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", $".\\{Path.GetFileName(batchPath)}"],
            temporaryDirectory.Path,
            new Dictionary<string, string> { ["NoDefaultCurrentDirectoryInExePath"] = "1" });
        using var cancellation = new CancellationTokenSource();
        Task<int> runTask = new ProcessCommandRunner().RunAsync(command, null, null, cancellation.Token);
        DateTime startDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!File.Exists(startedPath) && DateTime.UtcNow < startDeadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
        Assert.True(File.Exists(startedPath));
        var elapsed = Stopwatch.StartNew();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await runTask);

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(File.Exists(sentinelPath));
    }

    /// <summary>Parses environment values containing equals signs and replaces inherited vcpkg configuration.</summary>
    [Fact]
    public void CreatesTheCMakeEnvironmentFromVisualStudioOutput()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = CreateToolchain(temporaryDirectory.Path);

        IReadOnlyDictionary<string, string> environment = VisualStudioEnvironment.Create(
            [
                "PATH=C:\\tools",
                "VALUE=left=right",
                "=C:=C:\\ignored-drive-entry",
                "malformed",
                "VCPKG_ROOT=C:\\old",
            ],
            toolchain);

        Assert.Equal(@"C:\tools", environment["PATH"]);
        Assert.Equal("left=right", environment["VALUE"]);
        Assert.Equal(toolchain.VcpkgRoot, environment["VCPKG_ROOT"]);
        Assert.False(environment.ContainsKey(""));
    }

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
        Assert.Equal(["--preset", "windows-x64-release"], runner.Commands[1].Arguments);
        Assert.Equal("cmake", runner.Commands[2].ExecutablePath);
        Assert.Equal(
            ["--build", "--preset", "windows-x64-release", "--target", "dovahlink_bridge_plugin"],
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

    /// <summary>Reports build progress through the output callback.</summary>
    [Fact]
    public async Task ReportsBuildProgressThroughTheOutputCallback()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path, includeAllArtifacts: true);
        var output = new List<string>();
        var coordinator = new BridgeBuildCoordinator(
            new FakeCommandRunner(),
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await coordinator.BuildAsync(
            new BridgeBuildRequest(temporaryDirectory.Path, PackageChannel.Release),
            output.Add);

        Assert.Contains("Building the DovahLink bridge Release target...", output);
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
