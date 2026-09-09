using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies Adapter builds and Adapter+Host packaging orchestration performed by <see cref="AdapterHostBuildCoordinator"/>.</summary>
public sealed class AdapterHostBuildCoordinatorTests
{
    /// <summary>Builds the Adapter, compiles Papyrus, and orchestrates the canonical packaging script.</summary>
    [Fact]
    public async Task BuildsPackageByOrchestratingAdapterBuildPapyrusCompileAndPackaging()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        AdapterHostBuildResult result = await coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path));

        Assert.Equal(runner.ArchivePath, result.ArchivePath);
        Assert.Equal(5, runner.Commands.Count);
        Assert.EndsWith("cmd.exe", runner.Commands[0].ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("cmake", runner.Commands[1].ExecutablePath);
        Assert.Equal(["--fresh", "--preset", "windows-x64-release"], runner.Commands[1].Arguments);
        Assert.Equal("cmake", runner.Commands[2].ExecutablePath);
        Assert.Equal(
            ["--build", "--preset", "windows-x64-release", "--target", "dovahlink_adapter_plugin"],
            runner.Commands[2].Arguments);
        Assert.All(
            runner.Commands.Skip(1).Take(2),
            command => Assert.Equal(Path.Combine(temporaryDirectory.Path, "adapter"), command.WorkingDirectory));

        string adapterBuildOutputRoot = Path.Combine(temporaryDirectory.Path, "adapter", "build", "windows-x64-release");
        PapyrusToolchain papyrusToolchain = Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path);
        Assert.Equal(papyrusToolchain.CompilerPath, runner.Commands[3].ExecutablePath);
        Assert.Equal(
            [
                Path.Combine(temporaryDirectory.Path, "console-admin", "DovahLinkAdmin.psc"),
                $"-i={Path.Combine(temporaryDirectory.Path, "console-admin")};{papyrusToolchain.ImportDirectory}",
                $"-f={papyrusToolchain.FlagsFilePath}",
                $"-o={adapterBuildOutputRoot}",
            ],
            runner.Commands[3].Arguments);

        Assert.Equal("python", runner.Commands[4].ExecutablePath);
        Assert.Equal(temporaryDirectory.Path, runner.Commands[4].WorkingDirectory);
        Assert.Equal(
            [
                "tooling/package_adapter_host.py",
                "--adapter-build-dir", adapterBuildOutputRoot,
                "--output-dir", Path.Combine(temporaryDirectory.Path, "tooling", "out"),
                "--console-admin-pex", Path.Combine(adapterBuildOutputRoot, "DovahLinkAdmin.pex"),
                "--console-admin-yaml", Path.Combine(temporaryDirectory.Path, "console-admin", "dovahlink.yaml"),
            ],
            runner.Commands[4].Arguments);
    }

    /// <summary>Fails without invoking the packaging script when either direct CMake command returns an error.</summary>
    /// <param name="failingInvocation">The configure or build invocation to fail after environment import.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailsWithoutPackagingWhenACMakeCommandFails(int failingInvocation)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { FailingInvocation = failingInvocation };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Equal(failingInvocation + 1, runner.Commands.Count);
    }

    /// <summary>Fails without invoking the packaging script when the Papyrus compile returns an error.</summary>
    [Fact]
    public async Task FailsWithoutPackagingWhenThePapyrusCompileFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { FailingInvocation = 3 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Equal(4, runner.Commands.Count);
    }

    /// <summary>Fails when the packaging script exits with a nonzero status.</summary>
    [Fact]
    public async Task FailsWhenThePackagingScriptFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { FailingInvocation = 4 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Contains("packaging failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, runner.Commands.Count);
    }

    /// <summary>Fails when the packaging script succeeds but never reports a written archive path.</summary>
    [Fact]
    public async Task FailsWhenThePackagingScriptDoesNotReportAWrittenArchivePath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { ReportArchivePath = false };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Contains("did not report a written archive path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Uses the last reported archive path when packaging reports more than one.</summary>
    [Fact]
    public async Task UsesTheLastReportedArchivePathWhenPackagingReportsMultipleWrittenPaths()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner
        {
            AdditionalPackagingOutputLines = ["Wrote C:\\stale\\DovahLink-Adapter-0.0.9.zip"],
        };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        AdapterHostBuildResult result = await coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path));

        Assert.Equal(runner.ArchivePath, result.ArchivePath);
    }

    /// <summary>
    /// Fails before any build command runs when the console-admin YAML config source is missing.
    /// </summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenTheConsoleAdminYamlIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        File.Delete(Path.Combine(temporaryDirectory.Path, "console-admin", "dovahlink.yaml"));
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>
    /// Fails before any build command runs when the console-admin Papyrus script source is missing.
    /// </summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenTheConsoleAdminScriptIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        File.Delete(Path.Combine(temporaryDirectory.Path, "console-admin", "DovahLinkAdmin.psc"));
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>Fails before any build command runs when the Papyrus compiler cannot be found.</summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenThePapyrusCompilerIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        PapyrusToolchain papyrusToolchain = Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path);
        File.Delete(papyrusToolchain.CompilerPath);
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => papyrusToolchain);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>Fails before any build command runs when the Visual Studio toolchain cannot be found.</summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenTheVisualStudioToolchainIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path);
        File.Delete(toolchain.VcvarsallPath);
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => toolchain,
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>Fails before CMake when Visual Studio environment initialization returns an error.</summary>
    [Fact]
    public async Task FailsBeforeCMakeWhenEnvironmentInitializationFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { FailingInvocation = 0 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Single(runner.Commands);
        Assert.EndsWith("cmd.exe", runner.Commands[0].ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reports build and packaging progress through the output callback.</summary>
    [Fact]
    public async Task ReportsBuildProgressThroughTheOutputCallback()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var output = new List<string>();
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            output.Add);

        Assert.Contains("Building the DovahLink Adapter Release binary...", output);
        Assert.Contains("Compiling the DovahLink admin console script...", output);
        Assert.Contains("Packaging the Adapter and Host...", output);
        Assert.Contains("fake build output", output);
        Assert.Contains("fake packaging output", output);
        Assert.Contains(output, line => line.StartsWith("Wrote ", StringComparison.Ordinal));
    }

    /// <summary>Rejects a repository that does not contain the adapter manifest.</summary>
    [Fact]
    public async Task RejectsARepositoryWithoutTheAdapterManifest()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "VERSION"), "0.1.0");
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path);
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => toolchain,
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));
    }

    /// <summary>Rejects a repository that does not contain the root VERSION file.</summary>
    [Fact]
    public async Task RejectsARepositoryWithoutTheVersionFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "adapter"));
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "adapter", "vcpkg.json"), "{}");
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path);
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => toolchain,
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));
    }

    /// <summary>Rejects a repository that does not contain the Adapter+Host packaging script.</summary>
    [Fact]
    public async Task RejectsARepositoryWithoutThePackagingScript()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temporaryDirectory.Path, "adapter"));
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "adapter", "vcpkg.json"), "{}");
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "VERSION"), "0.1.0");
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path);
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => toolchain,
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));
    }

    /// <summary>Reports an ordered Running/Succeeded sequence for every stage the coordinator owns directly.</summary>
    [Fact]
    public async Task ReportsStageProgressThroughTheStageCallback()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add);

        Assert.Equal(
            [
                (BuildStage.ValidateRepository, BuildStageStatus.Running),
                (BuildStage.ValidateRepository, BuildStageStatus.Succeeded),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Running),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Succeeded),
                (BuildStage.BuildAdapter, BuildStageStatus.Running),
                (BuildStage.BuildAdapter, BuildStageStatus.Succeeded),
                (BuildStage.CompilePapyrus, BuildStageStatus.Running),
                (BuildStage.CompilePapyrus, BuildStageStatus.Succeeded),
            ],
            stageEvents.Select(stageEvent => (stageEvent.Stage, stageEvent.Status)));
        Assert.All(
            stageEvents.Where(stageEvent => stageEvent.Status != BuildStageStatus.Running),
            stageEvent => Assert.NotNull(stageEvent.Duration));
    }

    /// <summary>Reports only the failed stage, and no later stage, when a build command fails.</summary>
    [Fact]
    public async Task ReportsOnlyTheFailedStageWhenAConfigureCommandFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var runner = new FakeCommandRunner { FailingInvocation = 1 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add));

        Assert.Equal(
            [
                (BuildStage.ValidateRepository, BuildStageStatus.Running),
                (BuildStage.ValidateRepository, BuildStageStatus.Succeeded),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Running),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Failed),
            ],
            stageEvents.Select(stageEvent => (stageEvent.Stage, stageEvent.Status)));
    }

    /// <summary>Reports only the failed stage when the Adapter build command fails.</summary>
    [Fact]
    public async Task ReportsOnlyTheFailedStageWhenTheBuildCommandFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var runner = new FakeCommandRunner { FailingInvocation = 2 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add));

        Assert.Equal(
            [
                (BuildStage.ValidateRepository, BuildStageStatus.Running),
                (BuildStage.ValidateRepository, BuildStageStatus.Succeeded),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Running),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Succeeded),
                (BuildStage.BuildAdapter, BuildStageStatus.Running),
                (BuildStage.BuildAdapter, BuildStageStatus.Failed),
            ],
            stageEvents.Select(stageEvent => (stageEvent.Stage, stageEvent.Status)));
    }

    /// <summary>Reports only the failed stage when the Papyrus compile command fails.</summary>
    [Fact]
    public async Task ReportsOnlyTheFailedStageWhenThePapyrusCompileFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var runner = new FakeCommandRunner { FailingInvocation = 3 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add));

        Assert.Equal(
            [
                (BuildStage.ValidateRepository, BuildStageStatus.Running),
                (BuildStage.ValidateRepository, BuildStageStatus.Succeeded),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Running),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Succeeded),
                (BuildStage.BuildAdapter, BuildStageStatus.Running),
                (BuildStage.BuildAdapter, BuildStageStatus.Succeeded),
                (BuildStage.CompilePapyrus, BuildStageStatus.Running),
                (BuildStage.CompilePapyrus, BuildStageStatus.Failed),
            ],
            stageEvents.Select(stageEvent => (stageEvent.Stage, stageEvent.Status)));
    }

    /// <summary>Reports only the ValidateRepository stage as failed when a required toolchain is missing.</summary>
    [Fact]
    public async Task ReportsOnlyValidateRepositoryFailedWhenAToolchainIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        PapyrusToolchain papyrusToolchain = Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path);
        File.Delete(papyrusToolchain.CompilerPath);
        var stageEvents = new List<BuildStageEvent>();
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => papyrusToolchain);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add));

        Assert.Equal(
            [
                (BuildStage.ValidateRepository, BuildStageStatus.Running),
                (BuildStage.ValidateRepository, BuildStageStatus.Failed),
            ],
            stageEvents.Select(stageEvent => (stageEvent.Stage, stageEvent.Status)));
    }

    /// <summary>Reports an ordered Running/Succeeded sequence for every packaging stage the canonical script marks.</summary>
    [Fact]
    public async Task ReportsPackagingStagesThroughTheStageCallback()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var runner = new FakeCommandRunner
        {
            AdditionalPackagingOutputLines =
            [
                "##stage host_publish start",
                "##stage host_publish done",
                "##stage package_assembly start",
                "##stage package_assembly done",
                "##stage package_validation start",
                "##stage package_validation done",
                "##stage archive start",
                "##stage archive done",
            ],
        };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add);

        IEnumerable<(BuildStage Stage, BuildStageStatus Status)> packagingEvents = stageEvents
            .Where(stageEvent => stageEvent.Stage
                is BuildStage.PublishHost or BuildStage.AssemblePackage or BuildStage.ValidatePackage or BuildStage.CreateZip)
            .Select(stageEvent => (stageEvent.Stage, stageEvent.Status));
        Assert.Equal(
            [
                (BuildStage.PublishHost, BuildStageStatus.Running),
                (BuildStage.PublishHost, BuildStageStatus.Succeeded),
                (BuildStage.AssemblePackage, BuildStageStatus.Running),
                (BuildStage.AssemblePackage, BuildStageStatus.Succeeded),
                (BuildStage.ValidatePackage, BuildStageStatus.Running),
                (BuildStage.ValidatePackage, BuildStageStatus.Succeeded),
                (BuildStage.CreateZip, BuildStageStatus.Running),
                (BuildStage.CreateZip, BuildStageStatus.Succeeded),
            ],
            packagingEvents);
        Assert.All(
            stageEvents.Where(stageEvent =>
                stageEvent.Status == BuildStageStatus.Succeeded &&
                stageEvent.Stage is BuildStage.PublishHost or BuildStage.AssemblePackage or BuildStage.ValidatePackage or BuildStage.CreateZip),
            stageEvent => Assert.NotNull(stageEvent.Duration));
    }

    /// <summary>Reports a packaging stage as failed when it starts but the packaging script exits before it completes.</summary>
    [Fact]
    public async Task ReportsThePackagingStageAsFailedWhenItStartsButNeverCompletes()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var runner = new FakeCommandRunner
        {
            FailingInvocation = 4,
            AdditionalPackagingOutputLines = ["##stage host_publish start"],
        };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add));

        IEnumerable<(BuildStage Stage, BuildStageStatus Status)> packagingEvents = stageEvents
            .Where(stageEvent => stageEvent.Stage
                is BuildStage.PublishHost or BuildStage.AssemblePackage or BuildStage.ValidatePackage or BuildStage.CreateZip)
            .Select(stageEvent => (stageEvent.Stage, stageEvent.Status));
        Assert.Equal(
            [
                (BuildStage.PublishHost, BuildStageStatus.Running),
                (BuildStage.PublishHost, BuildStageStatus.Failed),
            ],
            packagingEvents);
    }

    /// <summary>Records command-runner inputs and returns a configured exit code and packaging output.</summary>
    private sealed class FakeCommandRunner : ICommandRunner
    {
        /// <summary>Gets the zero-based invocation that returns failure, or minus one for success.</summary>
        public int FailingInvocation { get; init; } = -1;

        /// <summary>Gets whether the fake packaging invocation reports a written archive path.</summary>
        public bool ReportArchivePath { get; init; } = true;

        /// <summary>Gets the archive path the fake packaging invocation reports when <see cref="ReportArchivePath"/> is set.</summary>
        public string ArchivePath { get; init; } = Path.Combine("C:", "repo", "tooling", "out", "DovahLink-Adapter-0.1.0.zip");

        /// <summary>Gets extra stdout lines the fake packaging invocation emits before its own archive-path line.</summary>
        public IReadOnlyList<string> AdditionalPackagingOutputLines { get; init; } = [];

        /// <summary>Gets the ordered commands supplied to the runner.</summary>
        public List<BuildCommand> Commands { get; } = [];

        /// <summary>Records the command invocation and emits fake build or packaging output.</summary>
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
            else if (invocation == 4)
            {
                onStandardOutput?.Invoke("fake packaging output");
                foreach (string line in AdditionalPackagingOutputLines)
                {
                    onStandardOutput?.Invoke(line);
                }
                if (ReportArchivePath)
                {
                    onStandardOutput?.Invoke($"Wrote {ArchivePath}");
                }
            }
            else
            {
                onStandardOutput?.Invoke("fake build output");
            }
            return Task.FromResult(invocation == FailingInvocation ? 1 : 0);
        }
    }
}
