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
