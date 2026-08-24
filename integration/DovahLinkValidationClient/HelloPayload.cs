using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Outgoing <c>hello</c> payload (<c>protocol/schema/README.md</c>'s <c>hello</c>). Encode-only:
/// this client never decodes its own <c>hello</c>.
/// </summary>
/// <param name="ClientId">Identifies the logical client/installation, independent of any
/// connection.</param>
/// <param name="Auth">The nested authentication object.</param>
public sealed record HelloPayload(string ClientId, HelloAuthPayload Auth)
{
    /// <summary>The sender's role. Always <c>"client"</c>: only the connecting client sends
    /// <c>hello</c>.</summary>
    public string Endpoint => "client";

    /// <summary>
    /// Encodes this payload as a JSON object.
    /// </summary>
    /// <returns>The encoded <c>hello</c> payload.</returns>
    public JsonObject Encode() => new()
    {
        ["endpoint"] = Endpoint,
        ["clientId"] = ClientId,
        ["auth"] = Auth.Encode(),
    };
}
