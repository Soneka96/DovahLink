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

        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

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
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.RepositoryPath = @"D:\repo";

        Assert.Equal(@"D:\repo", store.Settings.RepositoryPath);
    }

    /// <summary>Saves the updated settings immediately when the Skyrim install path changes.</summary>
    [Fact]
    public void SettingSkyrimInstallPathSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.SkyrimInstallPath = @"D:\Skyrim";

        Assert.Equal(@"D:\Skyrim", store.Settings.SkyrimInstallPath);
    }

    /// <summary>Saves the updated settings immediately when the output path changes.</summary>
    [Fact]
    public void SettingOutputPathSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.OutputPath = @"D:\out";

        Assert.Equal(@"D:\out", store.Settings.OutputPath);
    }

    /// <summary>Clears the repository path override back to auto-detection and saves immediately.</summary>
    [Fact]
    public void ResetRepositoryPathCommandClearsTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\repo") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

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
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.ResetSkyrimInstallPathCommand.Execute(null);

        Assert.Null(viewModel.SkyrimInstallPath);
        Assert.Null(store.Settings.SkyrimInstallPath);
    }

    /// <summary>Clears the output path override back to auto-detection and saves immediately.</summary>
    [Fact]
    public void ResetOutputPathCommandClearsTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\out") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.ResetOutputPathCommand.Execute(null);

        Assert.Null(viewModel.OutputPath);
        Assert.Null(store.Settings.OutputPath);
    }

    /// <summary>Disables each reset command until its path has an override to clear.</summary>
    [Fact]
    public void ResetCommandsAreDisabledWithoutAnOverride()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

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
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.OpenOutputFolderAfterSuccessfulBuild = false;

        Assert.False(store.Settings.OpenOutputFolderAfterSuccessfulBuild);
    }

    /// <summary>Saves the updated settings immediately when the auto-scroll-logs toggle changes.</summary>
    [Fact]
    public void SettingAutoScrollLogsSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.AutoScrollLogs = false;

        Assert.False(store.Settings.AutoScrollLogs);
    }

    /// <summary>Saves the updated settings immediately when the verbose-command-output toggle changes.</summary>
    [Fact]
    public void SettingVerboseCommandOutputSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.VerboseCommandOutput = true;

        Assert.True(store.Settings.VerboseCommandOutput);
    }

    /// <summary>Saves the updated settings immediately when the notify-when-build-completes toggle changes.</summary>
    [Fact]
    public void SettingNotifyWhenBuildCompletesSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        viewModel.NotifyWhenBuildCompletes = true;

        Assert.True(store.Settings.NotifyWhenBuildCompletes);
    }

    /// <summary>Falls back to the resolved repository root when there is no override.</summary>
    [Fact]
    public void EffectiveRepositoryPathFallsBackToTheResolvedRootWithoutAnOverride()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        Assert.Equal(@"D:\resolved-repo", viewModel.EffectiveRepositoryPath);
    }

    /// <summary>Prefers the override over the resolved repository root once one is set.</summary>
    [Fact]
    public void EffectiveRepositoryPathPrefersTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\override") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        Assert.Equal(@"D:\override", viewModel.EffectiveRepositoryPath);
    }

    /// <summary>Sets the repository path override when the picker returns a valid DovahLink repository.</summary>
    [Fact]
    public void BrowseRepositoryPathCommandSetsTheOverrideForAValidRepository()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");
        var store = new FakeSettingsStore();
        var picker = new FakeFolderPicker { NextPick = repositoryRoot };
        var viewModel = new SettingsPageViewModel(store, picker, _ => { }, @"D:\resolved-repo");

        viewModel.BrowseRepositoryPathCommand.Execute(null);

        Assert.Equal(repositoryRoot, viewModel.RepositoryPath);
        Assert.Equal(repositoryRoot, store.Settings.RepositoryPath);
        Assert.Null(viewModel.RepositoryPathError);
    }

    /// <summary>Resolves a picked folder inside the repository up to the repository's actual root.</summary>
    [Fact]
    public void BrowseRepositoryPathCommandResolvesAChildFolderToTheRepositoryRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        string childDirectory = Path.Combine(repositoryRoot, "tooling");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");
        var picker = new FakeFolderPicker { NextPick = childDirectory };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo");

        viewModel.BrowseRepositoryPathCommand.Execute(null);

        Assert.Equal(repositoryRoot, viewModel.RepositoryPath);
    }

    /// <summary>Reports an error and leaves the override unset when the picked folder is not a DovahLink repository.</summary>
    [Fact]
    public void BrowseRepositoryPathCommandReportsAnErrorForAnInvalidFolder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var picker = new FakeFolderPicker { NextPick = temporaryDirectory.Path };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo");

        viewModel.BrowseRepositoryPathCommand.Execute(null);

        Assert.Null(viewModel.RepositoryPath);
        Assert.NotNull(viewModel.RepositoryPathError);
    }

    /// <summary>Does nothing when the folder picker is cancelled.</summary>
    [Fact]
    public void BrowseRepositoryPathCommandDoesNothingWhenCancelled()
    {
        var picker = new FakeFolderPicker { NextPick = null };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo");

        viewModel.BrowseRepositoryPathCommand.Execute(null);

        Assert.Null(viewModel.RepositoryPath);
        Assert.Null(viewModel.RepositoryPathError);
    }

    /// <summary>Leaves a previously reported error in place rather than clearing it when a later Browse is cancelled.</summary>
    [Fact]
    public void BrowseRepositoryPathCommandCancellingLeavesAnExistingErrorUntouched()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var picker = new FakeFolderPicker { NextPick = temporaryDirectory.Path };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo");
        viewModel.BrowseRepositoryPathCommand.Execute(null);
        string? errorAfterInvalidPick = viewModel.RepositoryPathError;
        Assert.NotNull(errorAfterInvalidPick);

        picker.NextPick = null;
        viewModel.BrowseRepositoryPathCommand.Execute(null);

        Assert.Equal(errorAfterInvalidPick, viewModel.RepositoryPathError);
    }

    /// <summary>Opens the override when one is set.</summary>
    [Fact]
    public void OpenRepositoryFolderCommandOpensTheOverrideWhenSet()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\override") };
        var openedPaths = new List<string>();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo");

        viewModel.OpenRepositoryFolderCommand.Execute(null);

        Assert.Equal([@"D:\override"], openedPaths);
    }

    /// <summary>Opens the resolved repository root when there is no override.</summary>
    [Fact]
    public void OpenRepositoryFolderCommandOpensTheResolvedRootWithoutAnOverride()
    {
        var openedPaths = new List<string>();
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo");

        viewModel.OpenRepositoryFolderCommand.Execute(null);

        Assert.Equal([@"D:\resolved-repo"], openedPaths);
    }

    /// <summary>Swallows a failure from opening the folder, since it is a convenience action and must not affect any reported state.</summary>
    [Fact]
    public void OpenRepositoryFolderCommandSwallowsAFailureFromOpenFolder()
    {
        var viewModel = new SettingsPageViewModel(
            new FakeSettingsStore(), new FakeFolderPicker(), _ => throw new InvalidOperationException("boom"), @"D:\resolved-repo");

        Exception? thrown = Record.Exception(() => viewModel.OpenRepositoryFolderCommand.Execute(null));

        Assert.Null(thrown);
    }

    /// <summary>Falls back to the Release profile's default output root when there is no override.</summary>
    [Fact]
    public void EffectiveOutputPathFallsBackToTheReleaseDefaultWithoutAnOverride()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        Assert.Equal(BuildProfile.Release.ToOutputRoot(@"D:\resolved-repo"), viewModel.EffectiveOutputPath);
    }

    /// <summary>Prefers the override over the default output root once one is set.</summary>
    [Fact]
    public void EffectiveOutputPathPrefersTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\custom-out") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo");

        Assert.Equal(@"D:\custom-out", viewModel.EffectiveOutputPath);
    }

    /// <summary>Sets the output path override unconditionally to whatever folder the picker returns.</summary>
    [Fact]
    public void BrowseOutputPathCommandSetsTheOverride()
    {
        var store = new FakeSettingsStore();
        var picker = new FakeFolderPicker { NextPick = @"D:\custom-out" };
        var viewModel = new SettingsPageViewModel(store, picker, _ => { }, @"D:\resolved-repo");

        viewModel.BrowseOutputPathCommand.Execute(null);

        Assert.Equal(@"D:\custom-out", viewModel.OutputPath);
        Assert.Equal(@"D:\custom-out", store.Settings.OutputPath);
    }

    /// <summary>Does nothing when the folder picker is cancelled.</summary>
    [Fact]
    public void BrowseOutputPathCommandDoesNothingWhenCancelled()
    {
        var picker = new FakeFolderPicker { NextPick = null };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo");

        viewModel.BrowseOutputPathCommand.Execute(null);

        Assert.Null(viewModel.OutputPath);
    }

    /// <summary>Opens the override when one is set.</summary>
    [Fact]
    public void OpenOutputFolderCommandOpensTheOverrideWhenSet()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\custom-out") };
        var openedPaths = new List<string>();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo");

        viewModel.OpenOutputFolderCommand.Execute(null);

        Assert.Equal([@"D:\custom-out"], openedPaths);
    }

    /// <summary>Opens the Release profile's default output root when there is no override.</summary>
    [Fact]
    public void OpenOutputFolderCommandOpensTheDefaultWithoutAnOverride()
    {
        var openedPaths = new List<string>();
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo");

        viewModel.OpenOutputFolderCommand.Execute(null);

        Assert.Equal([BuildProfile.Release.ToOutputRoot(@"D:\resolved-repo")], openedPaths);
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

    /// <summary>A scripted <see cref="IFolderPickerService"/>, avoiding a real OS dialog in tests.</summary>
    private sealed class FakeFolderPicker : IFolderPickerService
    {
        /// <summary>Gets or sets the folder <see cref="PickFolder"/> returns, or <see langword="null"/> to simulate the user cancelling the dialog.</summary>
        public string? NextPick { get; set; }

        /// <inheritdoc/>
        public string? PickFolder(string title, string? initialDirectory) => NextPick;
    }
}
