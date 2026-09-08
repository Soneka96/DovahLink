using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies structured command construction for CMake, Papyrus, and Visual Studio environment import.</summary>
public sealed class BuildCommandTests
{
    /// <summary>Builds direct CMake commands with ordered arguments and an inherited environment.</summary>
    [Fact]
    public void BuildsStructuredReleaseCommands()
    {
        var environment = new Dictionary<string, string> { ["VCPKG_ROOT"] = @"C:\VS & tools\vcpkg" };

        IReadOnlyList<BuildCommand> commands = BuildCommand.CreateReleaseBuild(
            @"C:\repository & workspace\adapter",
            environment);

        Assert.Collection(
            commands,
            configure =>
            {
                Assert.Equal("cmake", configure.ExecutablePath);
                Assert.Equal(["--fresh", "--preset", "windows-x64-release"], configure.Arguments);
                Assert.Equal(Path.GetFullPath(@"C:\repository & workspace\adapter"), configure.WorkingDirectory);
                Assert.Same(environment, configure.EnvironmentVariables);
            },
            build =>
            {
                Assert.Equal("cmake", build.ExecutablePath);
                Assert.Equal(
                    ["--build", "--preset", "windows-x64-release", "--target", "dovahlink_adapter_plugin"],
                    build.Arguments);
                Assert.Equal(Path.GetFullPath(@"C:\repository & workspace\adapter"), build.WorkingDirectory);
                Assert.Same(environment, build.EnvironmentVariables);
            });
    }

    /// <summary>Builds a direct Papyrus compiler command with named arguments and the compiler's own directory.</summary>
    [Fact]
    public void BuildsStructuredPapyrusCompileCommand()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        PapyrusToolchain toolchain = CreatePapyrusToolchain(temporaryDirectory.Path);
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
        PapyrusToolchain toolchain = CreatePapyrusToolchain(temporaryDirectory.Path);
        string scriptPath = Path.Combine(temporaryDirectory.Path, "console-admin", "..", "console-admin", "DovahLinkAdmin.psc");
        string outputDirectory = Path.Combine(temporaryDirectory.Path, "out", "..", "out");

        BuildCommand command = BuildCommand.CreatePapyrusCompile(scriptPath, toolchain, outputDirectory);

        Assert.DoesNotContain("..", command.Arguments[0]);
        Assert.Equal(Path.GetFullPath(scriptPath), command.Arguments[0]);
        Assert.Equal($"-o={Path.GetFullPath(outputDirectory)}", command.Arguments[3]);
    }

    /// <summary>Parses environment values containing equals signs and replaces inherited vcpkg configuration.</summary>
    [Fact]
    public void CreatesTheCMakeEnvironmentFromVisualStudioOutput()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain toolchain = CreateToolchain(temporaryDirectory.Path);

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

    /// <summary>Creates the validated toolchain files used by command-construction and environment tests.</summary>
    /// <param name="repositoryRoot">The temporary root under which to create the installation.</param>
    /// <param name="installationName">The installation directory name, including any path characters under test.</param>
    /// <returns>The created environment-script and vcpkg paths.</returns>
    private static VisualStudioToolchain CreateToolchain(
        string repositoryRoot,
        string installationName = "Visual Studio")
    {
        string installationRoot = Path.Combine(repositoryRoot, installationName);
        string vcvarsallPath = Path.Combine(installationRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(installationRoot, "VC", "vcpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off\n");
        return new VisualStudioToolchain(vcvarsallPath, vcpkgRoot);
    }

    /// <summary>Creates the validated Papyrus toolchain files used by compile-command tests.</summary>
    /// <param name="repositoryRoot">The temporary root under which to create the installation.</param>
    /// <returns>The created compiler, import directory, and flags file paths.</returns>
    private static PapyrusToolchain CreatePapyrusToolchain(string repositoryRoot)
    {
        string installationRoot = Path.Combine(repositoryRoot, "Skyrim Special Edition");
        string compilerPath = Path.Combine(installationRoot, "Papyrus Compiler", "PapyrusCompiler.exe");
        string importDirectory = Path.Combine(installationRoot, "Data", "Scripts", "Source");
        string flagsFilePath = Path.Combine(importDirectory, "TESV_Papyrus_Flags.flg");
        Directory.CreateDirectory(Path.GetDirectoryName(compilerPath)!);
        Directory.CreateDirectory(importDirectory);
        File.WriteAllText(compilerPath, "compiler");
        File.WriteAllText(flagsFilePath, "flags");
        return new PapyrusToolchain(compilerPath, importDirectory, flagsFilePath);
    }

    /// <summary>Creates and removes an isolated temporary directory for a test.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>Creates a unique temporary directory.</summary>
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DovahLinkBuilderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the temporary directory path.</summary>
        public string Path { get; }

        /// <summary>Removes the temporary directory when the test completes.</summary>
        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
