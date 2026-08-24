using System.Globalization;

namespace DovahLinkValidationClient;

/// <summary>
/// Validates decoded <c>occurredAt</c> fields, shared by StateSnapshotPayload and
/// StateEventPayload. <c>protocol/schema/README.md</c>'s state envelope describes
/// <c>occurredAt</c> as UTC RFC 3339 wall-clock time for display and diagnostics, not an ordering
/// source.
/// </summary>
public static class ProtocolTimestampValidator
{
    /// <summary>
    /// Validates that a decoded timestamp string is a well-formed UTC RFC 3339 timestamp.
    /// </summary>
    /// <param name="value">The already-decoded timestamp string.</param>
    /// <exception cref="FormatException">Thrown when the value cannot be parsed as a UTC RFC 3339
    /// timestamp.</exception>
    public static void ValidateUtcRfc3339(string value)
    {
        string[] formats =
        [
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFF'Z'",
        ];
        bool parsed = DateTimeOffset.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset result);
        if (!parsed || result.Offset != TimeSpan.Zero)
        {
            throw new FormatException($"occurredAt must be a UTC RFC 3339 timestamp: {value}");
        }
    }
}
