using System.ComponentModel;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Preflight;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies preflight aggregation across the repository, toolchain, and output folder checks.</summary>
public sealed class PreflightServiceTests
{
    /// <summary>The version-probe timeout used by every test, short enough that the timeout test does not slow the suite.</summary>
    private static readonly TimeSpan TestVersionProbeTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Reports the repository as found when the search reaches a valid repository root.</summary>
    [Fact]
    public async Task CheckAllReportsRepositoryFoundFromAChildDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        string childDirectory = Path.Combine(repositoryRoot, "tooling", "DovahLinkBuilder", "bin");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(childDirectory);

        ToolchainCheckResult repositoryResult = results[0];
        Assert.Equal("Repository", repositoryResult.ToolName);
        Assert.Equal(ToolchainAvailability.Found, repositoryResult.Availability);
        Assert.Equal(repositoryRoot, repositoryResult.Detail);
    }

    /// <summary>Reports the repository as missing when the search path is outside any repository.</summary>
    [Fact]
    public async Task CheckAllReportsRepositoryMissingOutsideARepository()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        ToolchainCheckResult repositoryResult = results[0];
        Assert.Equal(ToolchainAvailability.Missing, repositoryResult.Availability);
        Assert.Null(repositoryResult.Detail);
        Assert.NotNull(repositoryResult.RemediationHint);
    }

    /// <summary>
    /// Reports Visual Studio exactly as <see cref="VisualStudioToolchainLocator.TryFind()"/> itself
    /// reports it, regardless of the running machine's actual installation state.
    /// </summary>
    [Fact]
    public async Task CheckAllReportsVisualStudioMatchingTheLocatorsOwnResult()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        Assert.Equal(VisualStudioToolchainLocator.TryFind(), results[2]);
    }

    /// <summary>
    /// Reports vcpkg as found with the bundled installation's <c>VcpkgRoot</c> exactly when
    /// <see cref="VisualStudioToolchainLocator.Find()"/> itself locates an installation, and missing
    /// otherwise -- proving the two checks are derived from the same lookup rather than
    /// independently searching, regardless of the running machine's actual installation state.
    /// </summary>
    [Fact]
    public async Task CheckAllReportsVcpkgMatchingTheBundledVisualStudioInstallation()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        ToolchainCheckResult vcpkgResult = results[4];
        Assert.Equal("vcpkg", vcpkgResult.ToolName);
        try
        {
            VisualStudioToolchain toolchain = VisualStudioToolchainLocator.Find();
            Assert.Equal(ToolchainAvailability.Found, vcpkgResult.Availability);
            Assert.Equal(toolchain.VcpkgRoot, vcpkgResult.Detail);
        }
        catch (InvalidOperationException)
        {
            Assert.Equal(ToolchainAvailability.Missing, vcpkgResult.Availability);
        }
    }

    /// <summary>
    /// Reports Papyrus exactly as <see cref="PapyrusToolchainLocator.TryFind()"/> itself reports it,
    /// regardless of the running machine's actual installation state.
    /// </summary>
    [Fact]
    public async Task CheckAllReportsPapyrusMatchingTheLocatorsOwnResult()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        Assert.Equal(PapyrusToolchainLocator.TryFind(), results[5]);
    }

    /// <summary>Reports a version-probed tool as found when the probe exits zero.</summary>
    [Fact]
    public async Task CheckAllReportsDotNetFoundOnAZeroExitCode()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var runner = new FakeCommandRunner { ExitCode = 0, OutputLines = ["9.0.316"] };
        var service = new PreflightService(runner, TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        ToolchainCheckResult dotNetResult = results[1];
        Assert.Equal(".NET SDK", dotNetResult.ToolName);
        Assert.Equal(ToolchainAvailability.Found, dotNetResult.Availability);
        Assert.Equal("9.0.316", dotNetResult.Detail);
        Assert.Equal("dotnet", runner.Commands[0].ExecutablePath);
        Assert.Equal(["--version"], runner.Commands[0].Arguments);
    }

    /// <summary>Reports a version-probed tool as invalid when the probe runs but exits nonzero.</summary>
    [Fact]
    public async Task CheckAllReportsDotNetInvalidOnANonzeroExitCode()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var runner = new FakeCommandRunner { ExitCode = 1 };
        var service = new PreflightService(runner, TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        ToolchainCheckResult dotNetResult = results[1];
        Assert.Equal(ToolchainAvailability.Invalid, dotNetResult.Availability);
        Assert.Null(dotNetResult.Detail);
        Assert.NotNull(dotNetResult.RemediationHint);
    }

    /// <summary>Reports a version-probed tool as missing when the executable cannot be started.</summary>
    [Fact]
    public async Task CheckAllReportsDotNetMissingWhenTheExecutableCannotStart()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var runner = new FakeCommandRunner { ThrowsWin32Exception = true };
        var service = new PreflightService(runner, TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        ToolchainCheckResult dotNetResult = results[1];
        Assert.Equal(ToolchainAvailability.Missing, dotNetResult.Availability);
        Assert.Null(dotNetResult.Detail);
        Assert.NotNull(dotNetResult.RemediationHint);
    }

    /// <summary>Reports a version-probed tool as could-not-check when the probe does not finish before the timeout.</summary>
    [Fact]
    public async Task CheckAllReportsDotNetCouldNotCheckWhenTheProbeTimesOut()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var runner = new FakeCommandRunner { HangsUntilCancelled = true };
        var service = new PreflightService(runner, TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        ToolchainCheckResult dotNetResult = results[1];
        Assert.Equal(ToolchainAvailability.CouldNotCheck, dotNetResult.Availability);
        Assert.Null(dotNetResult.Detail);
        Assert.NotNull(dotNetResult.RemediationHint);
    }

    /// <summary>Propagates cancellation from the caller's own token rather than reporting it as a timeout.</summary>
    [Fact]
    public async Task CheckAllPropagatesCallerCancellationRatherThanReportingCouldNotCheck()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var runner = new FakeCommandRunner { HangsUntilCancelled = true };
        var service = new PreflightService(runner, TimeSpan.FromSeconds(30));
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAllAsync(temporaryDirectory.Path, callerCancellation.Token));
    }

    /// <summary>Routes the CMake and Python probes through the same shared check, reporting their own tool names.</summary>
    [Fact]
    public async Task CheckAllReportsCMakeAndPythonFoundOnAZeroExitCode()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var runner = new FakeCommandRunner { ExitCode = 0, OutputLines = ["ok"] };
        var service = new PreflightService(runner, TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        ToolchainCheckResult cMakeResult = results[3];
        Assert.Equal("CMake", cMakeResult.ToolName);
        Assert.Equal(ToolchainAvailability.Found, cMakeResult.Availability);
        Assert.Equal("cmake", runner.Commands[1].ExecutablePath);

        ToolchainCheckResult pythonResult = results[6];
        Assert.Equal("Python", pythonResult.ToolName);
        Assert.Equal(ToolchainAvailability.Found, pythonResult.Availability);
        Assert.Equal("python", runner.Commands[2].ExecutablePath);
    }

    /// <summary>Reports the output folder as found when it exists under the repository root.</summary>
    [Fact]
    public async Task CheckAllReportsOutputFolderFoundWhenItCanBeCreated()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(repositoryRoot);

        ToolchainCheckResult outputFolderResult = results[7];
        Assert.Equal("Output Folder", outputFolderResult.ToolName);
        Assert.Equal(ToolchainAvailability.Found, outputFolderResult.Availability);
        Assert.Equal(Path.Combine(repositoryRoot, "tooling", "out"), outputFolderResult.Detail);
        Assert.True(Directory.Exists(Path.Combine(repositoryRoot, "tooling", "out")));
    }

    /// <summary>Reports the output folder as could-not-check when its path is blocked by an existing file.</summary>
    [Fact]
    public async Task CheckAllReportsOutputFolderCouldNotCheckWhenItsPathIsBlocked()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "tooling"));
        File.WriteAllText(Path.Combine(repositoryRoot, "tooling", "out"), "blocked");
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(repositoryRoot);

        ToolchainCheckResult outputFolderResult = results[7];
        Assert.Equal(ToolchainAvailability.CouldNotCheck, outputFolderResult.Availability);
        Assert.NotNull(outputFolderResult.RemediationHint);
    }

    /// <summary>Reports the output folder as could-not-check when the repository root itself could not be determined.</summary>
    [Fact]
    public async Task CheckAllReportsOutputFolderCouldNotCheckWithoutARepositoryRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        ToolchainCheckResult outputFolderResult = results[7];
        Assert.Equal(ToolchainAvailability.CouldNotCheck, outputFolderResult.Availability);
        Assert.NotNull(outputFolderResult.RemediationHint);
    }

    /// <summary>Returns all eight checks in the documented order.</summary>
    [Fact]
    public async Task CheckAllReturnsEveryCheckInTheDocumentedOrder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new PreflightService(new FakeCommandRunner(), TestVersionProbeTimeout);

        IReadOnlyList<ToolchainCheckResult> results = await service.CheckAllAsync(temporaryDirectory.Path);

        Assert.Equal(
            ["Repository", ".NET SDK", "Visual Studio", "CMake", "vcpkg", "Papyrus Compiler", "Python", "Output Folder"],
            results.Select(result => result.ToolName));
    }

    /// <summary>Records command invocations and simulates a missing executable or an unresponsive process.</summary>
    private sealed class FakeCommandRunner : ICommandRunner
    {
        /// <summary>Gets the exit code returned when the probe does not throw or hang.</summary>
        public int ExitCode { get; init; }

        /// <summary>Gets the standard-output lines emitted when the probe does not throw or hang.</summary>
        public IReadOnlyList<string> OutputLines { get; init; } = [];

        /// <summary>Gets whether <see cref="RunAsync"/> throws <see cref="Win32Exception"/> to simulate a missing executable.</summary>
        public bool ThrowsWin32Exception { get; init; }

        /// <summary>Gets whether <see cref="RunAsync"/> waits until cancelled to simulate an unresponsive process.</summary>
        public bool HangsUntilCancelled { get; init; }

        /// <summary>Gets the ordered commands supplied to the runner.</summary>
        public List<BuildCommand> Commands { get; } = [];

        /// <summary>Records the command invocation and returns the configured outcome.</summary>
        public async Task<int> RunAsync(
            BuildCommand command,
            Action<string>? onStandardOutput,
            Action<string>? onStandardError,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (ThrowsWin32Exception)
            {
                throw new Win32Exception("The system cannot find the file specified.");
            }
            if (HangsUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            foreach (string line in OutputLines)
            {
                onStandardOutput?.Invoke(line);
            }
            return ExitCode;
        }
    }
}
