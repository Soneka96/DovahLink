using System.Text.Json.Nodes;
using DovahLinkValidationClient;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises Envelope's version-gated encoding and tolerant decoding of the v2 identity fields.</summary>
public class EnvelopeTests
{
    /// <summary>Builds a minimal envelope at the given protocol version and identity field values.</summary>
    /// <param name="protocolVersion">The envelope's protocol version.</param>
    /// <param name="bridgeInstanceId">The bridge instance identity to encode.</param>
    /// <param name="playContextId">The play context identity to encode.</param>
    /// <param name="clientId">The client identity to encode.</param>
    /// <returns>The constructed envelope.</returns>
    private static Envelope MakeEnvelope(
        int protocolVersion, string? bridgeInstanceId = null, string? playContextId = null, string? clientId = null)
    {
        return new Envelope(protocolVersion, "ping", "message-1", "session-1", null, new JsonObject(),
            bridgeInstanceId, playContextId, clientId);
    }

    /// <summary>Verifies that v1 omits the v2 identity keys entirely, even when the record's fields are set.</summary>
    [Fact]
    public void EncodeOmitsTheV2IdentityFieldsForProtocolVersion1EvenWhenSet()
    {
        // Proves the gate is the version, not whether the fields happen to
        // be populated (protocol/schema/README.md's v2 section: "explicitly
        // version-gated, not inferred from whether the optional is empty").
        Envelope envelope = MakeEnvelope(1, "bridge-1", "context-1", "client-1");

        JsonObject encoded = JsonNode.Parse(envelope.Encode())!.AsObject();

        Assert.False(encoded.ContainsKey("bridgeInstanceId"));
        Assert.False(encoded.ContainsKey("playContextId"));
        Assert.False(encoded.ContainsKey("clientId"));
    }

    /// <summary>Verifies that v2 emits JSON null for empty identity fields rather than omitting them.</summary>
    [Fact]
    public void EncodeEmitsJsonNullForEmptyV2IdentityFields()
    {
        Envelope envelope = MakeEnvelope(2);

        JsonObject encoded = JsonNode.Parse(envelope.Encode())!.AsObject();

        Assert.True(encoded.ContainsKey("bridgeInstanceId"));
        Assert.Null(encoded["bridgeInstanceId"]);
        Assert.True(encoded.ContainsKey("playContextId"));
        Assert.Null(encoded["playContextId"]);
        Assert.True(encoded.ContainsKey("clientId"));
        Assert.Null(encoded["clientId"]);
    }

    /// <summary>Verifies that v2 emits string values for populated identity fields.</summary>
    [Fact]
    public void EncodeEmitsValuesForPopulatedV2IdentityFields()
    {
        Envelope envelope = MakeEnvelope(2, "bridge-1", "context-1", "client-1");

        JsonObject encoded = JsonNode.Parse(envelope.Encode())!.AsObject();

        Assert.Equal("bridge-1", encoded["bridgeInstanceId"]!.GetValue<string>());
        Assert.Equal("context-1", encoded["playContextId"]!.GetValue<string>());
        Assert.Equal("client-1", encoded["clientId"]!.GetValue<string>());
    }

    /// <summary>Verifies that decoding a v1 message with the identity keys omitted yields null fields.</summary>
    [Fact]
    public void DecodeTreatsOmittedV2IdentityKeysAsNull()
    {
        const string json = """
            {"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}}
            """;

        Envelope envelope = Envelope.Decode(json);

        Assert.Null(envelope.BridgeInstanceId);
        Assert.Null(envelope.PlayContextId);
        Assert.Null(envelope.ClientId);
    }

    /// <summary>Verifies that decoding a v2 message with explicit null identity fields yields null fields.</summary>
    [Fact]
    public void DecodeTreatsExplicitNullV2IdentityKeysAsNull()
    {
        const string json = """
            {"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": null, "playContextId": null,
             "clientId": null}
            """;

        Envelope envelope = Envelope.Decode(json);

        Assert.Null(envelope.BridgeInstanceId);
        Assert.Null(envelope.PlayContextId);
        Assert.Null(envelope.ClientId);
    }

    /// <summary>Verifies that decoding a v2 message with string identity fields yields their values.</summary>
    [Fact]
    public void DecodeReadsV2IdentityFieldValues()
    {
        const string json = """
            {"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": "bridge-1",
             "playContextId": "context-1", "clientId": "client-1"}
            """;

        Envelope envelope = Envelope.Decode(json);

        Assert.Equal("bridge-1", envelope.BridgeInstanceId);
        Assert.Equal("context-1", envelope.PlayContextId);
        Assert.Equal("client-1", envelope.ClientId);
    }

    /// <summary>Verifies that a v2 envelope with mixed set and null identity fields round-trips exactly.</summary>
    [Fact]
    public void EncodeThenDecodeRoundTripsMixedV2IdentityFields()
    {
        Envelope original = MakeEnvelope(2, "bridge-1", null, "client-1");

        Envelope roundTripped = Envelope.Decode(original.Encode());

        Assert.Equal(original.BridgeInstanceId, roundTripped.BridgeInstanceId);
        Assert.Equal(original.PlayContextId, roundTripped.PlayContextId);
        Assert.Equal(original.ClientId, roundTripped.ClientId);
    }

    /// <summary>Verifies that the v2 identity fields are still emitted above protocol version 2, guarding
    /// against the gate narrowing from >= 2 to == 2.</summary>
    [Fact]
    public void EncodeAlsoEmitsTheV2IdentityFieldsAtProtocolVersion3()
    {
        Envelope envelope = MakeEnvelope(3, "bridge-1", "context-1", "client-1");

        JsonObject encoded = JsonNode.Parse(envelope.Encode())!.AsObject();

        Assert.Equal("bridge-1", encoded["bridgeInstanceId"]!.GetValue<string>());
    }

    /// <summary>Verifies that decoding tolerates a v1 message that carries a v2 identity field anyway.</summary>
    [Fact]
    public void DecodeTreatsAV1MessageCarryingAV2IdentityFieldAnywayTolerantly()
    {
        // Decode never rejects on protocolVersion/identity-field mismatch:
        // only Encode enforces which version may write these fields.
        const string json = """
            {"protocolVersion": 1, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": "bridge-1"}
            """;

        Envelope envelope = Envelope.Decode(json);

        Assert.Equal("bridge-1", envelope.BridgeInstanceId);
    }

    /// <summary>Verifies that decoding rejects a non-string, non-null identity field as a malformed envelope.</summary>
    [Fact]
    public void DecodeRejectsANonStringNonNullIdentityField()
    {
        const string json = """
            {"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "bridgeInstanceId": 5}
            """;

        Assert.Throws<FormatException>(() => Envelope.Decode(json));
    }

    /// <summary>Verifies that decoding one present identity key alongside two absent ones reads only that value.</summary>
    [Fact]
    public void DecodeReadsOnePresentIdentityFieldWhileTheOthersStayAbsent()
    {
        const string json = """
            {"protocolVersion": 2, "messageType": "ping", "messageId": "m-1", "sessionId": "s-1",
             "correlationId": null, "payload": {}, "clientId": "client-1"}
            """;

        Envelope envelope = Envelope.Decode(json);

        Assert.Null(envelope.BridgeInstanceId);
        Assert.Null(envelope.PlayContextId);
        Assert.Equal("client-1", envelope.ClientId);
    }
}
