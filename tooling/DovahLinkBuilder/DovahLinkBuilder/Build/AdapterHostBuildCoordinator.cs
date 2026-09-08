namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Coordinates building the production Adapter and packaging it with the Host.</summary>
public interface IAdapterHostBuildCoordinator
{
    /// <summary>
    /// Builds the production Adapter and packages it with the published Host into a Vortex-ready archive.
    /// </summary>
    /// <param name="request">The repository to build.</param>
    /// <param name="onOutput">An optional callback for build and packaging progress messages.</param>
    /// <param name="cancellationToken">A token that can cancel the build or packaging commands.</param>
    /// <returns>The path to the created archive.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the adapter manifest, the repository VERSION file, the packaging script, or the
    /// console-admin script or YAML configuration is missing.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the Visual Studio or Papyrus toolchain cannot be validated, the Adapter build
    /// fails, or the packaging script fails or does not report a written archive path.
    /// </exception>
    Task<AdapterHostBuildResult> BuildAsync(
        AdapterHostBuildRequest request,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAdapterHostBuildCoordinator"/>
public sealed class AdapterHostBuildCoordinator : IAdapterHostBuildCoordinator
{
    /// <summary>The configured Release build output directory name.</summary>
    private const string ReleaseBuildDirectory = "windows-x64-release";

    /// <summary>The compiled console-admin Papyrus script file name.</summary>
    private const string ConsoleAdminPexFileName = "DovahLinkAdmin.pex";

    /// <summary>The stdout line prefix `tooling/package_adapter_host.py` reports the written archive path with.</summary>
    private const string WrittenArchivePrefix = "Wrote ";

    /// <summary>Runs the external build and packaging commands.</summary>
    private readonly ICommandRunner commandRunner;

    /// <summary>Provides the Visual Studio toolchain used by the build.</summary>
    private readonly Func<VisualStudioToolchain> toolchainProvider;

    /// <summary>Provides the Papyrus compiler toolchain used to compile the console-admin script.</summary>
    private readonly Func<PapyrusToolchain> papyrusToolchainProvider;

    /// <summary>
    /// Initializes a coordinator for building the Adapter and packaging it with the Host.
    /// </summary>
    /// <param name="commandRunner">The command runner used to execute the build and packaging commands.</param>
    /// <param name="toolchainProvider">The provider used to obtain the Visual Studio toolchain.</param>
    /// <param name="papyrusToolchainProvider">The provider used to obtain the Papyrus compiler toolchain.</param>
    public AdapterHostBuildCoordinator(
        ICommandRunner commandRunner,
        Func<VisualStudioToolchain> toolchainProvider,
        Func<PapyrusToolchain> papyrusToolchainProvider)
    {
        this.commandRunner = commandRunner;
        this.toolchainProvider = toolchainProvider;
        this.papyrusToolchainProvider = papyrusToolchainProvider;
    }

    /// <inheritdoc/>
    public async Task<AdapterHostBuildResult> BuildAsync(
        AdapterHostBuildRequest request,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        string repositoryRoot = Path.GetFullPath(request.RepositoryRoot);
        string adapterRoot = Path.Combine(repositoryRoot, "adapter");
        string manifestPath = Path.Combine(adapterRoot, "vcpkg.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Could not find the adapter vcpkg manifest.", manifestPath);
        }

        string versionPath = Path.Combine(repositoryRoot, "VERSION");
        if (!File.Exists(versionPath))
        {
            throw new FileNotFoundException("Could not find the repository VERSION file.", versionPath);
        }

        string packagingScriptPath = Path.Combine(repositoryRoot, "tooling", "package_adapter_host.py");
        if (!File.Exists(packagingScriptPath))
        {
            throw new FileNotFoundException("Could not find the Adapter+Host packaging script.", packagingScriptPath);
        }

        // Every prerequisite this build needs is validated here, before any build command runs --
        // a missing compiler, script, or config file fails immediately instead of after the
        // multi-minute Release CMake build below.
        string consoleAdminRoot = Path.Combine(repositoryRoot, "console-admin");
        string consoleAdminScriptPath = Path.Combine(consoleAdminRoot, "DovahLinkAdmin.psc");
        if (!File.Exists(consoleAdminScriptPath))
        {
            throw new FileNotFoundException("Could not find the console-admin Papyrus script.", consoleAdminScriptPath);
        }
        string consoleAdminYamlPath = Path.Combine(consoleAdminRoot, "dovahlink.yaml");
        if (!File.Exists(consoleAdminYamlPath))
        {
            throw new FileNotFoundException("Could not find the console-admin YAML configuration.", consoleAdminYamlPath);
        }

        VisualStudioToolchain toolchain = VisualStudioToolchainLocator.Validate(toolchainProvider());
        PapyrusToolchain papyrusToolchain = PapyrusToolchainLocator.Validate(papyrusToolchainProvider());

        onOutput?.Invoke("Building the DovahLink Adapter Release binary...");
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
        foreach (BuildCommand command in BuildCommand.CreateReleaseBuild(adapterRoot, buildEnvironment))
        {
            int exitCode = await commandRunner.RunAsync(command, onOutput, onOutput, cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"The Adapter build failed with exit code {exitCode}.");
            }
        }

        string adapterBuildOutputRoot = Path.Combine(adapterRoot, "build", ReleaseBuildDirectory);

        onOutput?.Invoke("Compiling the DovahLink admin console script...");
        BuildCommand papyrusCommand = BuildCommand.CreatePapyrusCompile(consoleAdminScriptPath, papyrusToolchain, adapterBuildOutputRoot);
        int papyrusExitCode = await commandRunner.RunAsync(papyrusCommand, onOutput, onOutput, cancellationToken);
        if (papyrusExitCode != 0)
        {
            throw new InvalidOperationException($"The Papyrus compile failed with exit code {papyrusExitCode}.");
        }
        string consoleAdminPexPath = Path.Combine(adapterBuildOutputRoot, ConsoleAdminPexFileName);

        string outputRoot = Path.Combine(repositoryRoot, "tooling", "out");
        Directory.CreateDirectory(outputRoot);

        // Packaging is entirely owned by tooling/package_adapter_host.py (see AdapterHostPackager):
        // this orchestrates it as an external process rather than reimplementing the Vortex package
        // layout, so the layout has exactly one authoritative implementation.
        onOutput?.Invoke("Packaging the Adapter and Host...");
        var packagingOutputLines = new List<string>();
        var packagingCommand = new BuildCommand(
            "python",
            [
                "tooling/package_adapter_host.py",
                "--adapter-build-dir", adapterBuildOutputRoot,
                "--output-dir", outputRoot,
                "--console-admin-pex", consoleAdminPexPath,
                "--console-admin-yaml", consoleAdminYamlPath,
            ],
            repositoryRoot,
            new Dictionary<string, string>());
        int packagingExitCode = await commandRunner.RunAsync(
            packagingCommand,
            line =>
            {
                packagingOutputLines.Add(line);
                onOutput?.Invoke(line);
            },
            onOutput,
            cancellationToken);
        if (packagingExitCode != 0)
        {
            throw new InvalidOperationException($"Adapter+Host packaging failed with exit code {packagingExitCode}.");
        }

        string? archivePath = packagingOutputLines
            .LastOrDefault(line => line.StartsWith(WrittenArchivePrefix, StringComparison.Ordinal))
            ?[WrittenArchivePrefix.Length..];
        if (archivePath is null)
        {
            throw new InvalidOperationException("The packaging script did not report a written archive path.");
        }

        return new AdapterHostBuildResult(archivePath);
    }
}
