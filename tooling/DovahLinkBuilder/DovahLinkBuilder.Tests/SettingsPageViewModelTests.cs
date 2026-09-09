using DovahLink.DovahLinkBuilder.Persistence;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the Settings page's path overrides, behavior toggles, and immediate persistence.</summary>
public sealed class SettingsPageViewModelTests
{
    /// <summary>Loads every field from the store's currently persisted settings.</summary>
    [Fact]
    public void ConstructorLoadsFieldsFromTheStore()
    {
        var store = new FakeSettingsStore
        {
            Settings = new BuilderSettings(
                RepositoryPath: @"D:\repo",
                SkyrimInstallPath: @"D:\Steam\steamapps\common\Skyrim Special Edition",
                OutputPath: @"D:\out",
                OpenOutputFolderAfterSuccessfulBuild: false,
                AutoScrollLogs: false,
                VerboseCommandOutput: true,
                NotifyWhenBuildCompletes: true),
        };

        var viewModel = new SettingsPageViewModel(store);

        Assert.Equal(@"D:\repo", viewModel.RepositoryPath);
        Assert.Equal(@"D:\Steam\steamapps\common\Skyrim Special Edition", viewModel.SkyrimInstallPath);
        Assert.Equal(@"D:\out", viewModel.OutputPath);
        Assert.False(viewModel.OpenOutputFolderAfterSuccessfulBuild);
        Assert.False(viewModel.AutoScrollLogs);
        Assert.True(viewModel.VerboseCommandOutput);
        Assert.True(viewModel.NotifyWhenBuildCompletes);
    }

    /// <summary>Saves the updated settings immediately when the repository path changes.</summary>
    [Fact]
    public void SettingRepositoryPathSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store);

        viewModel.RepositoryPath = @"D:\repo";

        Assert.Equal(@"D:\repo", store.Settings.RepositoryPath);
    }

    /// <summary>Saves the updated settings immediately when the Skyrim install path changes.</summary>
    [Fact]
    public void SettingSkyrimInstallPathSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store);

        viewModel.SkyrimInstallPath = @"D:\Skyrim";

        Assert.Equal(@"D:\Skyrim", store.Settings.SkyrimInstallPath);
    }

    /// <summary>Saves the updated settings immediately when the output path changes.</summary>
    [Fact]
    public void SettingOutputPathSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store);

        viewModel.OutputPath = @"D:\out";

        Assert.Equal(@"D:\out", store.Settings.OutputPath);
    }

    /// <summary>Clears the repository path override back to auto-detection and saves immediately.</summary>
    [Fact]
    public void ResetRepositoryPathCommandClearsTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\repo") };
        var viewModel = new SettingsPageViewModel(store);

        viewModel.ResetRepositoryPathCommand.Execute(null);

        Assert.Null(viewModel.RepositoryPath);
        Assert.Null(store.Settings.RepositoryPath);
        Assert.False(viewModel.ResetRepositoryPathCommand.CanExecute(null));
    }

    /// <summary>Clears the Skyrim install path override back to auto-detection and saves immediately.</summary>
    [Fact]
    public void ResetSkyrimInstallPathCommandClearsTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(SkyrimInstallPath: @"D:\Skyrim") };
        var viewModel = new SettingsPageViewModel(store);

        viewModel.ResetSkyrimInstallPathCommand.Execute(null);

        Assert.Null(viewModel.SkyrimInstallPath);
        Assert.Null(store.Settings.SkyrimInstallPath);
    }

    /// <summary>Clears the output path override back to auto-detection and saves immediately.</summary>
    [Fact]
    public void ResetOutputPathCommandClearsTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\out") };
        var viewModel = new SettingsPageViewModel(store);

        viewModel.ResetOutputPathCommand.Execute(null);

        Assert.Null(viewModel.OutputPath);
        Assert.Null(store.Settings.OutputPath);
    }

    /// <summary>Disables each reset command until its path has an override to clear.</summary>
    [Fact]
    public void ResetCommandsAreDisabledWithoutAnOverride()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore());

        Assert.False(viewModel.ResetRepositoryPathCommand.CanExecute(null));
        Assert.False(viewModel.ResetSkyrimInstallPathCommand.CanExecute(null));
        Assert.False(viewModel.ResetOutputPathCommand.CanExecute(null));

        viewModel.RepositoryPath = @"D:\repo";

        Assert.True(viewModel.ResetRepositoryPathCommand.CanExecute(null));
    }

    /// <summary>Saves the updated settings immediately when the open-output-folder toggle changes.</summary>
    [Fact]
    public void SettingOpenOutputFolderAfterSuccessfulBuildSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store);

        viewModel.OpenOutputFolderAfterSuccessfulBuild = false;

        Assert.False(store.Settings.OpenOutputFolderAfterSuccessfulBuild);
    }

    /// <summary>Saves the updated settings immediately when the auto-scroll-logs toggle changes.</summary>
    [Fact]
    public void SettingAutoScrollLogsSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store);

        viewModel.AutoScrollLogs = false;

        Assert.False(store.Settings.AutoScrollLogs);
    }

    /// <summary>Saves the updated settings immediately when the verbose-command-output toggle changes.</summary>
    [Fact]
    public void SettingVerboseCommandOutputSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store);

        viewModel.VerboseCommandOutput = true;

        Assert.True(store.Settings.VerboseCommandOutput);
    }

    /// <summary>Saves the updated settings immediately when the notify-when-build-completes toggle changes.</summary>
    [Fact]
    public void SettingNotifyWhenBuildCompletesSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store);

        viewModel.NotifyWhenBuildCompletes = true;

        Assert.True(store.Settings.NotifyWhenBuildCompletes);
    }

    /// <summary>An in-memory <see cref="ISettingsStore"/>, avoiding real disk I/O for tests over Builder settings.</summary>
    private sealed class FakeSettingsStore : ISettingsStore
    {
        /// <summary>Gets or sets the currently persisted settings; defaults to <see cref="BuilderSettings"/>'s own defaults.</summary>
        public BuilderSettings Settings { get; set; } = new();

        /// <inheritdoc/>
        public BuilderSettings Load() => Settings;

        /// <inheritdoc/>
        public void Save(BuilderSettings settings) => Settings = settings;
    }
}
