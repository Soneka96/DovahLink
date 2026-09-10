using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies one Environment page check row's availability and curated remediation action label.</summary>
public sealed class EnvironmentCheckViewModelTests
{
    /// <summary>Reports Found as available, with no remediation action label.</summary>
    [Fact]
    public void ReportsFoundAsAvailableWithNoRemediationActionLabel()
    {
        var check = new EnvironmentCheckViewModel(new ToolchainCheckResult("CMake", ToolchainAvailability.Found, "3.30.0", null));

        Assert.True(check.IsAvailable);
        Assert.Null(check.RemediationActionLabel);
    }

    /// <summary>Reports a curated "Find manually" action for a missing Papyrus Compiler.</summary>
    [Fact]
    public void ReportsFindManuallyForAMissingPapyrusCompiler()
    {
        var check = new EnvironmentCheckViewModel(
            new ToolchainCheckResult("Papyrus Compiler", ToolchainAvailability.Missing, null, "not found"));

        Assert.False(check.IsAvailable);
        Assert.Equal("Find manually", check.RemediationActionLabel);
    }

    /// <summary>
    /// Reports a curated "Open Visual Studio Installer" action for a missing CMake, never implying a
    /// manual path override the Settings page does not expose.
    /// </summary>
    [Fact]
    public void ReportsOpenVisualStudioInstallerForAMissingCMake()
    {
        var check = new EnvironmentCheckViewModel(new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not found"));

        Assert.False(check.IsAvailable);
        Assert.Equal("Open Visual Studio Installer", check.RemediationActionLabel);
    }

    /// <summary>Reports no curated remediation action label for a tool without one, even when missing.</summary>
    [Fact]
    public void ReportsNoRemediationActionLabelForAToolWithoutOne()
    {
        var check = new EnvironmentCheckViewModel(new ToolchainCheckResult(".NET SDK", ToolchainAvailability.Missing, null, "not found"));

        Assert.Null(check.RemediationActionLabel);
    }

    /// <summary>Reports Invalid and CouldNotCheck as unavailable, same as Missing.</summary>
    /// <param name="availability">The non-Found availability to check.</param>
    [Theory]
    [InlineData(ToolchainAvailability.Missing)]
    [InlineData(ToolchainAvailability.Invalid)]
    [InlineData(ToolchainAvailability.CouldNotCheck)]
    public void ReportsEveryNonFoundAvailabilityAsUnavailable(ToolchainAvailability availability)
    {
        var check = new EnvironmentCheckViewModel(new ToolchainCheckResult("CMake", availability, null, "detail"));

        Assert.False(check.IsAvailable);
    }

    /// <summary>Exposes the underlying result's tool name, detail, and remediation hint unchanged.</summary>
    [Fact]
    public void ExposesTheUnderlyingResultsFieldsUnchanged()
    {
        var result = new ToolchainCheckResult("Python", ToolchainAvailability.Found, "3.12.0", null);

        var check = new EnvironmentCheckViewModel(result);

        Assert.Equal("Python", check.ToolName);
        Assert.Equal(ToolchainAvailability.Found, check.Availability);
        Assert.Equal("3.12.0", check.Detail);
        Assert.Null(check.RemediationHint);
        Assert.Same(result, check.Result);
    }
}
