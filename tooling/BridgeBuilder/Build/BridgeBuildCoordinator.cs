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

    public BridgeBuildCoordinator(
        ICommandRunner commandRunner,
        Func<VisualStudioToolchain> toolchainProvider)
    {
        this.commandRunner = commandRunner;
        this.toolchainProvider = toolchainProvider;
    }

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
