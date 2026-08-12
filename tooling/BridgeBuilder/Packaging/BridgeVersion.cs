using System.Text.Json;

namespace DovahLink.BridgeBuilder.Packaging;

public readonly record struct BridgeVersion(int Major, int Minor, int Patch)
{
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

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
