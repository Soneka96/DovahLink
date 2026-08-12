using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

// A hand-written encoder/decoder for the canonical DovahLink envelope
// (protocol/schema/README.md), deliberately independent from the bridge's
// own C++ codec: TASK.md requires "the validation client must be an
// independent protocol consumer; it must not share the bridge's C++
// codec."
public sealed record Envelope(
    int ProtocolVersion,
    string MessageType,
    string MessageId,
    string? SessionId,
    string? CorrelationId,
    JsonObject Payload)
{
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
        return root.ToJsonString();
    }

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

            return new Envelope(protocolVersion, messageType, messageId, sessionId, correlationId, payload);
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
