using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// An independent encoder and decoder for one DovahLink protocol envelope.
/// </summary>
/// <param name="MessageType">The canonical message type.</param>
/// <param name="MessageId">The unique message identifier.</param>
/// <param name="SessionId">The session identifier, when one exists.</param>
/// <param name="CorrelationId">The correlated request identifier, when one exists.</param>
/// <param name="Payload">The message-specific JSON object.</param>
/// <param name="BridgeInstanceId">The identity of the bridge instance that produced this message.
/// `null` on the client's own <c>hello</c> (the client does not know it yet) and on a narrow set of
/// early connection-hygiene rejections the bridge cannot attach an identity to; present otherwise.</param>
/// <param name="PlayContextId">The identity of the currently loaded play context, when one is
/// active. `null` outside an active play context -- genuine semantic absence, not a placeholder.</param>
/// <param name="ClientId">The identity of the logical client, established at <c>hello</c>. `null`
/// before <c>hello</c> completes, the same shape <paramref name="SessionId"/> already uses.</param>
public sealed record Envelope(
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
            ["messageType"] = MessageType,
            ["messageId"] = MessageId,
            ["sessionId"] = SessionId,
            ["correlationId"] = CorrelationId,
            ["payload"] = Payload.DeepClone(),
            ["bridgeInstanceId"] = BridgeInstanceId,
            ["playContextId"] = PlayContextId,
            ["clientId"] = ClientId,
        };
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

            string messageType = root["messageType"]?.GetValue<string>() ?? throw new FormatException("Missing messageType.");
            string messageId = root["messageId"]?.GetValue<string>() ?? throw new FormatException("Missing messageId.");
            string? sessionId = root["sessionId"]?.GetValue<string>();
            string? correlationId = root["correlationId"]?.GetValue<string>();
            JsonObject payload = root["payload"]?.AsObject() ?? throw new FormatException("Missing payload.");
            // Required keys, nullable values: absent (not merely null) is
            // rejected, matching protocol/schema/README.md's envelope table.
            if (!root.ContainsKey("bridgeInstanceId"))
            {
                throw new FormatException("Missing bridgeInstanceId.");
            }
            if (!root.ContainsKey("playContextId"))
            {
                throw new FormatException("Missing playContextId.");
            }
            if (!root.ContainsKey("clientId"))
            {
                throw new FormatException("Missing clientId.");
            }
            string? bridgeInstanceId = root["bridgeInstanceId"]?.GetValue<string>();
            string? playContextId = root["playContextId"]?.GetValue<string>();
            string? clientId = root["clientId"]?.GetValue<string>();

            return new Envelope(messageType, messageId, sessionId, correlationId, payload,
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
