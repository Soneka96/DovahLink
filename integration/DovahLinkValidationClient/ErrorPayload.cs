using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>error</c> payload (<c>protocol/schema/README.md</c>'s <c>error</c>). Decode-only:
/// this client never sends <c>error</c>.
/// </summary>
/// <param name="Code">The stable, machine-readable error code. For branching; never
/// <paramref name="Message"/>.</param>
/// <param name="Message">The human-readable, diagnostic-only description. Must not be used for
/// branching.</param>
/// <param name="Retryable">Whether retrying the failed operation is allowed.</param>
/// <param name="Details">Optional structured error details. Absent (the key may be omitted
/// entirely) when no safe diagnostic details exist.</param>
public sealed record ErrorPayload(string Code, string Message, bool Retryable, JsonNode? Details)
{
    /// <summary>
    /// Decodes and validates one <c>error</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded error payload.</returns>
    /// <exception cref="FormatException">Thrown when a required field is missing or the wrong JSON
    /// type.</exception>
    public static ErrorPayload Decode(JsonObject payload)
    {
        try
        {
            string code = payload["code"]?.GetValue<string>() ?? throw new FormatException("Missing code.");
            string message = payload["message"]?.GetValue<string>() ?? throw new FormatException("Missing message.");
            bool retryable = payload["retryable"]?.GetValue<bool>() ?? throw new FormatException("Missing retryable.");
            JsonNode? details = payload["details"];
            return new ErrorPayload(code, message, retryable, details);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed error payload: {ex.Message}", ex);
        }
    }
}
