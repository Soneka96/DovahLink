using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies parsing of `##stage &lt;name&gt; &lt;status&gt;` packaging progress markers.</summary>
public sealed class BuildStageProgressParserTests
{
    /// <summary>Parses every known stage name and `start` into its <see cref="BuildStage"/> as <see cref="BuildStageStatus.Running"/>.</summary>
    /// <param name="markerName">The marker's stage name, as printed by `tooling/package_adapter_host.py`.</param>
    /// <param name="expectedStage">The stage the marker name is expected to map to.</param>
    [Theory]
    [InlineData("host_publish", BuildStage.PublishHost)]
    [InlineData("package_assembly", BuildStage.AssemblePackage)]
    [InlineData("package_validation", BuildStage.ValidatePackage)]
    [InlineData("archive", BuildStage.CreateZip)]
    public void ParsesAStartMarkerAsRunning(string markerName, BuildStage expectedStage)
    {
        (BuildStage Stage, BuildStageStatus Status)? result = BuildStageProgressParser.TryParse($"##stage {markerName} start");

        Assert.Equal((expectedStage, BuildStageStatus.Running), result);
    }

    /// <summary>Parses every known stage name and `done` into its <see cref="BuildStage"/> as <see cref="BuildStageStatus.Succeeded"/>.</summary>
    /// <param name="markerName">The marker's stage name, as printed by `tooling/package_adapter_host.py`.</param>
    /// <param name="expectedStage">The stage the marker name is expected to map to.</param>
    [Theory]
    [InlineData("host_publish", BuildStage.PublishHost)]
    [InlineData("package_assembly", BuildStage.AssemblePackage)]
    [InlineData("package_validation", BuildStage.ValidatePackage)]
    [InlineData("archive", BuildStage.CreateZip)]
    public void ParsesADoneMarkerAsSucceeded(string markerName, BuildStage expectedStage)
    {
        (BuildStage Stage, BuildStageStatus Status)? result = BuildStageProgressParser.TryParse($"##stage {markerName} done");

        Assert.Equal((expectedStage, BuildStageStatus.Succeeded), result);
    }

    /// <summary>Returns <see langword="null"/> for a line that is not a stage marker.</summary>
    [Fact]
    public void ReturnsNullForALineWithoutTheMarkerPrefix()
    {
        Assert.Null(BuildStageProgressParser.TryParse("Publishing self-contained win-x64..."));
    }

    /// <summary>Returns <see langword="null"/> for a marker whose stage name is not recognized.</summary>
    [Fact]
    public void ReturnsNullForAnUnknownStageName()
    {
        Assert.Null(BuildStageProgressParser.TryParse("##stage unknown_stage start"));
    }

    /// <summary>Returns <see langword="null"/> for a marker whose status is neither `start` nor `done`.</summary>
    [Fact]
    public void ReturnsNullForAnUnknownStatusWord()
    {
        Assert.Null(BuildStageProgressParser.TryParse("##stage host_publish paused"));
    }

    /// <summary>Returns <see langword="null"/> for a truncated marker with no content after the prefix.</summary>
    [Fact]
    public void ReturnsNullForAMarkerPrefixWithNoContent()
    {
        Assert.Null(BuildStageProgressParser.TryParse("##stage "));
    }
}
