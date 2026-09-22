using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies structured command construction for CMake, Papyrus, and Visual Studio environment import.</summary>
public sealed class BuildCommandTests
{
    /// <summary>Builds direct CMake commands with ordered arguments and an inherited environment.</summary>
    [Fact]
    public void BuildsStructuredReleaseCommands()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path);
        var environment = new Dictionary<string, string> { ["VCPKG_ROOT"] = @"C:\VS & tools\vcpkg" };

        IReadOnlyList<BuildCommand> commands = BuildCommand.CreateBuild(
            @"C:\repository & workspace\adapter",
            environment,
            "windows-x64-release",
            toolchain);

        Assert.Collection(
            commands,
            configure =>
            {
                Assert.Equal(toolchain.CMakePath, configure.ExecutablePath);
                Assert.Equal(
                    ["--fresh", "--preset", "windows-x64-release", $"-DCMAKE_MAKE_PROGRAM={toolchain.NinjaPath}"],
                    configure.Arguments);
                Assert.Equal(Path.GetFullPath(@"C:\repository & workspace\adapter"), configure.WorkingDirectory);
                Assert.Same(environment, configure.EnvironmentVariables);
            },
            build =>
            {
                Assert.Equal(toolchain.CMakePath, build.ExecutablePath);
                Assert.Equal(
                    ["--build", "--preset", "windows-x64-release", "--target", "dovahlink_adapter_plugin"],
                    build.Arguments);
                Assert.Equal(Path.GetFullPath(@"C:\repository & workspace\adapter"), build.WorkingDirectory);
                Assert.Same(environment, build.EnvironmentVariables);
            });
    }

    /// <summary>Builds direct CMake commands for a different preset, proving the preset name is not hardcoded.</summary>
    [Fact]
    public void BuildsStructuredCommandsForTheSuppliedPreset()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path);
        var environment = new Dictionary<string, string>();

        IReadOnlyList<BuildCommand> commands = BuildCommand.CreateBuild(
            @"C:\repository\adapter",
            environment,
            "windows-x64-debug",
            toolchain);

        Assert.Collection(
            commands,
            configure =>
            {
                Assert.Equal(toolchain.CMakePath, configure.ExecutablePath);
                Assert.Equal(
                    ["--fresh", "--preset", "windows-x64-debug", $"-DCMAKE_MAKE_PROGRAM={toolchain.NinjaPath}"],
                    configure.Arguments);
            },
            build => Assert.Equal(
                ["--build", "--preset", "windows-x64-debug", "--target", "dovahlink_adapter_plugin"],
                build.Arguments));
    }

    /// <summary>Builds a direct Papyrus compiler command with named arguments and the compiler's own directory.</summary>
    [Fact]
    public void BuildsStructuredPapyrusCompileCommand()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        PapyrusToolchain toolchain = Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path);
        string scriptPath = Path.Combine(temporaryDirectory.Path, "console-admin", "DovahLinkAdmin.psc");
        string outputDirectory = Path.Combine(temporaryDirectory.Path, "out");

        BuildCommand command = BuildCommand.CreatePapyrusCompile(scriptPath, toolchain, outputDirectory);

        Assert.Equal(toolchain.CompilerPath, command.ExecutablePath);
        Assert.Equal(
            [
                Path.GetFullPath(scriptPath),
                $"-i={Path.GetDirectoryName(Path.GetFullPath(scriptPath))};{toolchain.ImportDirectory}",
                $"-f={toolchain.FlagsFilePath}",
                $"-o={Path.GetFullPath(outputDirectory)}",
            ],
            command.Arguments);
        Assert.Equal(Path.GetDirectoryName(toolchain.CompilerPath), command.WorkingDirectory);
        Assert.Empty(command.EnvironmentVariables);
    }

    /// <summary>Normalizes a non-canonical script and output path into their full, canonical forms.</summary>
    [Fact]
    public void NormalizesTheScriptAndOutputPathsToFullPaths()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        PapyrusToolchain toolchain = Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path);
        string scriptPath = Path.Combine(temporaryDirectory.Path, "console-admin", "..", "console-admin", "DovahLinkAdmin.psc");
        string outputDirectory = Path.Combine(temporaryDirectory.Path, "out", "..", "out");

        BuildCommand command = BuildCommand.CreatePapyrusCompile(scriptPath, toolchain, outputDirectory);

        Assert.DoesNotContain("..", command.Arguments[0]);
        Assert.Equal(Path.GetFullPath(scriptPath), command.Arguments[0]);
        Assert.Equal($"-o={Path.GetFullPath(outputDirectory)}", command.Arguments[3]);
    }

    /// <summary>Builds the structured environment-import command from the validated Visual Studio toolchain.</summary>
    [Fact]
    public void BuildsTheEnvironmentImportCommandFromTheValidatedToolchain()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path);

        BuildCommand command = BuildCommand.CreateEnvironmentImport(toolchain);

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), command.ExecutablePath);
        Assert.Equal(["/d", "/c", "call .\\vcvarsall.bat x64 >nul && set"], command.Arguments);
        Assert.Equal(Path.GetDirectoryName(toolchain.VcvarsallPath), command.WorkingDirectory);
        Assert.Empty(command.EnvironmentVariables);
    }

    /// <summary>
    /// Builds the environment-import command unchanged when the installation root contains an
    /// ampersand, proving the working directory is passed as structured process data rather than
    /// interpolated into the <c>cmd.exe</c> command text, where an ampersand would start a new
    /// chained command.
    /// </summary>
    [Fact]
    public void BuildsTheEnvironmentImportCommandForAnInstallationRootContainingAnAmpersand()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(
            temporaryDirectory.Path,
            Path.Combine("Visual Studio & SDKs", "VS2026"));

        BuildCommand command = BuildCommand.CreateEnvironmentImport(toolchain);

        Assert.Equal(Path.GetDirectoryName(toolchain.VcvarsallPath), command.WorkingDirectory);
        Assert.Equal(["/d", "/c", "call .\\vcvarsall.bat x64 >nul && set"], command.Arguments);
    }

    /// <summary>Builds the direct dumpbin command used to reject dynamically imported Adapter runtimes.</summary>
    [Fact]
    public void BuildsTheAdapterDependencyInspectionCommand()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var environment = new Dictionary<string, string> { ["PATH"] = @"C:\Visual Studio\bin" };
        string pluginPath = Path.Combine(temporaryDirectory.Path, "adapter", "..", "adapter", "dovahlink_adapter_plugin.dll");

        BuildCommand command = BuildCommand.CreateAdapterDependencyInspection(pluginPath, environment);

        string fullPluginPath = Path.GetFullPath(pluginPath);
        Assert.Equal("dumpbin.exe", command.ExecutablePath);
        Assert.Equal(["/DEPENDENTS", fullPluginPath], command.Arguments);
        Assert.Equal(Path.GetDirectoryName(fullPluginPath), command.WorkingDirectory);
        Assert.Same(environment, command.EnvironmentVariables);
    }

    /// <summary>Parses environment values containing equals signs and replaces inherited vcpkg configuration.</summary>
    [Fact]
    public void CreatesTheCMakeEnvironmentFromVisualStudioOutput()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path);

        IReadOnlyDictionary<string, string> environment = VisualStudioEnvironment.Create(
            [
                "PATH=C:\\tools",
                "VALUE=left=right",
                "=C:=C:\\ignored-drive-entry",
                "malformed",
                "VCPKG_ROOT=C:\\old",
            ],
            toolchain);

        Assert.Equal(@"C:\tools", environment["PATH"]);
        Assert.Equal("left=right", environment["VALUE"]);
        Assert.Equal(toolchain.VcpkgRoot, environment["VCPKG_ROOT"]);
        Assert.False(environment.ContainsKey(""));
    }
}
