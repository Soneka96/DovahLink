using DovahLink.DovahLinkBuilder.Persistence;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies <see cref="SettingsStore"/>'s persistence behavior.</summary>
public sealed class SettingsStoreTests
{
    /// <summary>Returns default settings when no file has ever been saved.</summary>
    [Fact]
    public void LoadReturnsDefaultsWhenNoFileExists()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new SettingsStore(temporaryDirectory.Path);

        BuilderSettings settings = store.Load();

        Assert.Equal(new BuilderSettings(), settings);
    }

    /// <summary>Round-trips saved settings exactly.</summary>
    [Fact]
    public void SaveThenLoadRoundTripsTheSettings()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new SettingsStore(temporaryDirectory.Path);
        var settings = new BuilderSettings(
            RepositoryPath: @"C:\Projects\DovahLink",
            SkyrimInstallPath: @"C:\Games\Skyrim Special Edition",
            OutputPath: @"D:\builds",
            OpenOutputFolderAfterSuccessfulBuild: false,
            AutoScrollLogs: false,
            VerboseCommandOutput: true,
            NotifyWhenBuildCompletes: true,
            WindowLeft: 120,
            WindowTop: 80,
            WindowWidth: 1024,
            WindowHeight: 768,
            WindowIsMaximized: true);

        store.Save(settings);

        Assert.Equal(settings, store.Load());
    }

    /// <summary>Creates the destination directory when it does not already exist.</summary>
    [Fact]
    public void SaveCreatesTheDirectoryWhenItDoesNotExist()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string nestedDirectory = Path.Combine(temporaryDirectory.Path, "nested", "settings-dir");
        var store = new SettingsStore(nestedDirectory);

        store.Save(new BuilderSettings());

        Assert.True(Directory.Exists(nestedDirectory));
    }

    /// <summary>Returns default settings, rather than throwing, when the saved file is corrupt.</summary>
    [Fact]
    public void LoadReturnsDefaultsWhenTheFileIsCorrupt()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "settings.json"), "{ not valid json");
        var store = new SettingsStore(temporaryDirectory.Path);

        Assert.Equal(new BuilderSettings(), store.Load());
    }

    /// <summary>
    /// Returns default settings for well-formed but empty JSON, distinguishing this from corrupt
    /// JSON: <c>{}</c> parses successfully and simply supplies none of the record's properties,
    /// rather than raising the <see cref="System.Text.Json.JsonException"/> the corrupt-file case relies on.
    /// </summary>
    [Fact]
    public void LoadReturnsDefaultsForWellFormedButEmptyJson()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "settings.json"), "{}");
        var store = new SettingsStore(temporaryDirectory.Path);

        Assert.Equal(new BuilderSettings(), store.Load());
    }

    /// <summary>Returns default settings, rather than throwing, when the saved file cannot be read due to a sharing violation.</summary>
    [Fact]
    public void LoadReturnsDefaultsWhenTheFileCannotBeRead()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(temporaryDirectory.Path, "settings.json");
        File.WriteAllText(filePath, "{}");
        var store = new SettingsStore(temporaryDirectory.Path);
        using var lockingStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal(new BuilderSettings(), store.Load());
    }

    /// <summary>Leaves only the final settings file behind, proving the write goes through a temporary file rather than truncating it in place.</summary>
    [Fact]
    public void SaveDoesNotLeaveATemporaryFileBehind()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new SettingsStore(temporaryDirectory.Path);

        store.Save(new BuilderSettings());

        Assert.Equal(["settings.json"], Directory.GetFiles(temporaryDirectory.Path).Select(Path.GetFileName));
    }

    /// <summary>
    /// Throws, rather than silently discarding the settings, when the file cannot be written, and
    /// cleans up the temporary file used for the atomic write rather than leaving it behind.
    /// </summary>
    [Fact]
    public void SaveThrowsWhenTheFileCannotBeWrittenAndCleansUpTheTemporaryFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string filePath = Path.Combine(temporaryDirectory.Path, "settings.json");
        File.WriteAllText(filePath, "{}");
        var store = new SettingsStore(temporaryDirectory.Path);
        using var lockingStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Exception? exception = Record.Exception(() => store.Save(new BuilderSettings()));

        Assert.True(exception is IOException or UnauthorizedAccessException, $"Expected an IOException or UnauthorizedAccessException but got {exception?.GetType()}.");
        Assert.False(File.Exists(filePath + ".tmp"));
    }
}
