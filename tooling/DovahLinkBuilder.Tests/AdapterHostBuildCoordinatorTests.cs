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
        CreateBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

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
        PapyrusToolchain papyrusToolchain = CreatePapyrusToolchain(temporaryDirectory.Path);
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
        CreateBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { FailingInvocation = failingInvocation };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Equal(failingInvocation + 1, runner.Commands.Count);
    }

    /// <summary>Fails without invoking the packaging script when the Papyrus compile returns an error.</summary>
    [Fact]
    public async Task FailsWithoutPackagingWhenThePapyrusCompileFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { FailingInvocation = 3 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Equal(4, runner.Commands.Count);
    }

    /// <summary>Fails when the packaging script exits with a nonzero status.</summary>
    [Fact]
    public async Task FailsWhenThePackagingScriptFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { FailingInvocation = 4 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

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
        CreateBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { ReportArchivePath = false };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Contains("did not report a written archive path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Uses the last reported archive path when packaging reports more than one.</summary>
    [Fact]
    public async Task UsesTheLastReportedArchivePathWhenPackagingReportsMultipleWrittenPaths()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner
        {
            AdditionalPackagingOutputLines = ["Wrote C:\\stale\\DovahLink-Adapter-0.0.9.zip"],
        };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

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
        CreateBuildInputs(temporaryDirectory.Path);
        File.Delete(Path.Combine(temporaryDirectory.Path, "console-admin", "dovahlink.yaml"));
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

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
        CreateBuildInputs(temporaryDirectory.Path);
        File.Delete(Path.Combine(temporaryDirectory.Path, "console-admin", "DovahLinkAdmin.psc"));
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>Fails before any build command runs when the Papyrus compiler cannot be found.</summary>
    [Fact]
    public async Task FailsBeforeAnyBuildCommandWhenThePapyrusCompilerIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path);
        PapyrusToolchain papyrusToolchain = CreatePapyrusToolchain(temporaryDirectory.Path);
        File.Delete(papyrusToolchain.CompilerPath);
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
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
        CreateBuildInputs(temporaryDirectory.Path);
        VisualStudioToolchain toolchain = CreateToolchain(temporaryDirectory.Path);
        File.Delete(toolchain.VcvarsallPath);
        var runner = new FakeCommandRunner();
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => toolchain,
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));

        Assert.Empty(runner.Commands);
    }

    /// <summary>Fails before CMake when Visual Studio environment initialization returns an error.</summary>
    [Fact]
    public async Task FailsBeforeCMakeWhenEnvironmentInitializationFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        CreateBuildInputs(temporaryDirectory.Path);
        var runner = new FakeCommandRunner { FailingInvocation = 0 };
        var coordinator = new AdapterHostBuildCoordinator(
            runner,
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

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
        CreateBuildInputs(temporaryDirectory.Path);
        var output = new List<string>();
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => CreateToolchain(temporaryDirectory.Path),
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

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
        VisualStudioToolchain toolchain = CreateToolchain(temporaryDirectory.Path);
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => toolchain,
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

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
        VisualStudioToolchain toolchain = CreateToolchain(temporaryDirectory.Path);
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => toolchain,
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

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
        VisualStudioToolchain toolchain = CreateToolchain(temporaryDirectory.Path);
        var coordinator = new AdapterHostBuildCoordinator(
            new FakeCommandRunner(),
            () => toolchain,
            () => CreatePapyrusToolchain(temporaryDirectory.Path));

        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.BuildAsync(
            new AdapterHostBuildRequest(temporaryDirectory.Path)));
    }

    /// <summary>Creates the repository files required by the build coordinator tests.</summary>
    /// <param name="repositoryRoot">The temporary repository root.</param>
    private static void CreateBuildInputs(string repositoryRoot)
    {
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");
        File.WriteAllText(Path.Combine(repositoryRoot, "VERSION"), "0.1.0");

        string toolingRoot = Path.Combine(repositoryRoot, "tooling");
        Directory.CreateDirectory(toolingRoot);
        File.WriteAllText(Path.Combine(toolingRoot, "package_adapter_host.py"), "# fake packaging script");

        string consoleAdminRoot = Path.Combine(repositoryRoot, "console-admin");
        Directory.CreateDirectory(consoleAdminRoot);
        File.WriteAllText(Path.Combine(consoleAdminRoot, "DovahLinkAdmin.psc"), "Scriptname DovahLinkAdmin Hidden");
        File.WriteAllText(Path.Combine(consoleAdminRoot, "dovahlink.yaml"), "name: dovahlink");
    }

    /// <summary>Creates the validated toolchain files used by coordinator tests.</summary>
    /// <param name="repositoryRoot">The temporary root under which to create the installation.</param>
    /// <returns>The created environment-script and vcpkg paths.</returns>
    private static VisualStudioToolchain CreateToolchain(string repositoryRoot)
    {
        string installationRoot = Path.Combine(repositoryRoot, "Visual Studio");
        string vcvarsallPath = Path.Combine(installationRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(installationRoot, "VC", "vcpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off\n");
        return new VisualStudioToolchain(vcvarsallPath, vcpkgRoot);
    }

    /// <summary>Creates the validated Papyrus toolchain files used by coordinator tests.</summary>
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
