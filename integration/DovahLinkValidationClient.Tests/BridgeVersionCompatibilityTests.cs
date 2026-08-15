using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises BridgeVersionCompatibility's evaluation of a Bridge-reported version string.</summary>
public class BridgeVersionCompatibilityTests
{
    /// <summary>Verifies that a version equal to the supported range is accepted.</summary>
    [Fact]
    public void EvaluateReturnsSupportedForTheDeclaredSupportedVersion()
    {
        BridgeVersionCompatibilityResult result =
            BridgeVersionCompatibility.Evaluate(BridgeVersionCompatibility.MinimumSupportedVersion.ToString());

        Assert.Equal(BridgeVersionCompatibilityResult.Supported, result);
    }

    /// <summary>Verifies that a version equal to the maximum supported version is accepted. Distinct
    /// from the minimum-boundary test above so a future widened range still exercises both ends of
    /// the comparison, even though both bounds currently equal the same version.</summary>
    [Fact]
    public void EvaluateReturnsSupportedForTheMaximumSupportedVersion()
    {
        BridgeVersionCompatibilityResult result =
            BridgeVersionCompatibility.Evaluate(BridgeVersionCompatibility.MaximumSupportedVersion.ToString());

        Assert.Equal(BridgeVersionCompatibilityResult.Supported, result);
    }

    /// <summary>Verifies that a version older than the minimum is rejected as too old.</summary>
    [Fact]
    public void EvaluateReturnsBridgeOlderThanSupportedForAnOlderVersion()
    {
        BridgeVersionCompatibilityResult result = BridgeVersionCompatibility.Evaluate("0.1.0");

        Assert.Equal(BridgeVersionCompatibilityResult.BridgeOlderThanSupported, result);
    }

    /// <summary>Verifies that a version newer than the maximum is rejected as too new.</summary>
    [Fact]
    public void EvaluateReturnsBridgeNewerThanSupportedForANewerVersion()
    {
        BridgeVersionCompatibilityResult result = BridgeVersionCompatibility.Evaluate("99.0.0");

        Assert.Equal(BridgeVersionCompatibilityResult.BridgeNewerThanSupported, result);
    }

    /// <summary>Verifies that a null version is treated as unparseable rather than throwing.</summary>
    [Fact]
    public void EvaluateReturnsUnparseableForANullVersion()
    {
        BridgeVersionCompatibilityResult result = BridgeVersionCompatibility.Evaluate(null);

        Assert.Equal(BridgeVersionCompatibilityResult.Unparseable, result);
    }

    /// <summary>Verifies that an empty version string is treated as unparseable.</summary>
    [Fact]
    public void EvaluateReturnsUnparseableForAnEmptyVersion()
    {
        BridgeVersionCompatibilityResult result = BridgeVersionCompatibility.Evaluate("");

        Assert.Equal(BridgeVersionCompatibilityResult.Unparseable, result);
    }

    /// <summary>Verifies that a non-numeric version string is treated as unparseable.</summary>
    [Fact]
    public void EvaluateReturnsUnparseableForANonNumericVersion()
    {
        BridgeVersionCompatibilityResult result = BridgeVersionCompatibility.Evaluate("not-a-version");

        Assert.Equal(BridgeVersionCompatibilityResult.Unparseable, result);
    }
}
