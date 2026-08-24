using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises PairingAckPayload's Encode behavior against the canonical fixture.</summary>
public class PairingAckPayloadTests
{
    /// <summary>Verifies that Encode matches the canonical pairing-ack fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalFixture()
    {
        PairingAckPayload payload = Fixtures.BuildPairingAckPayload();

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("pairing/pairing-ack.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }
}
