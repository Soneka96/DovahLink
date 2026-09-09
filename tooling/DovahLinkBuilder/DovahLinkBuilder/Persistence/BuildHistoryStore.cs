using System.IO;
using System.Text.Json;

namespace DovahLink.DovahLinkBuilder.Persistence;

/// <summary>Persists the Builder's recent build history.</summary>
public interface IBuildHistoryStore
{
    /// <summary>Gets the retained build history, most recent first.</summary>
    /// <returns>Up to the retained maximum, most recent first, or an empty list when none exist or the saved file cannot be read.</returns>
    IReadOnlyList<BuildHistoryEntry> GetRecent();

    /// <summary>
    /// Records <paramref name="entry"/> as the most recent build, trimming the oldest entry once the
    /// retained maximum is exceeded.
    /// </summary>
    /// <param name="entry">The build to record.</param>
    /// <exception cref="IOException">Thrown when the file cannot be written. Unlike <see cref="GetRecent"/>, this is not swallowed: a caller for whom a failed recording is non-critical (see <see cref="Ui.BuildPageViewModel"/>'s own handling) must catch it itself.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the file cannot be written due to insufficient permissions.</exception>
    void Add(BuildHistoryEntry entry);
}

/// <inheritdoc cref="IBuildHistoryStore"/>
public sealed class BuildHistoryStore : IBuildHistoryStore
{
    /// <summary>The maximum number of entries retained.</summary>
    private const int MaxEntries = 10;

    /// <summary>The build history file name within the store's directory.</summary>
    private const string FileName = "build-history.json";

    /// <summary>The full path to the build history file.</summary>
    private readonly string filePath;

    /// <summary>Initializes a store rooted at the specified directory.</summary>
    /// <param name="directoryPath">The directory the build history file is read from and written to.</param>
    public BuildHistoryStore(string directoryPath)
    {
        filePath = Path.Combine(directoryPath, FileName);
    }

    /// <inheritdoc/>
    public IReadOnlyList<BuildHistoryEntry> GetRecent()
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<BuildHistoryEntry>>(json) ?? [];
        }
        catch (JsonException)
        {
            // A corrupt local history file is not meaningful build state to preserve or fail over;
            // the Builder falls back to an empty history rather than refusing to start.
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked or inaccessible history file is no more meaningful to preserve or fail over
            // than a corrupt one, per this method's own documented "cannot be read" contract.
            return [];
        }
    }

    /// <inheritdoc/>
    public void Add(BuildHistoryEntry entry)
    {
        List<BuildHistoryEntry> entries = [entry, .. GetRecent()];
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }
}
