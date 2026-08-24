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
    /// <exception cref="FormatException">Thrown when a required key is missing, a value has the
    /// wrong JSON type, or fields do not match the outcome.</exception>
    public static PairingOutcomePayload Decode(JsonObject payload)
    {
        try
        {
            string outcome = payload["outcome"]?.GetValue<string>() ?? throw new FormatException("Missing outcome.");
            string? credential = GetRequiredNullableString(payload, "credential");
            string? shortId = GetRequiredNullableString(payload, "shortId");
            string? displayName = GetRequiredNullableString(payload, "displayName");
            int? retryAfterSeconds = GetRequiredNullableInt(payload, "retryAfterSeconds");
            ValidateSemantics(outcome, credential, shortId, displayName, retryAfterSeconds);
            return new PairingOutcomePayload(outcome, credential, shortId, displayName, retryAfterSeconds);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed pairing_outcome payload: {ex.Message}", ex);
        }
    }

    /// <summary>Validates outcome-specific field presence and value rules.</summary>
    /// <param name="outcome">The decoded outcome value.</param>
    /// <param name="credential">The decoded credential value.</param>
    /// <param name="shortId">The decoded administration identifier.</param>
    /// <param name="displayName">The decoded display name.</param>
    /// <param name="retryAfterSeconds">The decoded retry delay.</param>
    /// <exception cref="FormatException">Thrown when a value is invalid for the outcome.</exception>
    private static void ValidateSemantics(
        string outcome,
        string? credential,
        string? shortId,
        string? displayName,
        int? retryAfterSeconds)
    {
        if (outcome is not (
            "credential_issued" or
            "trusted" or
            "already_trusted" or
            "expired" or
            "invalid" or
            "pacing_limited" or
            "hard_limit_reached" or
            "pending_not_found" or
            "renotified" or
            "renotify_cooldown" or
            "cancelled" or
            "already_idle"))
        {
            throw new FormatException($"Unknown pairing outcome: {outcome}.");
        }

        bool carriesCredential = outcome is "credential_issued" or "trusted" or "already_trusted";
        bool carriesShortId = outcome is "trusted" or "already_trusted";
        bool carriesRetryAfterSeconds = outcome is "pacing_limited" or "renotify_cooldown";

        if ((credential is not null) != carriesCredential)
        {
            throw new FormatException($"credential presence is invalid for {outcome}.");
        }
        if (credential is { Length: 0 })
        {
            throw new FormatException("credential must not be empty when present.");
        }
        if ((shortId is not null) != carriesShortId)
        {
            throw new FormatException($"shortId presence is invalid for {outcome}.");
        }
        if (shortId is { Length: 0 })
        {
            throw new FormatException("shortId must not be empty when present.");
        }
        if (!carriesCredential && displayName is not null)
        {
            throw new FormatException($"displayName presence is invalid for {outcome}.");
        }
        if ((retryAfterSeconds is not null) != carriesRetryAfterSeconds)
        {
            throw new FormatException($"retryAfterSeconds presence is invalid for {outcome}.");
        }
        if (retryAfterSeconds is < 0)
        {
            throw new FormatException("retryAfterSeconds must be a non-negative integer.");
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
