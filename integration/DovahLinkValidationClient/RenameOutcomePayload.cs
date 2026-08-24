using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>rename_outcome</c> payload (<c>protocol/schema/README.md</c>'s <c>rename_outcome</c>).
/// Decode-only: this client never sends <c>rename_outcome</c>.
/// </summary>
/// <param name="Outcome">The wire vocabulary of <c>rename_outcome.outcome</c>: <c>"renamed"</c>,
/// <c>"invalid_display_name"</c>, or <c>"not_trusted"</c>.</param>
/// <param name="DisplayName">Echoes the resulting name, present only for <c>"renamed"</c>;
/// <see langword="null"/> otherwise.</param>
public sealed record RenameOutcomePayload(string Outcome, string? DisplayName)
{
    /// <summary>
    /// Decodes and validates one <c>rename_outcome</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded rename-outcome payload.</returns>
    /// <exception cref="FormatException">Thrown when a required key is missing or a value has the
    /// wrong JSON type.</exception>
    public static RenameOutcomePayload Decode(JsonObject payload)
    {
        try
        {
            string outcome = payload["outcome"]?.GetValue<string>() ?? throw new FormatException("Missing outcome.");
            if (!payload.ContainsKey("displayName"))
            {
                throw new FormatException("Missing displayName.");
            }
            string? displayName = payload["displayName"]?.GetValue<string>();
            return new RenameOutcomePayload(outcome, displayName);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed rename_outcome payload: {ex.Message}", ex);
        }
    }
}
