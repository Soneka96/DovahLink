using System.IO.Compression;
using DovahLink.BridgeBuilder.Packaging;

namespace DovahLink.BridgeBuilder.Build;

/// <summary>Describes the repository and channel for one bridge build.</summary>
/// <param name="RepositoryRoot">The repository root containing the bridge sources.</param>
/// <param name="Channel">The package channel to build.</param>
public sealed record BridgeBuildRequest(string RepositoryRoot, PackageChannel Channel);

/// <summary>Contains the packaging plan and archive produced by a bridge build.</summary>
/// <param name="Plan">The files and version included in the package.</param>
/// <param name="ArchivePath">The path to the created ZIP archive.</param>
public sealed record BridgeBuildResult(ArtifactPlan Plan, string ArchivePath);

/// <summary>Coordinates bridge compilation and artifact packaging.</summary>
public sealed class BridgeBuildCoordinator
{
    /// <summary>The configured Release build output directory name.</summary>
    private const string ReleaseBuildDirectory = "windows-x64-release";

    /// <summary>Runs the external build command.</summary>
    private readonly ICommandRunner commandRunner;

    /// <summary>Provides the Visual Studio toolchain used by the build.</summary>
    private readonly Func<VisualStudioToolchain> toolchainProvider;

    /// <summary>Provides the Papyrus compiler toolchain used to compile the console-admin script.</summary>
    private readonly Func<PapyrusToolchain> papyrusToolchainProvider;

    /// <summary>
    /// Initializes a coordinator for building and packaging the bridge.
    /// </summary>
    /// <param name="commandRunner">The command runner used to execute the build.</param>
    /// <param name="toolchainProvider">The provider used to obtain the Visual Studio toolchain.</param>
    /// <param name="papyrusToolchainProvider">The provider used to obtain the Papyrus compiler toolchain.</param>
    public BridgeBuildCoordinator(
        ICommandRunner commandRunner,
        Func<VisualStudioToolchain> toolchainProvider,
        Func<PapyrusToolchain> papyrusToolchainProvider)
    {
        this.commandRunner = commandRunner;
        this.toolchainProvider = toolchainProvider;
        this.papyrusToolchainProvider = papyrusToolchainProvider;
    }

    /// <summary>
    /// Builds the bridge Release target and packages its artifacts into a ZIP archive.
    /// </summary>
    /// <param name="request">The repository root and package channel used for the build.</param>
    /// <param name="onOutput">An optional callback for build and packaging progress messages.</param>
    /// <param name="cancellationToken">A token that can cancel manifest reading or the build command.</param>
    /// <returns>The generated artifact plan and path to the created archive.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the bridge manifest or a required build artifact is missing.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the bridge build fails.</exception>
    public async Task<BridgeBuildResult> BuildAsync(
        BridgeBuildRequest request,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        string repositoryRoot = Path.GetFullPath(request.RepositoryRoot);
        string bridgeRoot = Path.Combine(repositoryRoot, "bridge");
        string manifestPath = Path.Combine(bridgeRoot, "vcpkg.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Could not find the bridge vcpkg manifest.", manifestPath);
        }

        ArtifactPlan plan = ArtifactPlan.Create(
            BridgeVersion.FromVcpkgManifest(await File.ReadAllTextAsync(manifestPath, cancellationToken)),
            request.Channel);
        VisualStudioToolchain toolchain = VisualStudioToolchainLocator.Validate(toolchainProvider());

        onOutput?.Invoke("Building the DovahLink bridge Release target...");
        var environmentLines = new List<string>();
        int environmentExitCode = await commandRunner.RunAsync(
            BuildCommand.CreateEnvironmentImport(toolchain),
            environmentLines.Add,
            onOutput,
            cancellationToken);
        if (environmentExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Visual Studio environment initialization failed with exit code {environmentExitCode}.");
        }

        IReadOnlyDictionary<string, string> buildEnvironment = VisualStudioEnvironment.Create(environmentLines, toolchain);
        foreach (BuildCommand command in BuildCommand.CreateReleaseBuild(bridgeRoot, buildEnvironment))
        {
            int exitCode = await commandRunner.RunAsync(command, onOutput, onOutput, cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"The bridge build failed with exit code {exitCode}.");
            }
        }

        string buildOutputRoot = Path.Combine(bridgeRoot, "build", ReleaseBuildDirectory);

        onOutput?.Invoke("Compiling the DovahLink admin console script...");
        PapyrusToolchain papyrusToolchain = PapyrusToolchainLocator.Validate(papyrusToolchainProvider());
        string consoleAdminRoot = Path.Combine(repositoryRoot, "console-admin");
        BuildCommand papyrusCommand = BuildCommand.CreatePapyrusCompile(
            Path.Combine(consoleAdminRoot, "DovahLinkAdmin.psc"),
            papyrusToolchain,
            buildOutputRoot);
        int papyrusExitCode = await commandRunner.RunAsync(papyrusCommand, onOutput, onOutput, cancellationToken);
        if (papyrusExitCode != 0)
        {
            throw new InvalidOperationException($"The Papyrus compile failed with exit code {papyrusExitCode}.");
        }
        File.Copy(
            Path.Combine(consoleAdminRoot, "dovahlink.yaml"),
            Path.Combine(buildOutputRoot, "dovahlink.yaml"),
            overwrite: true);

        string outputRoot = Path.Combine(repositoryRoot, "tooling", "out");
        Directory.CreateDirectory(outputRoot);

        string stagingRoot = Path.Combine(outputRoot, $".staging-{Guid.NewGuid():N}");
        string temporaryArchivePath = Path.Combine(outputRoot, $".{plan.ArchiveName}.{Guid.NewGuid():N}.tmp");
        string archivePath = Path.Combine(outputRoot, plan.ArchiveName);
        try
        {
            foreach (ArtifactFile artifactFile in plan.Files)
            {
                string sourcePath = Path.Combine(buildOutputRoot, artifactFile.SourceName);
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        $"The build completed but required artifact '{artifactFile.SourceName}' was not found.",
                        sourcePath);
                }

                string destinationPath = Path.Combine(stagingRoot, artifactFile.ArchivePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            ZipFile.CreateFromDirectory(stagingRoot, temporaryArchivePath, CompressionLevel.Optimal, false);
            File.Move(temporaryArchivePath, archivePath, overwrite: true);
            onOutput?.Invoke($"Created {archivePath}");
            return new BridgeBuildResult(plan, archivePath);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }

            if (File.Exists(temporaryArchivePath))
            {
                File.Delete(temporaryArchivePath);
            }
        }
    }
}
