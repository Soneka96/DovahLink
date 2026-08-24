using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises SnapshotRequestPayload's Encode behavior against the canonical fixture.</summary>
public class SnapshotRequestPayloadTests
{
    /// <summary>Verifies that Encode matches the canonical snapshot-request fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalFixture()
    {
        SnapshotRequestPayload payload = Fixtures.BuildSnapshotRequestPayload();

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("subscriptions/snapshot-request.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }

    /// <summary>Verifies that Encode omits knownRevision entirely when absent.</summary>
    [Fact]
    public void EncodeOmitsKnownRevisionEntirelyWhenAbsent()
    {
        SnapshotRequestPayload payload = Fixtures.BuildSnapshotRequestPayload(knownRevision: null);

        JsonObject encoded = payload.Encode();
        Assert.False(encoded.ContainsKey("knownRevision"));
    }

    /// <summary>Verifies that Encode includes knownRevision when present.</summary>
    [Fact]
    public void EncodeIncludesKnownRevisionWhenPresent()
    {
        SnapshotRequestPayload payload = Fixtures.BuildSnapshotRequestPayload(knownRevision: 5);

        JsonObject encoded = payload.Encode();
        Assert.Equal(5, encoded["knownRevision"]?.GetValue<int>());
    }
}
