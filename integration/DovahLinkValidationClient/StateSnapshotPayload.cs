using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>state_snapshot</c> payload (<c>protocol/schema/README.md</c>'s <c>state_snapshot</c>).
/// Decode-only: this client never sends <c>state_snapshot</c>. <c>Data</c> stays an untyped
/// <see cref="JsonObject"/>: no state area is currently registered (protocol/schema/README.md's
/// "Registered state areas"), so there is no domain shape to decode it against yet.
/// </summary>
/// <param name="StateArea">The state area this snapshot represents.</param>
/// <param name="Revision">The revision established by this snapshot.</param>
/// <param name="OccurredAt">UTC RFC 3339 wall-clock time for display and diagnostics; not an
/// ordering source.</param>
/// <param name="Data">The state-area-specific snapshot data.</param>
public sealed record StateSnapshotPayload(string StateArea, int Revision, string OccurredAt, JsonObject Data)
{
    /// <summary>
    /// Decodes and validates one <c>state_snapshot</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded state-snapshot payload.</returns>
    /// <exception cref="FormatException">Thrown when a required field is missing, the wrong JSON
    /// type, or fails semantic validation (an empty state area, negative revision, or malformed
    /// timestamp).</exception>
    public static StateSnapshotPayload Decode(JsonObject payload)
    {
        try
        {
            string stateArea = payload["stateArea"]?.GetValue<string>()
                ?? throw new FormatException("Missing stateArea.");
            if (stateArea.Length == 0)
            {
                throw new FormatException("stateArea must be a non-empty string.");
            }
            int revision = payload["revision"]?.GetValue<int>()
                ?? throw new FormatException("Missing revision.");
            ProtocolRevisionValidator.ValidateNonNegative(revision, "revision");
            string occurredAt = payload["occurredAt"]?.GetValue<string>()
                ?? throw new FormatException("Missing occurredAt.");
            ProtocolTimestampValidator.ValidateUtcRfc3339(occurredAt);
            JsonObject data = payload["data"] as JsonObject ?? throw new FormatException("Missing or malformed data.");
            return new StateSnapshotPayload(stateArea, revision, occurredAt, data);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed state_snapshot payload: {ex.Message}", ex);
        }
    }
}
