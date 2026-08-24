using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// A <c>capabilities</c> message payload (<c>protocol/schema/README.md</c>'s <c>capabilities</c>),
/// sent by both endpoints after <c>hello_ack</c>. No capability is currently registered, so both
/// the Bridge's and this client's own list are always empty.
/// </summary>
/// <param name="Capabilities">The capabilities advertised or requested by the sender.</param>
public sealed record CapabilitiesPayload(IReadOnlyList<Capability> Capabilities)
{
    /// <summary>
    /// Encodes this payload as a JSON object.
    /// </summary>
    /// <returns>The encoded <c>capabilities</c> payload.</returns>
    public JsonObject Encode()
    {
        var array = new JsonArray();
        foreach (Capability capability in Capabilities)
        {
            array.Add(capability.Encode());
        }
        return new JsonObject { ["capabilities"] = array };
    }

    /// <summary>
    /// Decodes and validates one <c>capabilities</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded capabilities payload.</returns>
    /// <exception cref="FormatException">Thrown when <c>capabilities</c> is missing, not an array,
    /// or contains a malformed entry.</exception>
    public static CapabilitiesPayload Decode(JsonObject payload)
    {
        JsonArray array = payload["capabilities"] as JsonArray
            ?? throw new FormatException("Missing or malformed capabilities.");
        var capabilities = new List<Capability>();
        foreach (JsonNode? node in array)
        {
            JsonObject entry = node as JsonObject ?? throw new FormatException("Capability entry must be an object.");
            capabilities.Add(Capability.Decode(entry));
        }
        return new CapabilitiesPayload(capabilities);
    }
}
