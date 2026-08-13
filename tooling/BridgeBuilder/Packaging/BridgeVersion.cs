using System.Text.Json;

namespace DovahLink.BridgeBuilder.Packaging;

public readonly record struct BridgeVersion(int Major, int Minor, int Patch)
{
    /// <summary>
    /// Creates a bridge version from a vcpkg manifest JSON document.
    /// </summary>
    /// <param name="manifestJson">The vcpkg manifest JSON text.</param>
    /// <returns>The version specified by the manifest.</returns>
    /// <exception cref="FormatException">Thrown when the manifest is invalid JSON or contains an invalid version.</exception>
    public static BridgeVersion FromVcpkgManifest(string manifestJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifestJson);
        }
        catch (JsonException exception)
        {
            throw new FormatException("bridge/vcpkg.json is not valid JSON.", exception);
        }

        using (document)
        {
            return ParseVersion(document.RootElement);
        }
    }

    /// <summary>
    /// Parses a bridge version from a JSON manifest object.
    /// </summary>
    /// <param name="root">The JSON element containing the manifest.</param>
    /// <returns>The parsed major, minor, and patch version.</returns>
    /// <exception cref="FormatException">Thrown when the manifest structure or version format is invalid.</exception>
    private static BridgeVersion ParseVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("bridge/vcpkg.json must contain a JSON object.");
        }

        if (!root.TryGetProperty("version-string", out JsonElement versionElement) ||
            versionElement.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("bridge/vcpkg.json must contain a string version-string property.");
        }

        string? value = versionElement.GetString();
        if (value is null)
        {
            throw new FormatException("bridge/vcpkg.json version-string cannot be null.");
        }

        string[] components = value.Split('.', StringSplitOptions.None);
        if (components.Length != 3 ||
            !int.TryParse(components[0], out int major) ||
            !int.TryParse(components[1], out int minor) ||
            !int.TryParse(components[2], out int patch) ||
            major < 0 || minor < 0 || patch < 0)
        {
            throw new FormatException($"Unsupported bridge version '{value}'. Expected MAJOR.MINOR.PATCH.");
        }

        return new BridgeVersion(major, minor, patch);
    }

    /// <summary>
/// Formats the bridge version as a period-separated major, minor, and patch version.
/// </summary>
/// <returns>The version in <c>MAJOR.MINOR.PATCH</c> format.</returns>
public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
