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
}
