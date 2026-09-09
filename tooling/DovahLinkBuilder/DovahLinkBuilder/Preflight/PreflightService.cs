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
    /// <param name="cancellationToken">The token used to cancel the outstanding checks.</param>
    /// <returns>
    /// One <see cref="ToolchainCheckResult"/> per required check: Repository, .NET SDK, Visual
    /// Studio, CMake, vcpkg, Papyrus Compiler, Python, then Output Folder.
    /// </returns>
    Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, CancellationToken cancellationToken = default);
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

    /// <summary>The maximum time a version-probe check waits before reporting <see cref="ToolchainAvailability.CouldNotCheck"/>.</summary>
    private readonly TimeSpan versionProbeTimeout;

    /// <summary>Creates a preflight service over the given command runner.</summary>
    /// <param name="commandRunner">The runner used to probe version-reporting command-line tools.</param>
    public PreflightService(ICommandRunner commandRunner)
        : this(commandRunner, Constants.VersionProbeTimeout)
    {
    }

    /// <summary>Creates a preflight service with a controllable version-probe timeout seam.</summary>
    /// <param name="commandRunner">The runner used to probe version-reporting command-line tools.</param>
    /// <param name="versionProbeTimeout">The maximum time a version-probe check waits before reporting <see cref="ToolchainAvailability.CouldNotCheck"/>.</param>
    internal PreflightService(ICommandRunner commandRunner, TimeSpan versionProbeTimeout)
    {
        this.commandRunner = commandRunner;
        this.versionProbeTimeout = versionProbeTimeout;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, CancellationToken cancellationToken = default)
    {
        ToolchainCheckResult repositoryResult = CheckRepository(startPath, out string? repositoryRoot);

        return
        [
            repositoryResult,
            await CheckVersionProbeAsync("dotnet", ".NET SDK", startPath, cancellationToken),
            VisualStudioToolchainLocator.TryFind(),
            await CheckVersionProbeAsync("cmake", "CMake", startPath, cancellationToken),
            CheckVcpkg(),
            PapyrusToolchainLocator.TryFind(),
            await CheckVersionProbeAsync("python", "Python", startPath, cancellationToken),
            CheckOutputFolder(repositoryRoot),
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
    /// Locates the vcpkg directory bundled with Visual Studio, reporting the result instead of
    /// throwing. Reported separately from the Visual Studio check because vcpkg is its own required
    /// build input, even though both are located from the same installation search.
    /// </summary>
    private static ToolchainCheckResult CheckVcpkg()
    {
        try
        {
            VisualStudioToolchain toolchain = VisualStudioToolchainLocator.Find();
            return new ToolchainCheckResult(VcpkgToolName, ToolchainAvailability.Found, toolchain.VcpkgRoot, null);
        }
        catch (InvalidOperationException exception)
        {
            return new ToolchainCheckResult(VcpkgToolName, ToolchainAvailability.Missing, null, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new ToolchainCheckResult(VcpkgToolName, ToolchainAvailability.CouldNotCheck, null, exception.Message);
        }
    }

    /// <summary>
    /// Verifies that the build output folder exists or can be created, reporting the result instead
    /// of throwing.
    /// </summary>
    /// <param name="repositoryRoot">The repository root located by <see cref="CheckRepository"/>, or <see langword="null"/>.</param>
    private static ToolchainCheckResult CheckOutputFolder(string? repositoryRoot)
    {
        if (repositoryRoot is null)
        {
            return new ToolchainCheckResult(
                OutputFolderToolName,
                ToolchainAvailability.CouldNotCheck,
                null,
                "The repository root could not be determined, so the output folder location is unknown.");
        }

        string outputRoot = Path.Combine(repositoryRoot, "tooling", "out");
        try
        {
            Directory.CreateDirectory(outputRoot);
            return new ToolchainCheckResult(OutputFolderToolName, ToolchainAvailability.Found, outputRoot, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
