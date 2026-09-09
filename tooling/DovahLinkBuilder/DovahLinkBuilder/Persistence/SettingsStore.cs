using System.IO;
using System.Text.Json;

namespace DovahLink.DovahLinkBuilder.Persistence;

/// <summary>Persists the Builder's local settings.</summary>
public interface ISettingsStore
{
    /// <summary>
    /// Loads the persisted settings, or <see cref="BuilderSettings"/>'s defaults when none have been
    /// saved yet or the saved file cannot be read.
    /// </summary>
    /// <returns>The loaded or default settings.</returns>
    BuilderSettings Load();

    /// <summary>Persists <paramref name="settings"/>, replacing any previously saved value.</summary>
    /// <param name="settings">The settings to persist.</param>
    void Save(BuilderSettings settings);
}

/// <inheritdoc cref="ISettingsStore"/>
public sealed class SettingsStore : ISettingsStore
{
    /// <summary>The settings file name within the store's directory.</summary>
    private const string FileName = "settings.json";

    /// <summary>The full path to the settings file.</summary>
    private readonly string filePath;

    /// <summary>Initializes a store rooted at the specified directory.</summary>
    /// <param name="directoryPath">The directory the settings file is read from and written to.</param>
    public SettingsStore(string directoryPath)
    {
        filePath = Path.Combine(directoryPath, FileName);
    }

    /// <inheritdoc/>
    public BuilderSettings Load()
    {
        if (!File.Exists(filePath))
        {
            return new BuilderSettings();
        }

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<BuilderSettings>(json) ?? new BuilderSettings();
        }
        catch (JsonException)
        {
            // A corrupt local preferences file is not meaningful build state to preserve or fail
            // over; the Builder falls back to defaults rather than refusing to start.
            return new BuilderSettings();
        }
    }

    /// <inheritdoc/>
    public void Save(BuilderSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }
}
