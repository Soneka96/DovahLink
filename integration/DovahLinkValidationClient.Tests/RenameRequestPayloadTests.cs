using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises RenameRequestPayload's Encode behavior against the canonical fixture.</summary>
public class RenameRequestPayloadTests
{
    /// <summary>Verifies that Encode matches the canonical rename-request fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalFixture()
    {
        RenameRequestPayload payload = Fixtures.BuildRenameRequestPayload();

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("rename/rename-request.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }

    /// <summary>Verifies that Encode encodes an empty displayName, which clears the name.</summary>
    [Fact]
    public void EncodeEncodesAnEmptyDisplayName()
    {
        RenameRequestPayload payload = Fixtures.BuildRenameRequestPayload(displayName: "");

        JsonObject encoded = payload.Encode();
        Assert.Equal("", encoded["displayName"]?.GetValue<string>());
    }
}
