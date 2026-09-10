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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
                "--configuration", "Release",
                "--profile-label", "release",
            ],
            runner.Commands[4].Arguments);
    }

    /// <summary>Builds the Debug preset, uses the Debug adapter build directory, and keeps Debug output separate from Release's.</summary>
    [Fact]
    public async Task BuildsTheDebugPresetAndKeepsItsOutputSeparateFromRelease()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await coordinator.BuildAsync(new AdapterHostBuildRequest(temporaryDirectory.Path, BuildProfile.Debug));

        Assert.Equal(["--fresh", "--preset", "windows-x64-debug"], runner.Commands[1].Arguments);
        Assert.Equal(
            ["--build", "--preset", "windows-x64-debug", "--target", "dovahlink_adapter_plugin"],
            runner.Commands[2].Arguments);

        string adapterBuildOutputRoot = Path.Combine(temporaryDirectory.Path, "adapter", "build", "windows-x64-debug");
        string debugOutputRoot = Path.Combine(temporaryDirectory.Path, "tooling", "out", "debug");
        Assert.Equal(
            [
                "tooling/package_adapter_host.py",
                "--adapter-build-dir", adapterBuildOutputRoot,
                "--output-dir", debugOutputRoot,
                "--console-admin-pex", Path.Combine(adapterBuildOutputRoot, "DovahLinkAdmin.pex"),
                "--console-admin-yaml", Path.Combine(temporaryDirectory.Path, "console-admin", "dovahlink.yaml"),
                "--configuration", "Debug",
                "--profile-label", "debug",
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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => papyrusToolchain,
            new BuildOutputOwnershipGuard());

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>
    /// Fails before any build command runs when the build output location cannot be created -- so an
    /// unusable destination is caught in seconds rather than after several minutes of Adapter
    /// compilation.
    /// </summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenTheOutputLocationCannotBeCreated()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        string blockedOutputPath = Path.Combine(temporaryDirectory.Path, "blocked-output");
        File.WriteAllText(blockedOutputPath, "blocked");
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path, OutputRootOverride: blockedOutputPath)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>Reports only the ValidateRepository stage as failed when the build output location cannot be created.</summary>
    [Fact]
    public async Task ReportsOnlyValidateRepositoryFailedWhenTheOutputLocationCannotBeCreated()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        string blockedOutputPath = Path.Combine(temporaryDirectory.Path, "blocked-output");
        File.WriteAllText(blockedOutputPath, "blocked");
        var stageEvents = new List<BuildStageEvent>();
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path, OutputRootOverride: blockedOutputPath),
            onStage: stageEvents.Add));

        Assert.Equal(
            [
                (BuildStage.ValidateRepository, BuildStageStatus.Running),
                (BuildStage.ValidateRepository, BuildStageStatus.Failed),
            ],
            stageEvents.Select(stageEvent => (stageEvent.Stage, stageEvent.Status)));
    }

    /// <summary>
    /// Refuses to build, before any build command runs, when the output root override resolves to an
    /// arbitrary existing folder that already has unrelated content and no DovahLink Builder
    /// ownership mark -- proving normal (non-clean-flagged) packaging can never be pointed at, and
    /// later delete from, a folder full of a user's real, unrelated data.
    /// </summary>
    [Fact]
    public async Task RefusesToBuildWhenTheOutputRootOverrideIsAnUnrelatedNonEmptyFolder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        string outputOverride = Path.Combine(temporaryDirectory.Path, "unrelated-user-folder");
        Directory.CreateDirectory(outputOverride);
        string unrelatedFilePath = Path.Combine(outputOverride, "package");
        byte[] unrelatedContent = "the user's own real data, not DovahLink's"u8.ToArray();
        File.WriteAllBytes(unrelatedFilePath, unrelatedContent);
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path, OutputRootOverride: outputOverride)));

        Assert.Empty(runner.Commands);
        Assert.False(File.Exists(Path.Combine(outputOverride, BuildOutputOwnershipGuard.MarkerFileName)));
        Assert.Equal(unrelatedContent, File.ReadAllBytes(unrelatedFilePath));
    }

    /// <summary>
    /// Builds normally when the output root override is already marked as DovahLink Builder-owned
    /// from a previous run, even though it already has real content from that previous build --
    /// proving a valid Builder-owned custom output can still be built (and, by extension, cleaned).
    /// </summary>
    [Fact]
    public async Task BuildsWhenTheOutputRootOverrideIsAlreadyMarkedAsBuilderOwned()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        string outputOverride = Path.Combine(temporaryDirectory.Path, "previously-owned-output");
        Directory.CreateDirectory(outputOverride);
        File.WriteAllText(Path.Combine(outputOverride, BuildOutputOwnershipGuard.MarkerFileName), "owned");
        File.WriteAllText(Path.Combine(outputOverride, "package"), "a previous build's real output");
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        AdapterHostBuildResult result = await coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path, OutputRootOverride: outputOverride));

        Assert.Equal(runner.ArchivePath, result.ArchivePath);
        Assert.NotEmpty(runner.Commands);
    }

    /// <summary>
    /// Normalizes a malformed output root override -- here an embedded null character, which
    /// <see cref="System.IO.Path.GetFullPath(string)"/> itself rejects -- into the same documented
    /// <see cref="InvalidOperationException"/> every other output-location failure reports, through
    /// the full coordinator, not only the ownership guard in isolation.
    /// </summary>
    [Fact]
    public async Task NormalizesAMalformedOutputRootOverrideIntoAnInvalidOperationException()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        string malformedOutputOverride = Path.Combine(temporaryDirectory.Path, "custom-out\0bad");
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path, OutputRootOverride: malformedOutputOverride)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>Creates each profile's own output root, before any build command runs, whether an override is set or not.</summary>
    /// <param name="profile">The profile whose default output root must exist once ValidateRepository succeeds.</param>
    [Theory]
    [InlineData(BuildProfile.Debug)]
    [InlineData(BuildProfile.Beta)]
    [InlineData(BuildProfile.Release)]
    public async Task CreatesTheProfileSpecificDefaultOutputRootDuringValidateRepository(BuildProfile profile)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await coordinator.BuildAsync(new AdapterHostBuildRequest(temporaryDirectory.Path, profile));

        Assert.True(Directory.Exists(profile.ToOutputRoot(temporaryDirectory.Path)));
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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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

    /// <summary>
    /// Reports a C# stage cancelled mid-flight as Cancelled, not Failed, with no event at all for any
    /// later stage -- proving the one still Pending when cancellation happened never gets a spurious
    /// Running or Failed event of its own.
    /// </summary>
    [Fact]
    public async Task ReportsCancelledNotFailedWhenACSharpStageIsCancelled()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var runner = new FakeCommandRunner { CancelledInvocation = 1 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add));

        Assert.Equal(
            [
                (BuildStage.ValidateRepository, BuildStageStatus.Running),
                (BuildStage.ValidateRepository, BuildStageStatus.Succeeded),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Running),
                (BuildStage.ConfigureAdapter, BuildStageStatus.Cancelled),
            ],
            stageEvents.Select(stageEvent => (stageEvent.Stage, stageEvent.Status)));
    }

    /// <summary>
    /// Reports a packaging stage cancelled mid-flight as Cancelled, not left stuck Running forever --
    /// the one gap <see cref="RunStageAsync"/>'s own per-stage try/catch cannot cover, since the four
    /// packaging stages share one external process rather than one call per stage.
    /// </summary>
    [Fact]
    public async Task ReportsCancelledNotStuckRunningWhenAPackagingStageIsCancelled()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var runner = new FakeCommandRunner
        {
            CancelledInvocation = 4,
            AdditionalPackagingOutputLines = ["##stage host_publish start"],
        };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add));

        IEnumerable<(BuildStage Stage, BuildStageStatus Status)> packagingEvents = stageEvents
            .Where(stageEvent => stageEvent.Stage
                is BuildStage.PublishHost or BuildStage.AssemblePackage or BuildStage.ValidatePackage or BuildStage.CreateZip)
            .Select(stageEvent => (stageEvent.Stage, stageEvent.Status));
        Assert.Equal(
            [
                (BuildStage.PublishHost, BuildStageStatus.Running),
                (BuildStage.PublishHost, BuildStageStatus.Cancelled),
            ],
            packagingEvents);
    }

    /// <summary>
    /// Attributes a cancelled packaging stage to whichever stage was actually running, not the first
    /// one to have started -- proving <c>runningPackagingStage</c>'s tracking keeps up as the shared
    /// packaging process moves through more than one stage marker before cancellation happens.
    /// </summary>
    [Fact]
    public async Task ReportsCancelledForTheLaterPackagingStageActuallyRunning()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var stageEvents = new List<BuildStageEvent>();
        var runner = new FakeCommandRunner
        {
            CancelledInvocation = 4,
            AdditionalPackagingOutputLines =
            [
                "##stage host_publish start",
                "##stage host_publish done",
                "##stage package_assembly start",
            ],
        };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path),
            onStage: stageEvents.Add));

        IEnumerable<(BuildStage Stage, BuildStageStatus Status)> packagingEvents = stageEvents
            .Where(stageEvent => stageEvent.Stage
                is BuildStage.PublishHost or BuildStage.AssemblePackage or BuildStage.ValidatePackage or BuildStage.CreateZip)
            .Select(stageEvent => (stageEvent.Stage, stageEvent.Status));
        Assert.Equal(
            [
                (BuildStage.PublishHost, BuildStageStatus.Running),
                (BuildStage.PublishHost, BuildStageStatus.Succeeded),
                (BuildStage.AssemblePackage, BuildStageStatus.Running),
                (BuildStage.AssemblePackage, BuildStageStatus.Cancelled),
            ],
            packagingEvents);
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
            () => papyrusToolchain,
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

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

    /// <summary>Packages to the request's output root override instead of the profile's default output root.</summary>
    [Fact]
    public async Task PackagesToTheOutputRootOverrideWhenSet()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        string outputOverride = Path.Combine(temporaryDirectory.Path, "custom-output");
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await coordinator.BuildAsync(new AdapterHostBuildRequest(temporaryDirectory.Path, OutputRootOverride: outputOverride));

        string adapterBuildOutputRoot = Path.Combine(temporaryDirectory.Path, "adapter", "build", "windows-x64-release");
        Assert.Equal(
            [
                "tooling/package_adapter_host.py",
                "--adapter-build-dir", adapterBuildOutputRoot,
                "--output-dir", outputOverride,
                "--console-admin-pex", Path.Combine(adapterBuildOutputRoot, "DovahLinkAdmin.pex"),
                "--console-admin-yaml", Path.Combine(temporaryDirectory.Path, "console-admin", "dovahlink.yaml"),
                "--configuration", "Release",
                "--profile-label", "release",
            ],
            runner.Commands[4].Arguments);
        Assert.True(Directory.Exists(outputOverride));
    }

    /// <summary>Uses the output root override instead of a non-Release profile's own default subfolder, the same as it does for Release's.</summary>
    [Fact]
    public async Task PackagesToTheOutputRootOverrideInsteadOfANonReleaseProfilesDefaultSubfolder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        string outputOverride = Path.Combine(temporaryDirectory.Path, "custom-output");
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new BuildOutputOwnershipGuard());

        await coordinator.BuildAsync(new AdapterHostBuildRequest(temporaryDirectory.Path, BuildProfile.Debug, outputOverride));

        Assert.True(Directory.Exists(outputOverride));
        Assert.False(Directory.Exists(Path.Combine(temporaryDirectory.Path, "tooling", "out", "debug")));
    }

    /// <summary>Records command-runner inputs and returns a configured exit code and packaging output.</summary>
    private sealed class FakeCommandRunner : ICommandRunner
    {
        /// <summary>Gets the zero-based invocation that returns failure, or minus one for success.</summary>
        public int FailingInvocation { get; init; } = -1;

        /// <summary>Gets the zero-based invocation that throws <see cref="OperationCanceledException"/> instead of completing, or minus one for none.</summary>
        public int CancelledInvocation { get; init; } = -1;

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

            if (invocation == CancelledInvocation)
            {
                throw new OperationCanceledException();
            }

            return Task.FromResult(invocation == FailingInvocation ? 1 : 0);
        }
    }
}
