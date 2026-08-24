using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>subscription_ack</c> payload (<c>protocol/schema/README.md</c>'s
/// <c>subscription_ack</c>). Decode-only: this client never sends <c>subscription_ack</c>.
/// </summary>
/// <param name="AcceptedStateAreas">The state areas accepted by the bridge.</param>
/// <param name="RejectedStateAreas">The state areas rejected by the bridge.</param>
public sealed record SubscriptionAckPayload(
    IReadOnlyList<string> AcceptedStateAreas,
    IReadOnlyList<string> RejectedStateAreas)
{
    /// <summary>
    /// Decodes and validates one <c>subscription_ack</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded subscription-ack payload.</returns>
    /// <exception cref="FormatException">Thrown when a required field is missing, not an array, or
    /// contains a non-empty string violation.</exception>
    public static SubscriptionAckPayload Decode(JsonObject payload)
    {
        try
        {
            IReadOnlyList<string> accepted = DecodeStringArray(payload, "acceptedStateAreas");
            IReadOnlyList<string> rejected = DecodeStringArray(payload, "rejectedStateAreas");
            return new SubscriptionAckPayload(accepted, rejected);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed subscription_ack payload: {ex.Message}", ex);
        }
    }

    /// <summary>Decodes one required array-of-strings field.</summary>
    /// <param name="payload">The payload object.</param>
    /// <param name="key">The field's key.</param>
    /// <returns>The decoded string values, in order.</returns>
    /// <exception cref="FormatException">Thrown when the key is missing, not an array, or contains
    /// a null, empty, or non-string entry.</exception>
    private static IReadOnlyList<string> DecodeStringArray(JsonObject payload, string key)
    {
        JsonArray array = payload[key] as JsonArray ?? throw new FormatException($"Missing or malformed {key}.");
        var values = new List<string>();
        foreach (JsonNode? node in array)
        {
            string value = node?.GetValue<string>()
                ?? throw new FormatException($"{key} entries must be non-null strings.");
            if (value.Length == 0)
            {
                throw new FormatException($"{key} entries must be non-empty strings.");
            }
            values.Add(value);
        }
        return values;
    }
}
