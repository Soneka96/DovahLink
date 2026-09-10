using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies Visual Studio toolchain discovery.</summary>
public sealed class VisualStudioToolchainTests
{
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
