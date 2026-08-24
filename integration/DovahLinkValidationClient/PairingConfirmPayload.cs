using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Outgoing <c>pairing_confirm</c> payload (<c>protocol/schema/README.md</c>'s
/// <c>pairing_confirm</c>). Encode-only: this client never decodes its own <c>pairing_confirm</c>.
/// </summary>
/// <param name="Code">The six-digit code the user read from Skyrim and entered.</param>
/// <param name="DisplayName">A presentation-only label for the resulting trusted client. Always
/// encoded as a key -- <see langword="null"/> preserves the client's existing display name on a
/// re-pair; an empty string clears it; any other value replaces it.</param>
public sealed record PairingConfirmPayload(string Code, string? DisplayName = null)
{
    /// <summary>
    /// Encodes this payload as a JSON object.
    /// </summary>
    /// <returns>The encoded <c>pairing_confirm</c> payload.</returns>
    public JsonObject Encode() => new()
    {
        ["code"] = Code,
        ["displayName"] = DisplayName,
    };
}
