using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>pairing_status</c> payload (<c>protocol/schema/README.md</c>'s <c>pairing_status</c>).
/// Decode-only: this client never sends <c>pairing_status</c>.
/// </summary>
/// <param name="State">The wire vocabulary of <c>pairing_status.state</c>: <c>"unavailable"</c>,
/// <c>"available"</c>, <c>"in_progress"</c>, or <c>"other_device_pairing"</c>.</param>
/// <param name="ExpiresInSeconds">The active challenge's remaining code validity, when a number is
/// present. The decoder validates the state-specific wire-presence rules before collapsing valid
/// absent/present-null cases into <see langword="null"/>.</param>
public sealed record PairingStatusPayload(string State, int? ExpiresInSeconds)
{
    /// <summary>
    /// Decodes and validates one <c>pairing_status</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded pairing-status payload.</returns>
    /// <exception cref="FormatException">Thrown when a required field is missing, has the wrong JSON
    /// type, or violates the state's expiry rules.</exception>
    public static PairingStatusPayload Decode(JsonObject payload)
    {
        try
        {
            string state = payload["state"]?.GetValue<string>() ?? throw new FormatException("Missing state.");
            if (state is not ("unavailable" or "available" or "in_progress" or "other_device_pairing"))
            {
                throw new FormatException($"Unknown pairing status state: {state}.");
            }

            bool hasExpiry = payload.TryGetPropertyValue("expiresInSeconds", out JsonNode? node);
            if (state == "other_device_pairing")
            {
                if (hasExpiry)
                {
                    throw new FormatException("expiresInSeconds must be omitted for other_device_pairing.");
                }

                return new PairingStatusPayload(state, null);
            }

            if (!hasExpiry)
            {
                throw new FormatException($"expiresInSeconds must be present for {state}.");
            }

            int? expiresInSeconds = node?.GetValue<int>();
            if (expiresInSeconds is < 0)
            {
                throw new FormatException("expiresInSeconds must be a non-negative integer.");
            }
            if (state == "available" && expiresInSeconds is null)
            {
                throw new FormatException("expiresInSeconds must be a number for available.");
            }
            if (state == "unavailable" && expiresInSeconds is not null)
            {
                throw new FormatException("expiresInSeconds must be null for unavailable.");
            }

            return new PairingStatusPayload(state, expiresInSeconds);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed pairing_status payload: {ex.Message}", ex);
        }
    }
}
