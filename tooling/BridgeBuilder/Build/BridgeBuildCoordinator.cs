using System.IO.Compression;
using DovahLink.BridgeBuilder.Packaging;

namespace DovahLink.BridgeBuilder.Build;

public sealed record BridgeBuildRequest(string RepositoryRoot, PackageChannel Channel);

public sealed record BridgeBuildResult(ArtifactPlan Plan, string ArchivePath);

public sealed class BridgeBuildCoordinator
{
    private const string ReleaseBuildDirectory = "windows-x64-release";

    private readonly ICommandRunner commandRunner;
    private readonly Func<VisualStudioToolchain> toolchainProvider;

    /// <summary>
    /// Initializes a coordinator for building and packaging the bridge.
    /// </summary>
    /// <param name="commandRunner">The command runner used to execute the build.</param>
    /// <param name="toolchainProvider">The provider used to obtain the Visual Studio toolchain.</param>
    public BridgeBuildCoordinator(
        ICommandRunner commandRunner,
        Func<VisualStudioToolchain> toolchainProvider)
    {
        this.commandRunner = commandRunner;
        this.toolchainProvider = toolchainProvider;
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
        VisualStudioToolchain toolchain = toolchainProvider();

        onOutput?.Invoke("Building the DovahLink bridge Release target...");
        int exitCode = await commandRunner.RunAsync(
            BuildCommand.Create(toolchain),
            bridgeRoot,
            onOutput,
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"The bridge build failed with exit code {exitCode}.");
        }

        string buildOutputRoot = Path.Combine(bridgeRoot, "build", ReleaseBuildDirectory);
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
