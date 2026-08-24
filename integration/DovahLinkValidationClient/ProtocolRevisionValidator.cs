namespace DovahLinkValidationClient;

/// <summary>
/// Validates decoded revision fields, shared by StateSnapshotPayload and StateEventPayload.
/// <c>protocol/schema/README.md</c>'s state envelope describes every revision field as a
/// non-negative integer; <see cref="System.Text.Json.Nodes.JsonNode.GetValue{T}"/> already rejects
/// a non-integer JSON value, so this only needs to enforce the sign.
/// </summary>
public static class ProtocolRevisionValidator
{
    /// <summary>
    /// Validates that a decoded revision value is non-negative.
    /// </summary>
    /// <param name="value">The already-decoded revision value.</param>
    /// <param name="fieldName">The field's name, for the exception message.</param>
    /// <exception cref="FormatException">Thrown when the value is negative.</exception>
    public static void ValidateNonNegative(long value, string fieldName)
    {
        if (value < 0)
        {
            throw new FormatException($"{fieldName} must be a non-negative integer.");
        }
    }
}
