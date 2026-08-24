using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// Incoming <c>pairing_outcome</c> payload (<c>protocol/schema/README.md</c>'s
/// <c>pairing_outcome</c>). Decode-only: this client never sends <c>pairing_outcome</c>. Shared
/// reply to <c>pairing_confirm</c>, <c>pairing_ack</c>, <c>pairing_renotify</c>, and
/// <c>pairing_cancel</c>, distinguished by <see cref="Outcome"/>.
/// </summary>
/// <param name="Outcome">The wire vocabulary of <c>pairing_outcome.outcome</c>.</param>
/// <param name="Credential">Present only for <c>"credential_issued"</c>, <c>"trusted"</c>, and
/// <c>"already_trusted"</c>.</param>
/// <param name="ShortId">The administration-only identifier, present only for <c>"trusted"</c> and
/// <c>"already_trusted"</c>.</param>
/// <param name="DisplayName">Echoes the client-supplied label, present only alongside
/// <paramref name="Credential"/>/<paramref name="ShortId"/> when the client supplied one.</param>
/// <param name="RetryAfterSeconds">The minimum safe wait in seconds before retrying, present for
/// <c>"pacing_limited"</c> and <c>"renotify_cooldown"</c>.</param>
public sealed record PairingOutcomePayload(
    string Outcome,
    string? Credential,
    string? ShortId,
    string? DisplayName,
    int? RetryAfterSeconds)
{
    /// <summary>
    /// Decodes and validates one <c>pairing_outcome</c> payload.
    /// </summary>
    /// <param name="payload">The envelope's decoded payload object.</param>
    /// <returns>The decoded pairing-outcome payload.</returns>
    /// <exception cref="FormatException">Thrown when a required key is missing or a value has the
    /// wrong JSON type.</exception>
    public static PairingOutcomePayload Decode(JsonObject payload)
    {
        try
        {
            string outcome = payload["outcome"]?.GetValue<string>() ?? throw new FormatException("Missing outcome.");
            return new PairingOutcomePayload(
                outcome,
                Credential: GetRequiredNullableString(payload, "credential"),
                ShortId: GetRequiredNullableString(payload, "shortId"),
                DisplayName: GetRequiredNullableString(payload, "displayName"),
                RetryAfterSeconds: GetRequiredNullableInt(payload, "retryAfterSeconds"));
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed pairing_outcome payload: {ex.Message}", ex);
        }
    }

    /// <summary>Extracts a required, nullable string field, rejecting an absent key.</summary>
    /// <param name="payload">The payload object.</param>
    /// <param name="key">The field's key.</param>
    /// <returns>The field's string value, or <see langword="null"/>.</returns>
    /// <exception cref="FormatException">Thrown when the key is absent.</exception>
    private static string? GetRequiredNullableString(JsonObject payload, string key)
    {
        if (!payload.ContainsKey(key))
        {
            throw new FormatException($"Missing {key}.");
        }
        return payload[key]?.GetValue<string>();
    }

    /// <summary>Extracts a required, nullable int field, rejecting an absent key.</summary>
    /// <param name="payload">The payload object.</param>
    /// <param name="key">The field's key.</param>
    /// <returns>The field's int value, or <see langword="null"/>.</returns>
    /// <exception cref="FormatException">Thrown when the key is absent.</exception>
    private static int? GetRequiredNullableInt(JsonObject payload, string key)
    {
        if (!payload.ContainsKey(key))
        {
            throw new FormatException($"Missing {key}.");
        }
        return payload[key]?.GetValue<int>();
    }
}
