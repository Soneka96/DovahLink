using System.Text.Json.Nodes;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises ProtocolFixtures' loading of canonical protocol/fixtures/ files.</summary>
public class ProtocolFixturesTests
{
    /// <summary>Verifies that a known fixture's raw text can be read and parses as JSON.</summary>
    [Fact]
    public void ReadFixtureReturnsTheKnownFixtureFileContents()
    {
        string text = ProtocolFixtures.ReadFixture("connection/ping.json");

        Assert.Contains("\"messageType\": \"ping\"", text);
    }

    /// <summary>Verifies that ReadFixture throws FileNotFoundException for a missing fixture.</summary>
    [Fact]
    public void ReadFixtureThrowsFileNotFoundExceptionForAMissingFixture()
    {
        Assert.Throws<FileNotFoundException>(() => ProtocolFixtures.ReadFixture("connection/does-not-exist.json"));
    }

    /// <summary>Verifies that a known fixture decodes into its expected envelope fields.</summary>
    [Fact]
    public void DecodeFixtureEnvelopeDecodesTheKnownFixtureEnvelope()
    {
        Envelope envelope = ProtocolFixtures.DecodeFixtureEnvelope("connection/ping.json");

        Assert.Equal("ping", envelope.MessageType);
        Assert.Equal("message-ping-1", envelope.MessageId);
    }

    /// <summary>Verifies that ReadFixturePayload returns only the fixture's payload object.</summary>
    [Fact]
    public void ReadFixturePayloadReturnsOnlyThePayloadObject()
    {
        JsonObject payload = ProtocolFixtures.ReadFixturePayload("connection/ping.json");

        Assert.Empty(payload);
    }

    /// <summary>Verifies that ReadFixturePayload returns a non-empty payload's actual content.</summary>
    [Fact]
    public void ReadFixturePayloadReturnsNonEmptyPayloadContent()
    {
        JsonObject payload = ProtocolFixtures.ReadFixturePayload("connection/hello.json");

        Assert.Equal("client", payload["endpoint"]?.GetValue<string>());
    }
}
