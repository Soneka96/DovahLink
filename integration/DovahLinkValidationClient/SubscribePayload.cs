using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Outgoing <c>subscribe</c> payload (<c>protocol/schema/README.md</c>'s <c>subscribe</c>).
/// Encode-only: this client never decodes its own <c>subscribe</c>.
/// </summary>
/// <param name="StateAreas">The state areas requested.</param>
public sealed record SubscribePayload(IReadOnlyList<string> StateAreas)
{
    /// <summary>
    /// Encodes this payload as a JSON object.
    /// </summary>
    /// <returns>The encoded <c>subscribe</c> payload.</returns>
    public JsonObject Encode()
    {
        var array = new JsonArray();
        foreach (string area in StateAreas)
        {
            array.Add(area);
        }
        return new JsonObject { ["stateAreas"] = array };
    }
}
