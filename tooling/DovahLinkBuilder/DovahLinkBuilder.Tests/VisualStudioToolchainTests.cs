using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies Visual Studio toolchain discovery.</summary>
public sealed class VisualStudioToolchainTests
{
    /// <summary>Returns the configured root, discovered roots, then every supported standard edition path.</summary>
    [Fact]
    public void ReturnsConfiguredAndStandardInstallationRootsInSearchOrder()
    {
        const string configuredInstallation = @"D:\Custom Visual Studio";
        const string discoveredInstallation = @"E:\Installed Elsewhere\VS2026";
        const string programFiles = @"C:\Program Files";
        const string programFilesX86 = @"C:\Program Files (x86)";

        IEnumerable<string> roots = VisualStudioToolchainLocator.GetDefaultInstallationRoots(
            programFiles,
            programFilesX86,
            configuredInstallation,
            [discoveredInstallation]);

        string[] expectedRoots =
        [
            configuredInstallation,
            discoveredInstallation,
            Path.Combine(programFiles, "Microsoft Visual Studio", "18", "Community"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "18", "Professional"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "18", "Enterprise"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "18", "BuildTools"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Community"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Professional"),
            Path.Combine(programFiles, "Microsoft Visual Studio", "2022", "Enterprise"),
            Path.Combine(programFilesX86, "Microsoft Visual Studio", "2022", "BuildTools"),
        ];

        Assert.Equal(expectedRoots, roots);
    }

    /// <summary>Finds an installation containing the required toolchain files.</summary>
    [Fact]
    public void FindsTheFirstInstallationWithTheRequiredFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string installationRoot = Path.Combine(temporaryDirectory.Path, "Community");
        string vcvarsallPath = Path.Combine(installationRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(installationRoot, "VC", "vcpkg");
        string cmakePath = Path.Combine(installationRoot, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "CMake", "bin", "cmake.exe");
        string ninjaPath = Path.Combine(installationRoot, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(cmakePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ninjaPath)!);
        File.WriteAllText(vcvarsallPath, "@echo off");
        File.WriteAllText(cmakePath, "cmake");
        File.WriteAllText(ninjaPath, "ninja");

        VisualStudioToolchain toolchain = VisualStudioToolchainLocator.Find([installationRoot]);

        Assert.Equal(vcvarsallPath, toolchain.VcvarsallPath);
        Assert.Equal(vcpkgRoot, toolchain.VcpkgRoot);
        Assert.Equal(Path.Combine(installationRoot, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "CMake", "bin", "cmake.exe"), toolchain.CMakePath);
        Assert.Equal(ninjaPath, toolchain.NinjaPath);
    }

    /// <summary>Finds the default Visual Studio 2026 installation under the version 18 directory.</summary>
    [Fact]
    public void FindsTheDefaultVisualStudio2026Installation()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string programFiles = Path.Combine(temporaryDirectory.Path, "Program Files");
        string programFilesX86 = Path.Combine(temporaryDirectory.Path, "Program Files (x86)");
        string installationName = Path.Combine("Microsoft Visual Studio", "18", "Community");
        VisualStudioToolchain expected = Fixtures.BuildVisualStudioToolchain(programFiles, installationName);

        IEnumerable<string> roots = VisualStudioToolchainLocator.GetDefaultInstallationRoots(
            programFiles,
            programFilesX86,
            visualStudioInstall: null);

        VisualStudioToolchain actual = VisualStudioToolchainLocator.Find(roots);

        Assert.Equal(expected.VcvarsallPath, actual.VcvarsallPath);
        Assert.Equal(expected.VcpkgRoot, actual.VcpkgRoot);
    }

    /// <summary>Finds the default Visual Studio 2022 installation when Visual Studio 2026 is unavailable.</summary>
    [Fact]
    public void FindsTheDefaultVisualStudio2022InstallationWhenVisualStudio2026IsUnavailable()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string programFiles = Path.Combine(temporaryDirectory.Path, "Program Files");
        string programFilesX86 = Path.Combine(temporaryDirectory.Path, "Program Files (x86)");
        string installationName = Path.Combine("Microsoft Visual Studio", "2022", "Community");
        VisualStudioToolchain expected = Fixtures.BuildVisualStudioToolchain(programFiles, installationName);

        IEnumerable<string> roots = VisualStudioToolchainLocator.GetDefaultInstallationRoots(
            programFiles,
            programFilesX86,
            visualStudioInstall: null);

        VisualStudioToolchain actual = VisualStudioToolchainLocator.Find(roots);

        Assert.Equal(expected.VcvarsallPath, actual.VcvarsallPath);
        Assert.Equal(expected.VcpkgRoot, actual.VcpkgRoot);
    }

    /// <summary>Builds the vswhere query for the supported C++ Visual Studio versions.</summary>
    [Fact]
    public void BuildsTheVsWhereQueryForSupportedCPlusPlusInstallations()
    {
        Assert.Equal(
            [
                "-products", "*",
                "-version", "[17.0,19.0)",
                "-requires",
                "Microsoft.VisualStudio.Workload.NativeDesktop",
                "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                "-sort",
                "-property", "installationPath",
                "-utf8",
            ],
            VisualStudioToolchainLocator.GetVsWhereArguments());
    }

    /// <summary>Parses multiple discovered installation paths while skipping blank lines and trimming whitespace.</summary>
    [Fact]
    public void ParsesVsWhereInstallationPaths()
    {
        IReadOnlyList<string> roots = VisualStudioToolchainLocator.ParseVsWhereInstallationRoots(
            $"  C:\\VS2026\\Community  {Environment.NewLine}{Environment.NewLine} D:\\Custom VS2022 ");

        Assert.Equal([@"C:\VS2026\Community", @"D:\Custom VS2022"], roots);
    }

    /// <summary>Rejects installations missing required toolchain files.</summary>
    [Fact]
    public void RejectsInstallationsWithoutTheRequiredToolchainFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() =>
            VisualStudioToolchainLocator.Find([temporaryDirectory.Path]));
    }

    /// <summary>Skips a Visual Studio installation without bundled Ninja and finds the next complete installation.</summary>
    [Fact]
    public void FindsTheNextInstallationWhenAnEarlierInstallationLacksBundledNinja()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string incompleteRoot = Path.Combine(temporaryDirectory.Path, "Newer VS without CMake");
        string incompleteVcvarsall = Path.Combine(incompleteRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string incompleteCMake = Path.Combine(incompleteRoot, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "CMake", "bin", "cmake.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(incompleteVcvarsall)!);
        Directory.CreateDirectory(Path.GetDirectoryName(incompleteCMake)!);
        Directory.CreateDirectory(Path.Combine(incompleteRoot, "VC", "vcpkg"));
        File.WriteAllText(incompleteVcvarsall, "@echo off");
        File.WriteAllText(incompleteCMake, "cmake");
        VisualStudioToolchain expected = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path, "Older VS with CMake");

        VisualStudioToolchain actual = VisualStudioToolchainLocator.Find(
            [incompleteRoot, Path.Combine(temporaryDirectory.Path, "Older VS with CMake")]);

        Assert.Equal(expected, actual);
    }

    /// <summary>Rejects a direct toolchain value whose environment script is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingEnvironmentScript()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string vcpkgRoot = Path.Combine(temporaryDirectory.Path, "vcpkg");
        string cmakePath = Path.Combine(temporaryDirectory.Path, "cmake.exe");
        string ninjaPath = Path.Combine(temporaryDirectory.Path, "ninja.exe");
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(cmakePath, "cmake");
        File.WriteAllText(ninjaPath, "ninja");

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(Path.Combine(temporaryDirectory.Path, "missing.bat"), vcpkgRoot, cmakePath, ninjaPath)));
    }

    /// <summary>Rejects an existing batch file that is not the supported Visual Studio environment script.</summary>
    [Fact]
    public void ValidateRejectsAnUnexpectedEnvironmentScriptName()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string batchPath = Path.Combine(temporaryDirectory.Path, "other.bat");
        string vcpkgRoot = Path.Combine(temporaryDirectory.Path, "vcpkg");
        string cmakePath = Path.Combine(temporaryDirectory.Path, "cmake.exe");
        string ninjaPath = Path.Combine(temporaryDirectory.Path, "ninja.exe");
        File.WriteAllText(batchPath, "@echo off");
        File.WriteAllText(cmakePath, "cmake");
        File.WriteAllText(ninjaPath, "ninja");
        Directory.CreateDirectory(vcpkgRoot);

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(batchPath, vcpkgRoot, cmakePath, ninjaPath)));
    }

    /// <summary>Rejects a direct toolchain value whose bundled vcpkg directory is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingVcpkgDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string vcvarsallPath = Path.Combine(temporaryDirectory.Path, "vcvarsall.bat");
        string cmakePath = Path.Combine(temporaryDirectory.Path, "cmake.exe");
        string ninjaPath = Path.Combine(temporaryDirectory.Path, "ninja.exe");
        File.WriteAllText(vcvarsallPath, "@echo off");
        File.WriteAllText(cmakePath, "cmake");
        File.WriteAllText(ninjaPath, "ninja");

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(vcvarsallPath, Path.Combine(temporaryDirectory.Path, "missing-vcpkg"), cmakePath, ninjaPath)));
    }

    /// <summary>Rejects a direct toolchain value whose bundled CMake executable is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingCMakeExecutable()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string vcvarsallPath = Path.Combine(temporaryDirectory.Path, "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(temporaryDirectory.Path, "vcpkg");
        string ninjaPath = Path.Combine(temporaryDirectory.Path, "ninja.exe");
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off");
        File.WriteAllText(ninjaPath, "ninja");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(vcvarsallPath, vcpkgRoot, Path.Combine(temporaryDirectory.Path, "missing-cmake.exe"), ninjaPath)));

        Assert.Contains("CMake executable does not exist", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a direct toolchain value whose bundled Ninja executable is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingNinjaExecutable()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string vcvarsallPath = Path.Combine(temporaryDirectory.Path, "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(temporaryDirectory.Path, "vcpkg");
        string cmakePath = Path.Combine(temporaryDirectory.Path, "cmake.exe");
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off");
        File.WriteAllText(cmakePath, "cmake");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(vcvarsallPath, vcpkgRoot, cmakePath, Path.Combine(temporaryDirectory.Path, "missing-ninja.exe"))));

        Assert.Contains("Ninja executable does not exist", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Reports <see cref="ToolchainAvailability.Found"/> for an installation with the required files.</summary>
    [Fact]
    public void TryFindReturnsFoundForAnInstallationWithTheRequiredFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string installationRoot = Path.Combine(temporaryDirectory.Path, "Community");
        string vcvarsallPath = Path.Combine(installationRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(installationRoot, "VC", "vcpkg");
        string cmakePath = Path.Combine(installationRoot, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "CMake", "bin", "cmake.exe");
        string ninjaPath = Path.Combine(installationRoot, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(cmakePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ninjaPath)!);
        File.WriteAllText(vcvarsallPath, "@echo off");
        File.WriteAllText(cmakePath, "cmake");
        File.WriteAllText(ninjaPath, "ninja");

        ToolchainCheckResult result = VisualStudioToolchainLocator.TryFind([installationRoot]);

        Assert.Equal(ToolchainAvailability.Found, result.Availability);
        Assert.Equal(vcvarsallPath, result.Detail);
        Assert.Null(result.RemediationHint);
    }

    /// <summary>Reports <see cref="ToolchainAvailability.Missing"/> when no root has the required files.</summary>
    [Fact]
    public void TryFindReturnsMissingWhenNoInstallationHasTheRequiredFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        ToolchainCheckResult result = VisualStudioToolchainLocator.TryFind([temporaryDirectory.Path]);

        Assert.Equal(ToolchainAvailability.Missing, result.Availability);
        Assert.Null(result.Detail);
        Assert.NotNull(result.RemediationHint);
    }

    /// <summary>Reports <see cref="ToolchainAvailability.CouldNotCheck"/> when a root cannot be evaluated.</summary>
    [Fact]
    public void TryFindReturnsCouldNotCheckWhenARootCannotBeEvaluated()
    {
        ToolchainCheckResult result = VisualStudioToolchainLocator.TryFind([null!]);

        Assert.Equal(ToolchainAvailability.CouldNotCheck, result.Availability);
        Assert.Null(result.Detail);
        Assert.NotNull(result.RemediationHint);
    }
}
