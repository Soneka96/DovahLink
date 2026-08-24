using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises Capability's Encode and Decode behavior.</summary>
public class CapabilityTests
{
    /// <summary>Verifies that Encode produces id and version.</summary>
    [Fact]
    public void EncodeProducesIdAndVersion()
    {
        Capability capability = Fixtures.BuildCapability();

        JsonObject encoded = capability.Encode();
        Assert.Equal("state.inventory", encoded["id"]?.GetValue<string>());
        Assert.Equal(1, encoded["version"]?.GetValue<int>());
    }

    /// <summary>Verifies that Decode round-trips through Encode.</summary>
    [Fact]
    public void DecodeRoundTripsThroughEncode()
    {
        Capability original = Fixtures.BuildCapability();

        Capability decoded = Capability.Decode(original.Encode());

        Assert.Equal(original, decoded);
    }

    /// <summary>Verifies that Decode throws FormatException when id is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenIdIsMissing()
    {
        var entry = new JsonObject { ["version"] = 1 };

        Assert.Throws<FormatException>(() => Capability.Decode(entry));
    }

    /// <summary>Verifies that Decode throws FormatException when version is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenVersionIsMissing()
    {
        var entry = new JsonObject { ["id"] = "state.inventory" };

        Assert.Throws<FormatException>(() => Capability.Decode(entry));
    }

    /// <summary>Verifies that Decode throws FormatException when id is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenIdIsTheWrongType()
    {
        var entry = new JsonObject { ["id"] = 1, ["version"] = 1 };

        Assert.Throws<FormatException>(() => Capability.Decode(entry));
    }

    /// <summary>Verifies that Decode throws FormatException when version is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenVersionIsTheWrongType()
    {
        var entry = new JsonObject { ["id"] = "state.inventory", ["version"] = "one" };

        Assert.Throws<FormatException>(() => Capability.Decode(entry));
    }

    /// <summary>Verifies that Decode throws FormatException when version is a non-integer number.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenVersionIsFractional()
    {
        var entry = new JsonObject { ["id"] = "state.inventory", ["version"] = 1.5 };

        Assert.Throws<FormatException>(() => Capability.Decode(entry));
    }
}
