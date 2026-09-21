using System.ComponentModel;
using System.IO;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Preflight;

/// <summary>Aggregates every required build tool into one ordered, non-throwing preflight report.</summary>
public interface IPreflightService
{
    /// <summary>
    /// Checks the repository, every required toolchain, and the build output location, in that order.
    /// </summary>
    /// <param name="startPath">The path from which to search upward for the repository root.</param>
    /// <param name="outputPathOverride">
    /// The configured build output path override, or <see langword="null"/> to check the repository's
    /// default <c>tooling/out</c> instead.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the outstanding checks.</param>
    /// <returns>
    /// One <see cref="ToolchainCheckResult"/> per required check: Repository, .NET SDK, Visual
    /// Studio, CMake, vcpkg, Papyrus Compiler, Python, then Output Folder.
    /// </returns>
    Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, string? outputPathOverride = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes just the Output Folder check against <paramref name="outputPathOverride"/>, leaving
    /// every other entry in <paramref name="previousResults"/> unchanged -- a build output path change
    /// alone does not affect any other required tool's availability, so nothing else needs re-checking.
    /// </summary>
    /// <param name="previousResults">The most recently loaded results, as returned by <see cref="CheckAllAsync"/>.</param>
    /// <param name="repositoryRoot">The repository root to resolve the default output location under when <paramref name="outputPathOverride"/> is <see langword="null"/>.</param>
    /// <param name="outputPathOverride">The configured build output path override, or <see langword="null"/> to check the repository's default <c>tooling/out</c> instead.</param>
    /// <returns><paramref name="previousResults"/> unchanged if it contains no Output Folder entry yet; otherwise a new list with that entry replaced.</returns>
    IReadOnlyList<ToolchainCheckResult> RefreshOutputFolderCheck(IReadOnlyList<ToolchainCheckResult> previousResults, string? repositoryRoot, string? outputPathOverride);
}

/// <summary>Aggregates every required build tool into one ordered, non-throwing preflight report.</summary>
public sealed class PreflightService : IPreflightService
{
    /// <summary>The tool name reported for the repository check.</summary>
    private const string RepositoryToolName = "Repository";

    /// <summary>The tool name reported for the vcpkg check.</summary>
    private const string VcpkgToolName = "vcpkg";

    /// <summary>The tool name reported for the output folder check.</summary>
    private const string OutputFolderToolName = "Output Folder";

    /// <summary>The runner used to probe version-reporting command-line tools.</summary>
    private readonly ICommandRunner commandRunner;

    /// <summary>The lookup that selects the Visual Studio toolchain used by preflight and builds.</summary>
    private readonly Func<VisualStudioToolchain> visualStudioToolchainProvider;

    /// <summary>The maximum time a version-probe check waits before reporting <see cref="ToolchainAvailability.CouldNotCheck"/>.</summary>
    private readonly TimeSpan versionProbeTimeout;

    /// <summary>Creates a preflight service over the given command runner.</summary>
    /// <param name="commandRunner">The runner used to probe version-reporting command-line tools.</param>
    public PreflightService(ICommandRunner commandRunner)
        : this(commandRunner, VisualStudioToolchainLocator.Find, Constants.VersionProbeTimeout)
    {
    }

    /// <summary>Creates a preflight service over the given command runner and Visual Studio lookup.</summary>
    /// <param name="commandRunner">The runner used to probe version-reporting command-line tools.</param>
    /// <param name="visualStudioToolchainProvider">The lookup that selects the Visual Studio installation and its tools.</param>
    public PreflightService(ICommandRunner commandRunner, Func<VisualStudioToolchain> visualStudioToolchainProvider)
        : this(commandRunner, visualStudioToolchainProvider, Constants.VersionProbeTimeout)
    {
    }

    /// <summary>Creates a preflight service with a controllable version-probe timeout seam.</summary>
    /// <param name="commandRunner">The runner used to probe version-reporting command-line tools.</param>
    /// <param name="versionProbeTimeout">The maximum time a version-probe check waits before reporting <see cref="ToolchainAvailability.CouldNotCheck"/>.</param>
    internal PreflightService(ICommandRunner commandRunner, TimeSpan versionProbeTimeout)
        : this(commandRunner, VisualStudioToolchainLocator.Find, versionProbeTimeout)
    {
    }

    /// <summary>Creates a preflight service with a controllable toolchain lookup and version-probe timeout.</summary>
    /// <param name="commandRunner">The runner used to probe version-reporting command-line tools.</param>
    /// <param name="visualStudioToolchainProvider">The lookup that selects the Visual Studio installation and its tools.</param>
    /// <param name="versionProbeTimeout">The maximum time a version-probe check waits before reporting <see cref="ToolchainAvailability.CouldNotCheck"/>.</param>
    internal PreflightService(
        ICommandRunner commandRunner,
        Func<VisualStudioToolchain> visualStudioToolchainProvider,
        TimeSpan versionProbeTimeout)
    {
        this.commandRunner = commandRunner;
        this.visualStudioToolchainProvider = visualStudioToolchainProvider;
        this.versionProbeTimeout = versionProbeTimeout;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, string? outputPathOverride = null, CancellationToken cancellationToken = default)
    {
        ToolchainCheckResult repositoryResult = CheckRepository(startPath, out string? repositoryRoot);
        ToolchainCheckResult visualStudioResult = VisualStudioToolchainLocator.TryFind(visualStudioToolchainProvider, out VisualStudioToolchain? toolchain);

        return
        [
            repositoryResult,
            await CheckVersionProbeAsync("dotnet", ".NET SDK", startPath, cancellationToken),
            visualStudioResult,
            await CheckCMakeAsync(toolchain, visualStudioResult, startPath, cancellationToken),
            CheckVcpkg(toolchain, visualStudioResult),
            PapyrusToolchainLocator.TryFind(),
            await CheckVersionProbeAsync("python", "Python", startPath, cancellationToken),
            CheckOutputFolder(repositoryRoot, outputPathOverride),
        ];
    }

    /// <summary>Locates the repository root, reporting the result instead of throwing.</summary>
    /// <param name="startPath">The path from which to search upward for the repository root.</param>
    /// <param name="repositoryRoot">The located repository root, or <see langword="null"/> when not found.</param>
    private static ToolchainCheckResult CheckRepository(string startPath, out string? repositoryRoot)
    {
        try
        {
            repositoryRoot = RepositoryRootLocator.Find(startPath);
            return new ToolchainCheckResult(RepositoryToolName, ToolchainAvailability.Found, repositoryRoot, null);
        }
        catch (InvalidOperationException exception)
        {
            repositoryRoot = null;
            return new ToolchainCheckResult(RepositoryToolName, ToolchainAvailability.Missing, null, exception.Message);
        }
    }

    /// <summary>
    /// Reports the vcpkg directory from the same selected Visual Studio installation, preserving
    /// the separate preflight row because vcpkg is its own required build input.
    /// </summary>
    private static ToolchainCheckResult CheckVcpkg(VisualStudioToolchain? toolchain, ToolchainCheckResult visualStudioResult)
    {
        if (toolchain is not null)
        {
            return new ToolchainCheckResult(VcpkgToolName, ToolchainAvailability.Found, toolchain.VcpkgRoot, null);
        }

        return new ToolchainCheckResult(
            VcpkgToolName,
            visualStudioResult.Availability,
            null,
            visualStudioResult.RemediationHint);
    }

    /// <summary>Checks the CMake executable selected from the located Visual Studio installation.</summary>
    /// <param name="toolchain">The selected Visual Studio toolchain, or <see langword="null"/> if it could not be located.</param>
    /// <param name="visualStudioResult">The result explaining why Visual Studio could not be located, if applicable.</param>
    /// <param name="workingDirectory">The directory in which to run the version probe.</param>
    /// <param name="cancellationToken">The caller's token; cancelling it cancels the probe.</param>
    /// <returns>The CMake availability result.</returns>
    private Task<ToolchainCheckResult> CheckCMakeAsync(
        VisualStudioToolchain? toolchain,
        ToolchainCheckResult visualStudioResult,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (toolchain is null)
        {
            ToolchainAvailability availability = visualStudioResult.Availability == ToolchainAvailability.CouldNotCheck
                ? ToolchainAvailability.CouldNotCheck
                : ToolchainAvailability.Missing;
            return Task.FromResult(new ToolchainCheckResult(
                "CMake",
                availability,
                null,
                visualStudioResult.RemediationHint ?? "A supported Visual Studio installation is required to locate its bundled CMake."));
        }

        if (!File.Exists(toolchain.CMakePath))
        {
            return Task.FromResult(new ToolchainCheckResult(
                "CMake",
                ToolchainAvailability.Missing,
                null,
                $"The selected Visual Studio installation does not contain CMake at '{toolchain.CMakePath}'. Install C++ CMake tools for Windows in Visual Studio Installer."));
        }

        return CheckVersionProbeAsync(toolchain.CMakePath, "CMake", workingDirectory, cancellationToken);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolchainCheckResult> RefreshOutputFolderCheck(IReadOnlyList<ToolchainCheckResult> previousResults, string? repositoryRoot, string? outputPathOverride)
    {
        int index = previousResults.ToList().FindIndex(result => result.ToolName == OutputFolderToolName);
        if (index < 0)
        {
            return previousResults;
        }

        ToolchainCheckResult[] updatedResults = previousResults.ToArray();
        updatedResults[index] = CheckOutputFolder(repositoryRoot, outputPathOverride);
        return updatedResults;
    }

    /// <summary>
    /// Verifies that the build output folder exists or can be created, reporting the result instead
    /// of throwing.
    /// </summary>
    /// <param name="repositoryRoot">The repository root located by <see cref="CheckRepository"/>, or <see langword="null"/>.</param>
    /// <param name="outputPathOverride">The configured build output path override, or <see langword="null"/> to check the repository's default <c>tooling/out</c> instead.</param>
    private static ToolchainCheckResult CheckOutputFolder(string? repositoryRoot, string? outputPathOverride)
    {
        if (outputPathOverride is null && repositoryRoot is null)
        {
            return new ToolchainCheckResult(
                OutputFolderToolName,
                ToolchainAvailability.CouldNotCheck,
                null,
                "The repository root could not be determined, so the output folder location is unknown.");
        }

        // repositoryRoot is only null here when outputPathOverride is set, per the guard above.
        string outputRoot = outputPathOverride ?? Path.Combine(repositoryRoot!, "tooling", "out");
        try
        {
            Directory.CreateDirectory(outputRoot);
            return new ToolchainCheckResult(OutputFolderToolName, ToolchainAvailability.Found, outputRoot, null);
        }
        // ArgumentException and NotSupportedException cover an invalid path value itself (for
        // example characters Windows rejects, or a colon outside the drive designator) -- not just
        // an inaccessible one: outputPathOverride flows here unvalidated from Settings' free-text
        // OutputPathContext, and every value it can hold must resolve to a reported result rather
        // than throwing out of this call, which OnOutputPathContextChanged invokes synchronously
        // from a PropertyChanged handler with no exception boundary of its own.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new ToolchainCheckResult(OutputFolderToolName, ToolchainAvailability.CouldNotCheck, outputRoot, exception.Message);
        }
    }

    /// <summary>
    /// Runs <paramref name="executablePath"/> with a single <c>--version</c> argument through
    /// <see cref="commandRunner"/>, reporting the result instead of throwing or hanging.
    /// </summary>
    /// <param name="executablePath">The executable to probe.</param>
    /// <param name="toolName">The human-readable tool name to report.</param>
    /// <param name="workingDirectory">The directory in which to run the probe.</param>
    /// <param name="cancellationToken">The caller's token; cancelling it cancels this probe.</param>
    /// <returns>
    /// <see cref="ToolchainAvailability.Found"/> on a zero exit code; <see cref="ToolchainAvailability.Invalid"/>
    /// when the tool ran but exited nonzero; <see cref="ToolchainAvailability.Missing"/> when the
    /// executable could not be started; <see cref="ToolchainAvailability.CouldNotCheck"/> when the
    /// probe did not finish within <see cref="versionProbeTimeout"/>.
    /// </returns>
    private async Task<ToolchainCheckResult> CheckVersionProbeAsync(
        string executablePath,
        string toolName,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var command = new BuildCommand(executablePath, ["--version"], workingDirectory, new Dictionary<string, string>());
        List<string> outputLines = [];
        using var timeoutSource = new CancellationTokenSource(versionProbeTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            int exitCode = await commandRunner.RunAsync(command, outputLines.Add, outputLines.Add, linkedSource.Token);
            return exitCode == 0
                ? new ToolchainCheckResult(toolName, ToolchainAvailability.Found, string.Join(Environment.NewLine, outputLines), null)
                : new ToolchainCheckResult(
                    toolName,
                    ToolchainAvailability.Invalid,
                    null,
                    $"{executablePath} --version exited with code {exitCode}.");
        }
        catch (Win32Exception exception)
        {
            return new ToolchainCheckResult(toolName, ToolchainAvailability.Missing, null, exception.Message);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new ToolchainCheckResult(
                toolName,
                ToolchainAvailability.CouldNotCheck,
                null,
                $"{executablePath} --version did not respond within {versionProbeTimeout.TotalSeconds:0} seconds.");
        }
    }
}
