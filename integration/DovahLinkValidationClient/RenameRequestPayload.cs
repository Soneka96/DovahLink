using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Outgoing <c>rename_request</c> payload (<c>protocol/schema/README.md</c>'s
/// <c>rename_request</c>). Encode-only: this client never decodes its own <c>rename_request</c>.
/// </summary>
/// <param name="DisplayName">The requested display name. May be empty, which clears the device's
/// display name.</param>
public sealed record RenameRequestPayload(string DisplayName)
{
    /// <summary>
    /// Encodes this payload as a JSON object.
    /// </summary>
    /// <returns>The encoded <c>rename_request</c> payload.</returns>
    public JsonObject Encode() => new() { ["displayName"] = DisplayName };
}
