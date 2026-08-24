using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises HelloAckPayload's Decode behavior against canonical fixtures.</summary>
public class HelloAckPayloadTests
{
    /// <summary>Verifies that Decode matches the canonical unpaired hello-ack fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalUnpairedFixture()
    {
        HelloAckPayload payload = HelloAckPayload.Decode(ProtocolFixtures.ReadFixturePayload("connection/hello-ack.json"));

        Assert.Equal("0.3.2", payload.BridgeVersion);
        Assert.Equal("unpaired", payload.ClientIdentityKind);
    }

    /// <summary>Verifies that Decode matches the canonical paired hello-ack fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalPairedFixture()
    {
        HelloAckPayload payload =
            HelloAckPayload.Decode(ProtocolFixtures.ReadFixturePayload("connection/hello-ack-paired.json"));

        Assert.Equal("0.3.2", payload.BridgeVersion);
        Assert.Equal("paired", payload.ClientIdentityKind);
    }

    /// <summary>Verifies that Decode throws FormatException when bridgeVersion is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenBridgeVersionIsMissing()
    {
        var payload = new JsonObject { ["clientIdentityKind"] = "paired" };

        Assert.Throws<FormatException>(() => HelloAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when clientIdentityKind is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenClientIdentityKindIsMissing()
    {
        var payload = new JsonObject { ["bridgeVersion"] = "0.3.2" };

        Assert.Throws<FormatException>(() => HelloAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when bridgeVersion is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenBridgeVersionIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["bridgeVersion"] = 1,
            ["clientIdentityKind"] = "paired",
        };

        Assert.Throws<FormatException>(() => HelloAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when clientIdentityKind is the wrong JSON type.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenClientIdentityKindIsTheWrongType()
    {
        var payload = new JsonObject
        {
            ["bridgeVersion"] = "0.3.2",
            ["clientIdentityKind"] = 1,
        };

        Assert.Throws<FormatException>(() => HelloAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an empty bridge version.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenBridgeVersionIsEmpty()
    {
        var payload = new JsonObject
        {
            ["bridgeVersion"] = "",
            ["clientIdentityKind"] = "paired",
        };

        Assert.Throws<FormatException>(() => HelloAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects an unknown client identity kind.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenClientIdentityKindIsUnknown()
    {
        var payload = new JsonObject
        {
            ["bridgeVersion"] = "0.3.2",
            ["clientIdentityKind"] = "unknown",
        };

        Assert.Throws<FormatException>(() => HelloAckPayload.Decode(payload));
    }
}
