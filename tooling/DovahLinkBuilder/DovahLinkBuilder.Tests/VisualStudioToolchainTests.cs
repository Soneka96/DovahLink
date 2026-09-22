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

    /// <summary>Returns no installation roots when vswhere's entire output is blank or whitespace-only.</summary>
    [Fact]
    public void ParsesBlankOrWhitespaceOnlyVsWhereOutputAsNoInstallationRoots()
    {
        IReadOnlyList<string> roots = VisualStudioToolchainLocator.ParseVsWhereInstallationRoots(
            $"   {Environment.NewLine}  \t  {Environment.NewLine}{Environment.NewLine}   ");

        Assert.Empty(roots);
    }

    /// <summary>Omits a blank or whitespace-only configured <c>VSINSTALLDIR</c> from the search order instead of searching it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OmitsABlankOrWhitespaceConfiguredInstallationFromTheSearchOrder(string visualStudioInstall)
    {
        const string programFiles = @"C:\Program Files";
        const string programFilesX86 = @"C:\Program Files (x86)";

        IEnumerable<string> roots = VisualStudioToolchainLocator.GetDefaultInstallationRoots(
            programFiles,
            programFilesX86,
            visualStudioInstall);

        Assert.DoesNotContain(visualStudioInstall, roots);
        Assert.Equal(Path.Combine(programFiles, "Microsoft Visual Studio", "18", "Community"), roots.First());
    }

    /// <summary>
    /// Selects the first of several complete installations discovered by vswhere, proving that when
    /// Visual Studio 2022 and Visual Studio 2026 are both installed, the newest-first vswhere
    /// ordering -- not installation order or path -- decides which one is used.
    /// </summary>
    [Fact]
    public void SelectsTheFirstCompleteInstallationWhenMultipleAreDiscovered()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        VisualStudioToolchain newer = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path, "VS2026");
        VisualStudioToolchain older = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path, "VS2022");

        VisualStudioToolchain actual = VisualStudioToolchainLocator.Find(
            [
                Path.Combine(temporaryDirectory.Path, "VS2026"),
                Path.Combine(temporaryDirectory.Path, "VS2022"),
            ]);

        Assert.Equal(newer, actual);
        Assert.NotEqual(older, actual);
    }

    /// <summary>
    /// Finds an installation whose root contains an ampersand, proving file-existence discovery does
    /// not depend on characters that are significant to a shell. A literal, non-default drive letter
    /// (for example <c>D:\...</c>) is covered separately by
    /// <see cref="ReturnsConfiguredAndStandardInstallationRootsInSearchOrder"/>, since the temporary
    /// directories this test suite creates are confined to the test runner's own drive.
    /// </summary>
    [Fact]
    public void FindsAnInstallationWithAnAmpersandInItsPath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string installationName = Path.Combine("Dev Tools", "Visual Studio & SDKs", "VS2026");
        VisualStudioToolchain expected = Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path, installationName);

        VisualStudioToolchain actual = VisualStudioToolchainLocator.Find(
            [Path.Combine(temporaryDirectory.Path, installationName)]);

        Assert.Equal(expected, actual);
    }

    /// <summary>Rejects installations missing required toolchain files.</summary>
    [Fact]
    public void RejectsInstallationsWithoutTheRequiredToolchainFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() =>
            VisualStudioToolchainLocator.Find([temporaryDirectory.Path]));
    }

    /// <summary>Reports a compiler-only installation even when its bundled CMake, Ninja, and vcpkg are all missing.</summary>
    [Fact]
    public void FindCompilerOnlyReportsAnInstallationMissingEveryBundledTool()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string root = Path.Combine(temporaryDirectory.Path, "Incomplete VS");
        string vcvarsallPath = Path.Combine(root, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        File.WriteAllText(vcvarsallPath, "@echo off");

        VisualStudioCompilerInstallation? actual = VisualStudioToolchainLocator.FindCompilerOnly([root]);

        Assert.NotNull(actual);
        Assert.Equal(root, actual.Root);
        Assert.Equal(vcvarsallPath, actual.VcvarsallPath);
    }

    /// <summary>Reports no compiler-only installation when no candidate root contains the environment script.</summary>
    [Fact]
    public void FindCompilerOnlyReturnsNullWhenNoRootContainsTheEnvironmentScript()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        VisualStudioCompilerInstallation? actual = VisualStudioToolchainLocator.FindCompilerOnly([temporaryDirectory.Path]);

        Assert.Null(actual);
    }

    /// <summary>Selects the first of several candidate roots that contains the environment script, skipping an earlier root without one.</summary>
    [Fact]
    public void FindCompilerOnlySelectsTheFirstRootContainingTheEnvironmentScript()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string emptyRoot = Path.Combine(temporaryDirectory.Path, "Empty");
        string secondRoot = Path.Combine(temporaryDirectory.Path, "Second");
        string secondVcvarsallPath = Path.Combine(secondRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        Directory.CreateDirectory(emptyRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(secondVcvarsallPath)!);
        File.WriteAllText(secondVcvarsallPath, "@echo off");

        VisualStudioCompilerInstallation? actual = VisualStudioToolchainLocator.FindCompilerOnly([emptyRoot, secondRoot]);

        Assert.NotNull(actual);
        Assert.Equal(secondRoot, actual.Root);
        Assert.Equal(secondVcvarsallPath, actual.VcvarsallPath);
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
