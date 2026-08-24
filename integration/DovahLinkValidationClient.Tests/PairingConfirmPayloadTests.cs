using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises PairingConfirmPayload's Encode behavior against the canonical fixture.</summary>
public class PairingConfirmPayloadTests
{
    /// <summary>Verifies that Encode matches the canonical pairing-confirm fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalFixture()
    {
        PairingConfirmPayload payload = Fixtures.BuildPairingConfirmPayload();

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("pairing/pairing-confirm.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }

    /// <summary>Verifies that Encode always includes displayName as a key, even when null.</summary>
    [Fact]
    public void EncodeAlwaysIncludesDisplayNameAsAKeyEvenWhenNull()
    {
        PairingConfirmPayload payload = Fixtures.BuildPairingConfirmPayload(displayName: null);

        JsonObject encoded = payload.Encode();
        Assert.True(encoded.ContainsKey("displayName"));
        Assert.Null(encoded["displayName"]);
    }

    /// <summary>Verifies that Encode preserves an empty displayName, which clears the name.</summary>
    [Fact]
    public void EncodePreservesAnEmptyDisplayName()
    {
        PairingConfirmPayload payload = Fixtures.BuildPairingConfirmPayload(displayName: "");

        JsonObject encoded = payload.Encode();
        Assert.Equal("", encoded["displayName"]?.GetValue<string>());
    }
}
