using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Outgoing <c>snapshot_request</c> payload (<c>protocol/schema/README.md</c>'s
/// <c>snapshot_request</c>). Encode-only: this client never decodes its own
/// <c>snapshot_request</c>.
/// </summary>
/// <param name="StateArea">The state area whose snapshot is requested.</param>
/// <param name="KnownRevision">The client's latest known revision, when available; advisory only.
/// Omitted from the encoded object entirely (not merely <see langword="null"/>) when absent.</param>
public sealed record SnapshotRequestPayload(string StateArea, int? KnownRevision = null)
{
    /// <summary>
    /// Encodes this payload as a JSON object.
    /// </summary>
    /// <returns>The encoded <c>snapshot_request</c> payload.</returns>
    public JsonObject Encode()
    {
        var obj = new JsonObject { ["stateArea"] = StateArea };
        if (KnownRevision is not null)
        {
            obj["knownRevision"] = KnownRevision;
        }
        return obj;
    }
}
