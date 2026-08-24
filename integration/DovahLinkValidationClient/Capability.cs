using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// One entry in a <c>capabilities</c> message's <c>capabilities</c> list
/// (<c>protocol/schema/README.md</c>'s <c>capabilities</c>). Both endpoints send this shape, so
/// this type supports both directions.
/// </summary>
/// <param name="Id">The capability identifier. Canonical protocol values, independent of the
/// Bridge release version; no capability is currently registered.</param>
/// <param name="Version">The capability version.</param>
public sealed record Capability(string Id, int Version)
{
    /// <summary>
    /// Encodes this capability entry as a JSON object.
    /// </summary>
    /// <returns>The encoded capability entry.</returns>
    /// <exception cref="FormatException">Thrown when <see cref="Id"/> is empty or
    /// <see cref="Version"/> is negative.</exception>
    public JsonObject Encode()
    {
        if (string.IsNullOrEmpty(Id))
        {
            throw new FormatException("id must be a non-empty string.");
        }
        if (Version < 0)
        {
            throw new FormatException("version must be a non-negative integer.");
        }

        return new JsonObject { ["id"] = Id, ["version"] = Version };
    }

    /// <summary>
    /// Decodes and validates one capability entry.
    /// </summary>
    /// <param name="entry">The capability entry's decoded JSON object.</param>
    /// <returns>The decoded capability entry.</returns>
    /// <exception cref="FormatException">Thrown when a required field is missing, has the wrong JSON
    /// type, or violates the capability identifier/version rules.</exception>
    public static Capability Decode(JsonObject entry)
    {
        try
        {
            string id = entry["id"]?.GetValue<string>() ?? throw new FormatException("Missing id.");
            int version = entry["version"]?.GetValue<int>() ?? throw new FormatException("Missing version.");
            if (id.Length == 0)
            {
                throw new FormatException("id must be a non-empty string.");
            }
            if (version < 0)
            {
                throw new FormatException("version must be a non-negative integer.");
            }
            return new Capability(id, version);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"Malformed capability entry: {ex.Message}", ex);
        }
    }
}
