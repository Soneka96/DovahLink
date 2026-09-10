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
    /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the file cannot be written due to insufficient permissions.</exception>
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked or inaccessible preferences file is no more meaningful to preserve or fail
            // over than a corrupt one, per this method's own documented "cannot be read" contract.
            return new BuilderSettings();
        }
    }

    /// <inheritdoc/>
    public void Save(BuilderSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        // Written to a temporary file first and moved into place, rather than written directly to
        // filePath, so a process stopped mid-write can never leave a partially written, unreadable
        // settings file behind: File.Move with overwrite replaces the destination in one step. The
        // finally block deletes the temporary file if the write or move above failed; it is already
        // gone (a no-op delete) once the move has succeeded.
        string temporaryPath = filePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
