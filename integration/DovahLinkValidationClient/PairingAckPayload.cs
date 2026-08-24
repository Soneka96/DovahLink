using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Outgoing <c>pairing_ack</c> payload (<c>protocol/schema/README.md</c>'s <c>pairing_ack</c>).
/// Encode-only: this client never decodes its own <c>pairing_ack</c>.
/// </summary>
/// <param name="Credential">The hex-encoded credential received in a prior
/// <c>credential_issued</c> outcome, saved to persistent storage before this message is sent.</param>
public sealed record PairingAckPayload(string Credential)
{
    /// <summary>
    /// Encodes this payload as a JSON object.
    /// </summary>
    /// <returns>The encoded <c>pairing_ack</c> payload.</returns>
    public JsonObject Encode() => new() { ["credential"] = Credential };
}
