using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// An independent encoder and decoder for one DovahLink protocol envelope.
/// </summary>
/// <param name="ProtocolVersion">The protocol version carried by the message.</param>
/// <param name="MessageType">The canonical message type.</param>
/// <param name="MessageId">The unique message identifier.</param>
/// <param name="SessionId">The session identifier, when one exists.</param>
/// <param name="CorrelationId">The correlated request identifier, when one exists.</param>
/// <param name="Payload">The message-specific JSON object.</param>
/// <param name="BridgeInstanceId">The identity of the bridge instance that produced this message.
/// Only encoded once <paramref name="ProtocolVersion"/> is 2 or higher; absent (not merely null)
/// below that.</param>
/// <param name="PlayContextId">The identity of the currently loaded play context, when one is
/// active. Only encoded once <paramref name="ProtocolVersion"/> is 2 or higher.</param>
/// <param name="ClientId">The identity of the logical client, established at <c>hello</c>. Only
/// encoded once <paramref name="ProtocolVersion"/> is 2 or higher.</param>
public sealed record Envelope(
    int ProtocolVersion,
    string MessageType,
    string MessageId,
    string? SessionId,
    string? CorrelationId,
    JsonObject Payload,
    string? BridgeInstanceId = null,
    string? PlayContextId = null,
    string? ClientId = null)
{
    /// <summary>
    /// Encodes the envelope as a JSON string.
    /// </summary>
    /// <returns>A JSON object containing the envelope metadata and payload.</returns>
    public string Encode()
    {
        var root = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["messageType"] = MessageType,
            ["messageId"] = MessageId,
            ["sessionId"] = SessionId,
            ["correlationId"] = CorrelationId,
            ["payload"] = Payload.DeepClone(),
        };
        // v1 omits these keys entirely; v2 always emits them, as a value or
        // JSON null, matching protocol/schema/README.md's v2 encoding rule.
        if (ProtocolVersion >= 2)
        {
            root["bridgeInstanceId"] = BridgeInstanceId;
            root["playContextId"] = PlayContextId;
            root["clientId"] = ClientId;
        }
        return root.ToJsonString();
    }

    /// <summary>
    /// Decodes a JSON string into a validated protocol envelope.
    /// </summary>
    /// <param name="json">The JSON representation of the envelope.</param>
    /// <returns>The decoded envelope.</returns>
    /// <exception cref="FormatException">Thrown when required fields are missing or have invalid JSON types.</exception>
    public static Envelope Decode(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("Envelope is not a JSON object.");

            int protocolVersion = root["protocolVersion"]?.GetValue<int>() ?? throw new FormatException("Missing protocolVersion.");
            string messageType = root["messageType"]?.GetValue<string>() ?? throw new FormatException("Missing messageType.");
            string messageId = root["messageId"]?.GetValue<string>() ?? throw new FormatException("Missing messageId.");
            string? sessionId = root["sessionId"]?.GetValue<string>();
            string? correlationId = root["correlationId"]?.GetValue<string>();
            JsonObject payload = root["payload"]?.AsObject() ?? throw new FormatException("Missing payload.");
            // Decoded tolerantly regardless of protocolVersion (absent and
            // explicit null are indistinguishable through JsonObject's
            // indexer, and both correctly decode to null here): only Encode
            // enforces which version may write these fields.
            string? bridgeInstanceId = root["bridgeInstanceId"]?.GetValue<string>();
            string? playContextId = root["playContextId"]?.GetValue<string>();
            string? clientId = root["clientId"]?.GetValue<string>();

            return new Envelope(protocolVersion, messageType, messageId, sessionId, correlationId, payload,
                bridgeInstanceId, playContextId, clientId);
        }
        catch (InvalidOperationException ex)
        {
            // GetValue<T>() throws InvalidOperationException when a field
            // exists but is the wrong JSON kind (e.g. messageType is a
            // number). Normalized to FormatException -- the type already
            // used for a missing field above -- so every decode failure is
            // the same exception type for callers to catch.
            throw new FormatException($"Malformed envelope: {ex.Message}", ex);
        }
    }
}
