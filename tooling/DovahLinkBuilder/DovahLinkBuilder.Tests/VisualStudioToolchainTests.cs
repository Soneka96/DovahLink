using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies Visual Studio toolchain discovery.</summary>
public sealed class VisualStudioToolchainTests
{
    /// <summary>Returns the configured root first, then every supported standard edition path.</summary>
    [Fact]
    public void ReturnsConfiguredAndStandardInstallationRootsInSearchOrder()
    {
        const string configuredInstallation = @"D:\Custom Visual Studio";
        const string programFiles = @"C:\Program Files";
        const string programFilesX86 = @"C:\Program Files (x86)";

        IEnumerable<string> roots = VisualStudioToolchainLocator.GetDefaultInstallationRoots(
            programFiles,
            programFilesX86,
            configuredInstallation);

        string[] expectedRoots =
        [
            configuredInstallation,
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
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off");

        VisualStudioToolchain toolchain = VisualStudioToolchainLocator.Find([installationRoot]);

        Assert.Equal(vcvarsallPath, toolchain.VcvarsallPath);
        Assert.Equal(vcpkgRoot, toolchain.VcpkgRoot);
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

    /// <summary>Rejects installations missing required toolchain files.</summary>
    [Fact]
    public void RejectsInstallationsWithoutTheRequiredToolchainFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() =>
            VisualStudioToolchainLocator.Find([temporaryDirectory.Path]));
    }

    /// <summary>Rejects a direct toolchain value whose environment script is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingEnvironmentScript()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string vcpkgRoot = Path.Combine(temporaryDirectory.Path, "vcpkg");
        Directory.CreateDirectory(vcpkgRoot);

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(Path.Combine(temporaryDirectory.Path, "missing.bat"), vcpkgRoot)));
    }

    /// <summary>Rejects an existing batch file that is not the supported Visual Studio environment script.</summary>
    [Fact]
    public void ValidateRejectsAnUnexpectedEnvironmentScriptName()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string batchPath = Path.Combine(temporaryDirectory.Path, "other.bat");
        string vcpkgRoot = Path.Combine(temporaryDirectory.Path, "vcpkg");
        File.WriteAllText(batchPath, "@echo off");
        Directory.CreateDirectory(vcpkgRoot);

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(batchPath, vcpkgRoot)));
    }

    /// <summary>Rejects a direct toolchain value whose bundled vcpkg directory is missing.</summary>
    [Fact]
    public void ValidateRejectsAMissingVcpkgDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string vcvarsallPath = Path.Combine(temporaryDirectory.Path, "vcvarsall.bat");
        File.WriteAllText(vcvarsallPath, "@echo off");

        Assert.Throws<InvalidOperationException>(() => VisualStudioToolchainLocator.Validate(
            new VisualStudioToolchain(vcvarsallPath, Path.Combine(temporaryDirectory.Path, "missing-vcpkg"))));
    }

    /// <summary>Reports <see cref="ToolchainAvailability.Found"/> for an installation with the required files.</summary>
    [Fact]
    public void TryFindReturnsFoundForAnInstallationWithTheRequiredFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string installationRoot = Path.Combine(temporaryDirectory.Path, "Community");
        string vcvarsallPath = Path.Combine(installationRoot, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        string vcpkgRoot = Path.Combine(installationRoot, "VC", "vcpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(vcvarsallPath)!);
        Directory.CreateDirectory(vcpkgRoot);
        File.WriteAllText(vcvarsallPath, "@echo off");

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
