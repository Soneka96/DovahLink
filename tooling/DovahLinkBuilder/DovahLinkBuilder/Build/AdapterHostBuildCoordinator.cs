using System.Diagnostics;
using System.IO;

namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Coordinates building the production Adapter and packaging it with the Host.</summary>
public interface IAdapterHostBuildCoordinator
{
    /// <summary>
    /// Builds the production Adapter and packages it with the published Host into a Vortex-ready archive.
    /// </summary>
    /// <param name="request">The repository to build.</param>
    /// <param name="onOutput">An optional callback for build and packaging progress messages.</param>
    /// <param name="onStage">
    /// An optional callback for structured progress across all eight <see cref="BuildStage"/> values:
    /// <see cref="BuildStage.ValidateRepository"/>, <see cref="BuildStage.ConfigureAdapter"/>,
    /// <see cref="BuildStage.BuildAdapter"/>, and <see cref="BuildStage.CompilePapyrus"/> are reported
    /// directly; <see cref="BuildStage.PublishHost"/>, <see cref="BuildStage.AssemblePackage"/>,
    /// <see cref="BuildStage.ValidatePackage"/>, and <see cref="BuildStage.CreateZip"/> are reported by
    /// parsing the canonical packaging script's own progress markers.
    /// </param>
    /// <param name="cancellationToken">A token that can cancel the build or packaging commands.</param>
    /// <returns>The path to the created archive.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the adapter manifest, the repository VERSION file, the packaging script, or the
    /// console-admin script or YAML configuration is missing.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the Visual Studio or Papyrus toolchain cannot be validated, the build output
    /// location cannot be created or accessed, the Adapter build fails, or the packaging script fails
    /// or does not report a written archive path.
    /// </exception>
    Task<AdapterHostBuildResult> BuildAsync(
        AdapterHostBuildRequest request,
        Action<string>? onOutput = null,
        Action<BuildStageEvent>? onStage = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAdapterHostBuildCoordinator"/>
public sealed class AdapterHostBuildCoordinator : IAdapterHostBuildCoordinator
{
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
        Action<BuildStageEvent>? onStage = null,
        CancellationToken cancellationToken = default)
    {
        string repositoryRoot = Path.GetFullPath(request.RepositoryRoot);
        string adapterRoot = Path.Combine(repositoryRoot, "adapter");
        string manifestPath = Path.Combine(adapterRoot, "vcpkg.json");
        string versionPath = Path.Combine(repositoryRoot, "VERSION");
        string packagingScriptPath = Path.Combine(repositoryRoot, "tooling", "package_adapter_host.py");
        string consoleAdminRoot = Path.Combine(repositoryRoot, "console-admin");
        string consoleAdminScriptPath = Path.Combine(consoleAdminRoot, "DovahLinkAdmin.psc");
        string consoleAdminYamlPath = Path.Combine(consoleAdminRoot, "dovahlink.yaml");
        string presetName = request.Profile.ToCMakePreset();
        string adapterBuildOutputRoot = Path.Combine(adapterRoot, "build", presetName);
        string consoleAdminPexPath = Path.Combine(adapterBuildOutputRoot, ConsoleAdminPexFileName);

        string outputRoot = request.OutputRootOverride ?? request.Profile.ToOutputRoot(repositoryRoot);

        (VisualStudioToolchain toolchain, PapyrusToolchain papyrusToolchain) = await RunStageAsync(
            BuildStage.ValidateRepository,
            onStage,
            () =>
            {
                if (!File.Exists(manifestPath))
                {
                    throw new FileNotFoundException("Could not find the adapter vcpkg manifest.", manifestPath);
                }

                if (!File.Exists(versionPath))
                {
                    throw new FileNotFoundException("Could not find the repository VERSION file.", versionPath);
                }

                if (!File.Exists(packagingScriptPath))
                {
                    throw new FileNotFoundException("Could not find the Adapter+Host packaging script.", packagingScriptPath);
                }

                // Every prerequisite this build needs is validated here, before any build command runs --
                // a missing compiler, script, config file, or unusable output destination fails
                // immediately instead of after the multi-minute CMake build below.
                if (!File.Exists(consoleAdminScriptPath))
                {
                    throw new FileNotFoundException("Could not find the console-admin Papyrus script.", consoleAdminScriptPath);
                }

                if (!File.Exists(consoleAdminYamlPath))
                {
                    throw new FileNotFoundException("Could not find the console-admin YAML configuration.", consoleAdminYamlPath);
                }

                try
                {
                    Directory.CreateDirectory(outputRoot);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException($"Could not create or access the build output location: {outputRoot}", exception);
                }

                VisualStudioToolchain validatedToolchain = VisualStudioToolchainLocator.Validate(toolchainProvider());
                PapyrusToolchain validatedPapyrusToolchain = PapyrusToolchainLocator.Validate(papyrusToolchainProvider());
                return Task.FromResult((validatedToolchain, validatedPapyrusToolchain));
            });

        IReadOnlyList<BuildCommand> releaseCommands = await RunStageAsync(
            BuildStage.ConfigureAdapter,
            onStage,
            async () =>
            {
                onOutput?.Invoke($"Building the DovahLink Adapter {request.Profile} binary...");
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
                IReadOnlyList<BuildCommand> commands = BuildCommand.CreateBuild(adapterRoot, buildEnvironment, presetName);
                int configureExitCode = await commandRunner.RunAsync(commands[0], onOutput, onOutput, cancellationToken);
                if (configureExitCode != 0)
                {
                    throw new InvalidOperationException($"The Adapter build failed with exit code {configureExitCode}.");
                }

                return commands;
            });

        await RunStageAsync(
            BuildStage.BuildAdapter,
            onStage,
            async () =>
            {
                int exitCode = await commandRunner.RunAsync(releaseCommands[1], onOutput, onOutput, cancellationToken);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"The Adapter build failed with exit code {exitCode}.");
                }
            });

        await RunStageAsync(
            BuildStage.CompilePapyrus,
            onStage,
            async () =>
            {
                onOutput?.Invoke("Compiling the DovahLink admin console script...");
                BuildCommand papyrusCommand = BuildCommand.CreatePapyrusCompile(consoleAdminScriptPath, papyrusToolchain, adapterBuildOutputRoot);
                int papyrusExitCode = await commandRunner.RunAsync(papyrusCommand, onOutput, onOutput, cancellationToken);
                if (papyrusExitCode != 0)
                {
                    throw new InvalidOperationException($"The Papyrus compile failed with exit code {papyrusExitCode}.");
                }
            });

        // Packaging is entirely owned by tooling/package_adapter_host.py (see AdapterHostPackager):
        // this orchestrates it as an external process rather than reimplementing the Vortex package
        // layout, so the layout has exactly one authoritative implementation. Its four stages
        // (PublishHost, AssemblePackage, ValidatePackage, CreateZip) are reported through onStage by
        // parsing the "##stage <name> <start|done>" markers that script prints (BuildStageProgressParser);
        // a stage that starts but never reports "done" before the process exits nonzero is reported
        // as Failed here, since the script never emits a "done" marker for a stage that failed.
        onOutput?.Invoke("Packaging the Adapter and Host...");
        var packagingOutputLines = new List<string>();
        // A single in-flight stage is tracked, not a stack: the packaging script's four stages run
        // strictly one at a time, each one's "start"/"done" pair fully bracketing before the next
        // stage's "start" ever prints.
        BuildStage? runningPackagingStage = null;
        Stopwatch? runningPackagingStageStopwatch = null;
        var packagingCommand = new BuildCommand(
            "python",
            [
                "tooling/package_adapter_host.py",
                "--adapter-build-dir", adapterBuildOutputRoot,
                "--output-dir", outputRoot,
                "--console-admin-pex", consoleAdminPexPath,
                "--console-admin-yaml", consoleAdminYamlPath,
                "--configuration", request.Profile.ToDotnetConfiguration(),
                "--profile-label", request.Profile.ToOutputSegment(),
            ],
            repositoryRoot,
            new Dictionary<string, string>());
        int packagingExitCode;
        try
        {
            packagingExitCode = await commandRunner.RunAsync(
                packagingCommand,
                line =>
                {
                    packagingOutputLines.Add(line);
                    onOutput?.Invoke(line);
                    switch (BuildStageProgressParser.TryParse(line))
                    {
                        case { Status: BuildStageStatus.Running } running:
                            runningPackagingStage = running.Stage;
                            runningPackagingStageStopwatch = Stopwatch.StartNew();
                            onStage?.Invoke(new BuildStageEvent(running.Stage, BuildStageStatus.Running));
                            break;
                        case { Status: BuildStageStatus.Succeeded } succeeded:
                            onStage?.Invoke(new BuildStageEvent(succeeded.Stage, BuildStageStatus.Succeeded, runningPackagingStageStopwatch?.Elapsed));
                            runningPackagingStage = null;
                            runningPackagingStageStopwatch = null;
                            break;
                    }
                },
                onOutput,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Not wrapped in RunStageAsync, since these four stages share one external process
            // rather than one call per stage: without this, cancelling mid-packaging would leave
            // whichever stage was running reported as stuck Running forever, since the script never
            // gets the chance to print its own "done" (or lack of one) for a killed process.
            if (runningPackagingStage is { } cancelledStage)
            {
                onStage?.Invoke(new BuildStageEvent(cancelledStage, BuildStageStatus.Cancelled, runningPackagingStageStopwatch?.Elapsed));
            }

            throw;
        }

        if (packagingExitCode != 0)
        {
            if (runningPackagingStage is { } failedStage)
            {
                onStage?.Invoke(new BuildStageEvent(failedStage, BuildStageStatus.Failed, runningPackagingStageStopwatch?.Elapsed));
            }

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

    /// <summary>Runs <paramref name="action"/> as one reported build stage, without a result value.</summary>
    /// <param name="stage">The stage being run.</param>
    /// <param name="onStage">The optional stage-progress callback to report through.</param>
    /// <param name="action">The stage's work.</param>
    /// <exception cref="Exception">Rethrows whatever exception <paramref name="action"/> throws, after reporting <see cref="BuildStageStatus.Cancelled"/> for an <see cref="OperationCanceledException"/> or <see cref="BuildStageStatus.Failed"/> for any other exception.</exception>
    private static async Task RunStageAsync(BuildStage stage, Action<BuildStageEvent>? onStage, Func<Task> action)
    {
        await RunStageAsync<object?>(stage, onStage, async () =>
        {
            await action();
            return null;
        });
    }

    /// <summary>Runs <paramref name="action"/> as one reported build stage, returning its result.</summary>
    /// <typeparam name="T">The type of value <paramref name="action"/> produces.</typeparam>
    /// <param name="stage">The stage being run.</param>
    /// <param name="onStage">The optional stage-progress callback to report through.</param>
    /// <param name="action">The stage's work.</param>
    /// <returns>The value <paramref name="action"/> produced.</returns>
    /// <exception cref="Exception">Rethrows whatever exception <paramref name="action"/> throws, after reporting <see cref="BuildStageStatus.Cancelled"/> for an <see cref="OperationCanceledException"/> or <see cref="BuildStageStatus.Failed"/> for any other exception.</exception>
    private static async Task<T> RunStageAsync<T>(BuildStage stage, Action<BuildStageEvent>? onStage, Func<Task<T>> action)
    {
        onStage?.Invoke(new BuildStageEvent(stage, BuildStageStatus.Running));
        Stopwatch stopwatch = Stopwatch.StartNew();
        T result;
        try
        {
            result = await action();
        }
        catch (OperationCanceledException)
        {
            onStage?.Invoke(new BuildStageEvent(stage, BuildStageStatus.Cancelled, stopwatch.Elapsed));
            throw;
        }
        catch
        {
            onStage?.Invoke(new BuildStageEvent(stage, BuildStageStatus.Failed, stopwatch.Elapsed));
            throw;
        }

        onStage?.Invoke(new BuildStageEvent(stage, BuildStageStatus.Succeeded, stopwatch.Elapsed));
        return result;
    }
}
