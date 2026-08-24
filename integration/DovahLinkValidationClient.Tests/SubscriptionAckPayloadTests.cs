using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises SubscriptionAckPayload's Decode behavior against the canonical fixture.</summary>
public class SubscriptionAckPayloadTests
{
    /// <summary>Verifies that Decode matches the canonical subscription-ack fixture.</summary>
    [Fact]
    public void DecodeMatchesTheCanonicalFixture()
    {
        SubscriptionAckPayload payload = SubscriptionAckPayload.Decode(
            ProtocolFixtures.ReadFixturePayload("subscriptions/subscription-ack.json"));

        Assert.Empty(payload.AcceptedStateAreas);
        Assert.Equal(["example_area"], payload.RejectedStateAreas);
    }

    /// <summary>Verifies that Decode decodes a non-empty acceptedStateAreas list.</summary>
    [Fact]
    public void DecodeDecodesANonEmptyAcceptedStateAreasList()
    {
        var payload = new JsonObject
        {
            ["acceptedStateAreas"] = new JsonArray("area_a"),
            ["rejectedStateAreas"] = new JsonArray(),
        };

        SubscriptionAckPayload decoded = SubscriptionAckPayload.Decode(payload);

        Assert.Equal(["area_a"], decoded.AcceptedStateAreas);
        Assert.Empty(decoded.RejectedStateAreas);
    }

    /// <summary>Verifies that Decode throws FormatException when acceptedStateAreas is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenAcceptedStateAreasIsMissing()
    {
        var payload = new JsonObject { ["rejectedStateAreas"] = new JsonArray() };

        Assert.Throws<FormatException>(() => SubscriptionAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when rejectedStateAreas is missing.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRejectedStateAreasIsMissing()
    {
        var payload = new JsonObject { ["acceptedStateAreas"] = new JsonArray() };

        Assert.Throws<FormatException>(() => SubscriptionAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when acceptedStateAreas is not an array.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenAcceptedStateAreasIsNotAnArray()
    {
        var payload = new JsonObject
        {
            ["acceptedStateAreas"] = "not an array",
            ["rejectedStateAreas"] = new JsonArray(),
        };

        Assert.Throws<FormatException>(() => SubscriptionAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when an acceptedStateAreas entry is
    /// not a string.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenAnAcceptedEntryIsNotAString()
    {
        var payload = new JsonObject
        {
            ["acceptedStateAreas"] = new JsonArray(1),
            ["rejectedStateAreas"] = new JsonArray(),
        };

        Assert.Throws<FormatException>(() => SubscriptionAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when rejectedStateAreas is not an array.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenRejectedStateAreasIsNotAnArray()
    {
        var payload = new JsonObject
        {
            ["acceptedStateAreas"] = new JsonArray(),
            ["rejectedStateAreas"] = "not an array",
        };

        Assert.Throws<FormatException>(() => SubscriptionAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode throws FormatException when a rejectedStateAreas entry is
    /// not a string.</summary>
    [Fact]
    public void DecodeThrowsFormatExceptionWhenARejectedEntryIsNotAString()
    {
        var payload = new JsonObject
        {
            ["acceptedStateAreas"] = new JsonArray(),
            ["rejectedStateAreas"] = new JsonArray(1),
        };

        Assert.Throws<FormatException>(() => SubscriptionAckPayload.Decode(payload));
    }

    /// <summary>Verifies that Decode preserves the order of multiple accepted and rejected entries.</summary>
    [Fact]
    public void DecodePreservesOrderOfMultipleAcceptedAndRejectedEntries()
    {
        var payload = new JsonObject
        {
            ["acceptedStateAreas"] = new JsonArray("area_a", "area_b"),
            ["rejectedStateAreas"] = new JsonArray("area_c", "area_d"),
        };

        SubscriptionAckPayload decoded = SubscriptionAckPayload.Decode(payload);

        Assert.Equal(["area_a", "area_b"], decoded.AcceptedStateAreas);
        Assert.Equal(["area_c", "area_d"], decoded.RejectedStateAreas);
    }
}
