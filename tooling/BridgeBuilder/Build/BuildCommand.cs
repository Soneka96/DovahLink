namespace DovahLink.BridgeBuilder.Build;

/// <summary>Describes the Visual Studio and vcpkg paths required by a build.</summary>
/// <param name="VcvarsallPath">The path to the Visual Studio environment script.</param>
/// <param name="VcpkgRoot">The bundled vcpkg root.</param>
public sealed record VisualStudioToolchain(string VcvarsallPath, string VcpkgRoot);

/// <summary>Describes one external process without shell-interpolating its executable, arguments, directory, or environment.</summary>
/// <param name="ExecutablePath">The executable launched directly by the process runner.</param>
/// <param name="Arguments">The ordered argument values passed to the executable.</param>
/// <param name="WorkingDirectory">The directory in which the process runs.</param>
/// <param name="EnvironmentVariables">Environment values applied to the child process.</param>
public sealed record BuildCommand(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    /// <summary>Creates the isolated command that imports the Visual Studio x64 developer environment.</summary>
    /// <param name="toolchain">The validated Visual Studio toolchain to import.</param>
    /// <returns>A command whose standard output is the imported environment.</returns>
    public static BuildCommand CreateEnvironmentImport(VisualStudioToolchain toolchain)
    {
        VisualStudioToolchain validated = VisualStudioToolchainLocator.Validate(toolchain);
        string commandInterpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return new BuildCommand(
            commandInterpreter,
            ["/d", "/c", "call .\\vcvarsall.bat x64 >nul && set"],
            Path.GetDirectoryName(validated.VcvarsallPath)!,
            new Dictionary<string, string>());
    }

    /// <summary>Creates a fresh CMake configure and build command sequence for the Release bridge target.</summary>
    /// <param name="bridgeRoot">The bridge source directory containing the CMake presets.</param>
    /// <param name="environmentVariables">The imported Visual Studio environment, including vcpkg configuration.</param>
    /// <returns>The ordered configure and build commands.</returns>
    public static IReadOnlyList<BuildCommand> CreateReleaseBuild(
        string bridgeRoot,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        string workingDirectory = Path.GetFullPath(bridgeRoot);
        return
        [
            new BuildCommand(
                "cmake",
                ["--fresh", "--preset", "windows-x64-release"],
                workingDirectory,
                environmentVariables),
            new BuildCommand(
                "cmake",
                ["--build", "--preset", "windows-x64-release", "--target", "dovahlink_bridge_plugin"],
                workingDirectory,
                environmentVariables),
        ];
    }

    /// <summary>Creates the direct Papyrus compiler command for one script.</summary>
    /// <param name="scriptPath">The path to the <c>.psc</c> source file to compile.</param>
    /// <param name="toolchain">The Papyrus toolchain to invoke.</param>
    /// <param name="outputDirectory">The directory that receives the compiled <c>.pex</c>.</param>
    /// <returns>The validated Papyrus compiler invocation.</returns>
    /// <remarks>
    /// The script's own directory is prepended to the import paths: PapyrusCompiler.exe resolves even
    /// its direct compile target through the import search path, and fails with "unable to locate
    /// script" if that target's own directory is not included.
    /// </remarks>
    public static BuildCommand CreatePapyrusCompile(string scriptPath, PapyrusToolchain toolchain, string outputDirectory)
    {
        PapyrusToolchain validated = PapyrusToolchainLocator.Validate(toolchain);
        string fullScriptPath = Path.GetFullPath(scriptPath);
        string scriptDirectory = Path.GetDirectoryName(fullScriptPath)!;
        return new BuildCommand(
            validated.CompilerPath,
            [
                fullScriptPath,
                $"-i={scriptDirectory};{validated.ImportDirectory}",
                $"-f={validated.FlagsFilePath}",
                $"-o={Path.GetFullPath(outputDirectory)}",
            ],
            Path.GetDirectoryName(validated.CompilerPath)!,
            new Dictionary<string, string>());
    }
}
