using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises HelloPayload and HelloAuthPayload's Encode behavior against canonical fixtures.</summary>
public class HelloPayloadTests
{
    /// <summary>Verifies that Encode matches the canonical one_time_local_token fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalOneTimeLocalTokenFixture()
    {
        HelloPayload payload = Fixtures.BuildHelloPayload();

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("connection/hello.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }

    /// <summary>Verifies that Encode matches the canonical trusted_device_credential fixture.</summary>
    [Fact]
    public void EncodeMatchesTheCanonicalTrustedDeviceCredentialFixture()
    {
        HelloPayload payload = Fixtures.BuildHelloPayload(
            auth: Fixtures.BuildHelloAuthPayload(method: "trusted_device_credential"));

        JsonObject expected = ProtocolFixtures.ReadFixturePayload("connection/hello-trusted-device-credential.json");
        Assert.True(JsonNode.DeepEquals(payload.Encode(), expected));
    }

    /// <summary>Verifies that Encode omits auth.token entirely for unpaired, matching the canonical
    /// fixture.</summary>
    [Fact]
    public void EncodeOmitsAuthTokenEntirelyForUnpaired()
    {
        HelloPayload payload = Fixtures.BuildHelloPayload(
            auth: Fixtures.BuildHelloAuthPayload(method: "unpaired", token: null));

        JsonObject encoded = payload.Encode();
        JsonObject expected = ProtocolFixtures.ReadFixturePayload("connection/hello-unpaired.json");
        Assert.True(JsonNode.DeepEquals(encoded, expected));
        Assert.False(((JsonObject)encoded["auth"]!).ContainsKey("token"));
    }

    /// <summary>Verifies that Endpoint is always "client".</summary>
    [Fact]
    public void EndpointIsAlwaysClient()
    {
        HelloPayload payload = Fixtures.BuildHelloPayload();

        Assert.Equal("client", payload.Endpoint);
    }
}
