using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>pairing_status</c> payload (<c>protocol/schema/README.md</c>'s <c>pairing_status</c>).
/// Decode-only: this client never sends <c>pairing_status</c>.
/// </summary>
/// <param name="State">The wire vocabulary of <c>pairing_status.state</c>: <c>"unavailable"</c>,
/// <c>"available"</c>, <c>"in_progress"</c>, or <c>"other_device_pairing"</c>.</param>
/// <param name="ExpiresInSeconds">The active challenge's remaining code validity, when a number is
/// present. Collapses the wire's "present as null" and "omitted entirely" cases (both mean no
/// number to show) into <see langword="null"/>; a test that needs the raw key-presence distinction
/// reads the payload object directly via <see cref="ProtocolFixtures.ReadFixturePayload"/>.</param>
public sealed record PairingStatusPayload(string State, int? ExpiresInSeconds)
{
    /// <summary>
    /// Decodes and validates one <c>pairing_status</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded pairing-status payload.</returns>
    /// <exception cref="FormatException">Thrown when a required field is missing or the wrong JSON
    /// type.</exception>
    public static PairingStatusPayload Decode(JsonObject payload)
    {
        try
        {
            string state = payload["state"]?.GetValue<string>() ?? throw new FormatException("Missing state.");
            int? expiresInSeconds = payload.TryGetPropertyValue("expiresInSeconds", out JsonNode? node)
                ? node?.GetValue<int>()
                : null;
            return new PairingStatusPayload(state, expiresInSeconds);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed pairing_status payload: {ex.Message}", ex);
        }
    }
}
