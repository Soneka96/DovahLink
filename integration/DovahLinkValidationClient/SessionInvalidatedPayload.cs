using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>session_invalidated</c> payload (<c>protocol/schema/README.md</c>'s
/// <c>session_invalidated</c>). Decode-only: this client never sends <c>session_invalidated</c>.
/// An unsolicited, best-effort terminal event for an administratively invalidated session.
/// </summary>
/// <param name="Reason">The wire vocabulary of <c>session_invalidated.reason</c>: <c>"revoked"</c>,
/// <c>"blocked"</c>, <c>"trust_reset"</c>, or <c>"factory_reset"</c>.</param>
public sealed record SessionInvalidatedPayload(string Reason)
{
    /// <summary>
    /// Decodes and validates one <c>session_invalidated</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded session-invalidated payload.</returns>
    /// <exception cref="FormatException">Thrown when reason is missing, has the wrong JSON type, or
    /// is not a registered invalidation reason.</exception>
    public static SessionInvalidatedPayload Decode(JsonObject payload)
    {
        try
        {
            string reason = payload["reason"]?.GetValue<string>() ?? throw new FormatException("Missing reason.");
            if (reason is not ("revoked" or "blocked" or "trust_reset" or "factory_reset"))
            {
                throw new FormatException($"Unknown session invalidation reason: {reason}.");
            }
            return new SessionInvalidatedPayload(reason);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed session_invalidated payload: {ex.Message}", ex);
        }
    }
}
