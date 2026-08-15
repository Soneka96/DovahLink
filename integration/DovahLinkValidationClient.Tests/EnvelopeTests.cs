using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises Envelope's unconditional encoding and decoding of the identity fields.</summary>
public class EnvelopeTests
{
    /// <summary>Builds a minimal envelope with the given identity field values.</summary>
    /// <param name="bridgeInstanceId">The bridge instance identity to encode.</param>
    /// <param name="playContextId">The play context identity to encode.</param>
    /// <param name="clientId">The client identity to encode.</param>
    /// <returns>The constructed envelope.</returns>
    private static Envelope MakeEnvelope(
        string? bridgeInstanceId = null, string? playContextId = null, string? clientId = null)
    {
        return new Envelope("ping", "message-1", "session-1", null, new JsonObject(),
            bridgeInstanceId, playContextId, clientId);
    }

    /// <summary>Verifies that empty identity fields are emitted as JSON null rather than omitted.</summary>
    [Fact]
    public void EncodeEmitsJsonNullForEmptyIdentityFields()
    {
        Envelope envelope = MakeEnvelope();

        JsonObject encoded = JsonNode.Parse(envelope.Encode())!.AsObject();

        Assert.True(encoded.ContainsKey("bridgeInstanceId"));
        Assert.Null(encoded["bridgeInstanceId"]);
        Assert.True(encoded.ContainsKey("playContextId"));
        Assert.Null(encoded["playContextId"]);
        Assert.True(encoded.ContainsKey("clientId"));
        Assert.Null(encoded["clientId"]);
    }

    /// <summary>Verifies that populated identity fields are emitted as their string values.</summary>
    [Fact]
    public void EncodeEmitsValuesForPopulatedIdentityFields()
    {
        Envelope envelope = MakeEnvelope("bridge-1", "context-1", "client-1");

        JsonObject encoded = JsonNode.Parse(envelope.Encode())!.AsObject();

        Assert.Equal("bridge-1", encoded["bridgeInstanceId"]!.GetValue<string>());
        Assert.Equal("context-1", encoded["playContextId"]!.GetValue<string>());
        Assert.Equal("client-1", encoded["clientId"]!.GetValue<string>());
    }

    /// <summary>Verifies that decoding a message with explicit null identity fields yields null fields.</summary>
    [Fact]
    public void DecodeTreatsExplicitNullIdentityKeysAsNull()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": null, "playContextId": null,
             "clientId": null}
            """;

        Envelope envelope = Envelope.Decode(json);

        Assert.Null(envelope.BridgeInstanceId);
        Assert.Null(envelope.PlayContextId);
        Assert.Null(envelope.ClientId);
    }

    /// <summary>Verifies that decoding a message with string identity fields yields their values.</summary>
    [Fact]
    public void DecodeReadsIdentityFieldValues()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": "bridge-1",
             "playContextId": "context-1", "clientId": "client-1"}
            """;

        Envelope envelope = Envelope.Decode(json);

        Assert.Equal("bridge-1", envelope.BridgeInstanceId);
        Assert.Equal("context-1", envelope.PlayContextId);
        Assert.Equal("client-1", envelope.ClientId);
    }

    /// <summary>Verifies that an envelope with mixed set and null identity fields round-trips exactly.</summary>
    [Fact]
    public void EncodeThenDecodeRoundTripsMixedIdentityFields()
    {
        Envelope original = MakeEnvelope("bridge-1", null, "client-1");

        Envelope roundTripped = Envelope.Decode(original.Encode());

        Assert.Equal(original.BridgeInstanceId, roundTripped.BridgeInstanceId);
        Assert.Equal(original.PlayContextId, roundTripped.PlayContextId);
        Assert.Equal(original.ClientId, roundTripped.ClientId);
    }

    /// <summary>Verifies that decoding rejects a non-string, non-null identity field as a malformed envelope.</summary>
    [Fact]
    public void DecodeRejectsANonStringNonNullIdentityField()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": 5, "playContextId": null,
             "clientId": null}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that decoding rejects a non-string, non-null playContextId as a malformed envelope.</summary>
    [Fact]
    public void DecodeRejectsANonStringNonNullPlayContextId()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": null, "playContextId": 5,
             "clientId": null}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that decoding rejects a non-string, non-null clientId as a malformed envelope.</summary>
    [Fact]
    public void DecodeRejectsANonStringNonNullClientId()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": null, "playContextId": null,
             "clientId": 5}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that decoding rejects an empty-string bridgeInstanceId as a malformed envelope.</summary>
    [Fact]
    public void DecodeRejectsAnEmptyStringBridgeInstanceId()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": "", "playContextId": null,
             "clientId": null}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that decoding rejects an empty-string playContextId as a malformed envelope.</summary>
    [Fact]
    public void DecodeRejectsAnEmptyStringPlayContextId()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": null, "playContextId": "",
             "clientId": null}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that decoding rejects an empty-string clientId as a malformed envelope.</summary>
    [Fact]
    public void DecodeRejectsAnEmptyStringClientId()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": null, "playContextId": null,
             "clientId": ""}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that an envelope constructed with an empty-string identity field encodes it
    /// faithfully but then fails to decode -- Encode has no reason to validate a value this client
    /// itself constructed, but the wire contract still rejects it on the way back in.</summary>
    [Fact]
    public void EncodeThenDecodeRejectsAnEnvelopeConstructedWithAnEmptyStringIdentityField()
    {
        Envelope original = MakeEnvelope(bridgeInstanceId: "");

        Assert.Throws<FormatException>(() => Envelope.Decode(original.Encode()));
    }

    /// <summary>Verifies that decoding rejects a message missing the bridgeInstanceId key entirely.</summary>
    [Fact]
    public void DecodeRejectsMissingBridgeInstanceId()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "playContextId": null, "clientId": null}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that decoding rejects a message missing the playContextId key entirely.</summary>
    [Fact]
    public void DecodeRejectsMissingPlayContextId()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": null, "clientId": null}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that decoding rejects a message missing the clientId key entirely.</summary>
    [Fact]
    public void DecodeRejectsMissingClientId()
    {
        const string json = """
            {"messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": null, "playContextId": null}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }
}
