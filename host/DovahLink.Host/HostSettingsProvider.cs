using System.Text.Json;

namespace DovahLink.Host;

/// <summary>
/// Resolves <see cref="HostSettings"/> from the user-editable settings file at
/// <see cref="Constants.HostSettingsFilePath"/>.
/// </summary>
public interface IHostSettingsProvider
{
    /// <summary>
    /// Reads and validates the settings file, falling back to <see cref="Constants.MaxActiveSessions"/>
    /// for any field that is missing, unreadable, malformed, or out of the accepted range, rather
    /// than failing host startup over a hand-edited settings mistake.
    /// </summary>
    HostSettings Load();
}

/// <inheritdoc cref="IHostSettingsProvider"/>
public sealed class HostSettingsProvider : IHostSettingsProvider
{
    /// <summary>The JSON options the settings file is parsed with: case-insensitive property names.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The settings file path this provider reads from.</summary>
    private readonly string filePath;

    /// <summary>Creates a provider reading from the production settings file at <see cref="Constants.HostSettingsFilePath"/>.</summary>
    public HostSettingsProvider()
        : this(Constants.HostSettingsFilePath)
    {
    }

    /// <summary>Creates a provider reading from an explicit file path, for a test that redirects it to a private file.</summary>
    /// <param name="filePath">The settings file path to read from.</param>
    internal HostSettingsProvider(string filePath)
    {
        this.filePath = filePath;
    }

    /// <inheritdoc/>
    public HostSettings Load()
    {
        return new HostSettings(MaxActiveSessions: TryReadValidatedMaxActiveSessions() ?? Constants.MaxActiveSessions);
    }

    /// <summary>
    /// Reads and validates the settings file's <c>maxActiveSessions</c> field.
    /// </summary>
    /// <returns>
    /// The configured value when the file exists, parses, and carries a value within
    /// <c>[1, <see cref="Constants.MaxActiveSessionsCeiling"/>]</c>; otherwise <see langword="null"/>.
    /// </returns>
    private int? TryReadValidatedMaxActiveSessions()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            HostSettingsFile? parsed = JsonSerializer.Deserialize<HostSettingsFile>(stream, SerializerOptions);
            if (parsed?.MaxActiveSessions is int configured && configured is >= 1 and <= Constants.MaxActiveSessionsCeiling)
            {
                return configured;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A malformed or unreadable settings file falls back to the shipped default rather than
            // failing host startup over a hand-edited mistake in a low-stakes tuning file; unlike
            // the trust store, nothing security-sensitive is ever persisted here.
        }

        return null;
    }
}
