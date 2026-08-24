using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises CapabilitiesPayload's Encode and Decode behavior against canonical fixtures.</summary>
public class CapabilitiesPayloadTests
{
    /// <summary>Verifies that Encode matches the canonical capabilities-bridge fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalBridgeFixture()
    {
        CapabilitiesPayload payload = Fixtures.BuildCapabilitiesPayload();

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("capabilities/capabilities-bridge.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }

    /// <summary>Verifies that Encode matches the canonical capabilities-client fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalClientFixture()
    {
        CapabilitiesPayload payload = Fixtures.BuildCapabilitiesPayload();

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("capabilities/capabilities-client.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }

    /// <summary>Verifies that Encode encodes a non-empty capabilities list.</summary>
    [Fact]
    public void EncodeEncodesANonEmptyCapabilitiesList()
    {
        CapabilitiesPayload payload = Fixtures.BuildCapabilitiesPayload([Fixtures.BuildCapability()]);

        JsonObject encoded = payload.Encode();
        var array = Assert.IsType<JsonArray>(encoded["capabilities"]);
        Assert.Single(array);
    }

    /// <summary>Verifies that Decode round-trips a non-empty, multi-entry list through Encode,
    /// preserving order.</summary>
    [Fact]
    public void DecodeRoundTripsAMultiEntryListThroughEncodeInOrder()
    {
        CapabilitiesPayload original = Fixtures.BuildCapabilitiesPayload([
            Fixtures.BuildCapability(id: "state.inventory"),
            Fixtures.BuildCapability(id: "state.quests"),
        ]);

        CapabilitiesPayload decoded = CapabilitiesPayload.Decode(original.Encode());

        Assert.Equal(2, decoded.Capabilities.Count);
        Assert.Equal("state.inventory", decoded.Capabilities[0].Id);
        Assert.Equal("state.quests", decoded.Capabilities[1].Id);
    }

    /// <summary>Verifies that Decode matches the canonical capabilities-bridge fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalBridgeFixture()
    {
        CapabilitiesPayload payload =
            CapabilitiesPayload.Decode(ProtocolFixtures.ReadFixturePayload("capabilities/capabilities-bridge.json"));

        Assert.Empty(payload.Capabilities);
    }

    /// <summary>Verifies that Decode matches the canonical capabilities-client fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalClientFixture()
    {
        CapabilitiesPayload payload =
            CapabilitiesPayload.Decode(ProtocolFixtures.ReadFixturePayload("capabilities/capabilities-client.json"));

        Assert.Empty(payload.Capabilities);
    }

    /// <summary>Verifies that Decode decodes a non-empty capabilities list.</summary>
    [Fact]
    public void DecodeDecodesANonEmptyCapabilitiesList()
    {
        var payload = new JsonObject
        {
            ["capabilities"] = new JsonArray(new JsonObject { ["id"] = "state.inventory", ["version"] = 1 }),
        };

        CapabilitiesPayload decoded = CapabilitiesPayload.Decode(payload);

        Assert.Single(decoded.Capabilities);
        Assert.Equal("state.inventory", decoded.Capabilities[0].Id);
        Assert.Equal(1, decoded.Capabilities[0].Version);
    }

    /// <summary>Verifies that Decode throws FormatException when capabilities is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCapabilitiesIsMissing()
    {
        var payload = new JsonObject();

        Assert.Throws<FormatException>(() => CapabilitiesPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when capabilities is not an array.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenCapabilitiesIsNotAnArray()
    {
        var payload = new JsonObject { ["capabilities"] = "not an array" };

        Assert.Throws<FormatException>(() => CapabilitiesPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when an entry is not an object.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenAnEntryIsNotAnObject()
    {
        var payload = new JsonObject { ["capabilities"] = new JsonArray("state.inventory") };

        Assert.Throws<FormatException>(() => CapabilitiesPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when an entry is malformed.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenAnEntryIsMalformed()
    {
        var payload = new JsonObject
        {
            ["capabilities"] = new JsonArray(new JsonObject { ["id"] = "state.inventory" }),
        };

        Assert.Throws<FormatException>(() => CapabilitiesPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode rejects the whole list when a later entry is malformed, even
    /// though an earlier entry was valid.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenAValidEntryIsFollowedByAMalformedOne()
    {
        var payload = new JsonObject
        {
            ["capabilities"] = new JsonArray(
                new JsonObject { ["id"] = "state.inventory", ["version"] = 1 },
                new JsonObject { ["id"] = "state.quests" }),
        };

        Assert.Throws<FormatException>(() => CapabilitiesPayload.Decode(payload));
    }
}
