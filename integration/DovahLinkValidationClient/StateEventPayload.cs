using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>state_event</c> payload (<c>protocol/schema/README.md</c>'s <c>state_event</c>).
/// Decode-only: this client never sends <c>state_event</c>. <c>Data</c> stays an untyped
/// <see cref="JsonObject"/>: no state area is currently registered (protocol/schema/README.md's
/// "Registered state areas"), so there is no domain shape to decode it against yet. Contains
/// complete post-change state, not a patch.
/// </summary>
/// <param name="StateArea">The state area this event represents.</param>
/// <param name="BaseRevision">The revision this event expects the client to have before applying it.</param>
/// <param name="Revision">The revision established by this event. Must equal
/// <paramref name="BaseRevision"/> + 1.</param>
/// <param name="OccurredAt">UTC RFC 3339 wall-clock time for display and diagnostics; not an
/// ordering source.</param>
/// <param name="Data">The complete post-change, state-area-specific data.</param>
public sealed record StateEventPayload(
    string StateArea,
    int BaseRevision,
    int Revision,
    string OccurredAt,
    JsonObject Data)
{
    /// <summary>
    /// Decodes and validates one <c>state_event</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded state-event payload.</returns>
    /// <exception cref="FormatException">Thrown when a required field is missing, the wrong JSON
    /// type, or fails semantic validation (an empty state area, negative revision, malformed
    /// timestamp, or a revision that does not equal baseRevision + 1).</exception>
    public static StateEventPayload Decode(JsonObject payload)
    {
        try
        {
            string stateArea = payload["stateArea"]?.GetValue<string>()
                ?? throw new FormatException("Missing stateArea.");
            if (stateArea.Length == 0)
            {
                throw new FormatException("stateArea must be a non-empty string.");
            }
            int baseRevision = payload["baseRevision"]?.GetValue<int>()
                ?? throw new FormatException("Missing baseRevision.");
            ProtocolRevisionValidator.ValidateNonNegative(baseRevision, "baseRevision");
            int revision = payload["revision"]?.GetValue<int>()
                ?? throw new FormatException("Missing revision.");
            ProtocolRevisionValidator.ValidateNonNegative(revision, "revision");
            string occurredAt = payload["occurredAt"]?.GetValue<string>()
                ?? throw new FormatException("Missing occurredAt.");
            ProtocolTimestampValidator.ValidateUtcRfc3339(occurredAt);
            JsonObject data = payload["data"] as JsonObject ?? throw new FormatException("Missing or malformed data.");
            if (revision != baseRevision + 1)
            {
                throw new FormatException("revision must equal baseRevision + 1.");
            }
            return new StateEventPayload(stateArea, baseRevision, revision, occurredAt, data);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed state_event payload: {ex.Message}", ex);
        }
    }
}
