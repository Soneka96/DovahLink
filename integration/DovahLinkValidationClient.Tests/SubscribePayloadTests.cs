using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises SubscribePayload's Encode behavior against the canonical fixture.</summary>
public class SubscribePayloadTests
{
    /// <summary>Verifies that Encode matches the canonical subscribe fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalFixture()
    {
        SubscribePayload payload = Fixtures.BuildSubscribePayload();

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("subscriptions/subscribe.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }

    /// <summary>Verifies that Encode encodes an empty stateAreas list as an unsubscribe.</summary>
    [Fact]
    public void EncodeEncodesAnEmptyStateAreasListAsAnUnsubscribe()
    {
        SubscribePayload payload = Fixtures.BuildSubscribePayload([]);

        JsonObject encoded = payload.Encode();
        var array = Assert.IsType<JsonArray>(encoded["stateAreas"]);
        Assert.Empty(array);
    }

    /// <summary>Verifies that Encode preserves multiple requested state areas in order.</summary>
    [Fact]
    public void EncodePreservesMultipleStateAreasInOrder()
    {
        SubscribePayload payload = Fixtures.BuildSubscribePayload(["area_a", "area_b"]);

        JsonObject encoded = payload.Encode();
        var array = Assert.IsType<JsonArray>(encoded["stateAreas"]);
        Assert.Equal("area_a", array[0]?.GetValue<string>());
        Assert.Equal("area_b", array[1]?.GetValue<string>());
    }
}
