using System.IO;
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

        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

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
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.RepositoryPath = @"D:\repo";

        Assert.Equal(@"D:\repo", store.Settings.RepositoryPath);
    }

    /// <summary>Swallows a local persistence failure rather than letting it escape a property setter as an unhandled exception.</summary>
    [Fact]
    public void SettingAFieldSwallowsAPersistenceFailure()
    {
        var store = new FakeSettingsStore { ThrownExceptionOnSave = new IOException("disk full") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Exception? thrown = Record.Exception(() => viewModel.RepositoryPath = @"D:\repo");

        Assert.Null(thrown);
        Assert.Equal(@"D:\repo", viewModel.RepositoryPath);
    }

    /// <summary>
    /// Updates the shared repository context even when persisting the change fails, so every other
    /// consumer stays coherent with what this page now displays regardless of a local disk problem.
    /// </summary>
    [Fact]
    public void SettingRepositoryPathUpdatesTheSharedRepositoryContextEvenWhenSavingFails()
    {
        var store = new FakeSettingsStore { ThrownExceptionOnSave = new IOException("disk full") };
        var repositoryContext = new RepositoryContext(@"D:\resolved-repo");
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", repositoryContext, new OutputPathContext(null));

        viewModel.RepositoryPath = @"D:\repo";

        Assert.Equal(@"D:\repo", repositoryContext.RepositoryRoot);
    }

    /// <summary>Seeds the shared repository context with a persisted override on construction, not just the auto-detected root.</summary>
    [Fact]
    public void ConstructorSeedsTheSharedRepositoryContextFromAPersistedOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\override") };
        var repositoryContext = new RepositoryContext(@"D:\resolved-repo");

        _ = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", repositoryContext, new OutputPathContext(null));

        Assert.Equal(@"D:\override", repositoryContext.RepositoryRoot);
    }

    /// <summary>Updates the shared repository context immediately when the repository path override changes.</summary>
    [Fact]
    public void SettingRepositoryPathUpdatesTheSharedRepositoryContext()
    {
        var repositoryContext = new RepositoryContext(@"D:\resolved-repo");
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", repositoryContext, new OutputPathContext(null));

        viewModel.RepositoryPath = @"D:\repo";

        Assert.Equal(@"D:\repo", repositoryContext.RepositoryRoot);
    }

    /// <summary>Reverts the shared repository context back to the auto-detected root once the override is reset.</summary>
    [Fact]
    public void ResettingTheRepositoryPathRevertsTheSharedRepositoryContextToTheAutoDetectedRoot()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\override") };
        var repositoryContext = new RepositoryContext(@"D:\resolved-repo");
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", repositoryContext, new OutputPathContext(null));
        Assert.Equal(@"D:\override", repositoryContext.RepositoryRoot);

        viewModel.ResetRepositoryPathCommand.Execute(null);

        Assert.Equal(@"D:\resolved-repo", repositoryContext.RepositoryRoot);
    }

    /// <summary>Preserves fields this page does not itself track (the main window's saved geometry) across a save it does trigger.</summary>
    [Fact]
    public void SavingASettingsPageFieldPreservesUntrackedFields()
    {
        var store = new FakeSettingsStore
        {
            Settings = new BuilderSettings(WindowLeft: 120, WindowTop: 80, WindowWidth: 1024, WindowHeight: 768, WindowIsMaximized: true),
        };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.RepositoryPath = @"D:\repo";

        Assert.Equal(@"D:\repo", store.Settings.RepositoryPath);
        Assert.Equal(120, store.Settings.WindowLeft);
        Assert.Equal(80, store.Settings.WindowTop);
        Assert.Equal(1024, store.Settings.WindowWidth);
        Assert.Equal(768, store.Settings.WindowHeight);
        Assert.True(store.Settings.WindowIsMaximized);
    }

    /// <summary>Preserves fields this page does not itself track across a reset command's save, the same as an ordinary field change.</summary>
    [Fact]
    public void ResettingASettingsPageFieldPreservesUntrackedFields()
    {
        var store = new FakeSettingsStore
        {
            Settings = new BuilderSettings(RepositoryPath: @"D:\repo", WindowLeft: 120, WindowTop: 80, WindowWidth: 1024, WindowHeight: 768, WindowIsMaximized: true),
        };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.ResetRepositoryPathCommand.Execute(null);

        Assert.Null(store.Settings.RepositoryPath);
        Assert.Equal(120, store.Settings.WindowLeft);
        Assert.Equal(80, store.Settings.WindowTop);
        Assert.Equal(1024, store.Settings.WindowWidth);
        Assert.Equal(768, store.Settings.WindowHeight);
        Assert.True(store.Settings.WindowIsMaximized);
    }

    /// <summary>Saves the updated settings immediately when the Skyrim install path changes.</summary>
    [Fact]
    public void SettingSkyrimInstallPathSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.SkyrimInstallPath = @"D:\Skyrim";

        Assert.Equal(@"D:\Skyrim", store.Settings.SkyrimInstallPath);
    }

    /// <summary>Saves the updated settings immediately when the output path changes.</summary>
    [Fact]
    public void SettingOutputPathSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.OutputPath = @"D:\out";

        Assert.Equal(@"D:\out", store.Settings.OutputPath);
    }

    /// <summary>Updates the shared output path context immediately when the output path override changes.</summary>
    [Fact]
    public void SettingOutputPathUpdatesTheSharedOutputPathContext()
    {
        var outputPathContext = new OutputPathContext(null);
        var viewModel = new SettingsPageViewModel(
            new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), outputPathContext);

        viewModel.OutputPath = @"D:\out";

        Assert.Equal(@"D:\out", outputPathContext.OutputPath);
    }

    /// <summary>
    /// Updates the shared output path context even when persisting the change fails, so every other
    /// consumer stays coherent with what this page now displays regardless of a local disk problem.
    /// </summary>
    [Fact]
    public void SettingOutputPathUpdatesTheSharedOutputPathContextEvenWhenSavingFails()
    {
        var store = new FakeSettingsStore { ThrownExceptionOnSave = new IOException("disk full") };
        var outputPathContext = new OutputPathContext(null);
        var viewModel = new SettingsPageViewModel(
            store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), outputPathContext);

        viewModel.OutputPath = @"D:\out";

        Assert.Equal(@"D:\out", outputPathContext.OutputPath);
    }

    /// <summary>Seeds the shared output path context with a persisted override on construction.</summary>
    [Fact]
    public void ConstructorSeedsTheSharedOutputPathContextFromAPersistedOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\persisted-out") };
        var outputPathContext = new OutputPathContext(null);

        _ = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), outputPathContext);

        Assert.Equal(@"D:\persisted-out", outputPathContext.OutputPath);
    }

    /// <summary>Reverts the shared output path context back to null once the override is reset.</summary>
    [Fact]
    public void ResettingTheOutputPathRevertsTheSharedOutputPathContextToNull()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\out") };
        var outputPathContext = new OutputPathContext(null);
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), outputPathContext);
        Assert.Equal(@"D:\out", outputPathContext.OutputPath);

        viewModel.ResetOutputPathCommand.Execute(null);

        Assert.Null(outputPathContext.OutputPath);
    }

    /// <summary>Clears the repository path override back to auto-detection and saves immediately.</summary>
    [Fact]
    public void ResetRepositoryPathCommandClearsTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\repo") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.ResetRepositoryPathCommand.Execute(null);

        Assert.Null(viewModel.RepositoryPath);
        Assert.Null(store.Settings.RepositoryPath);
        Assert.False(viewModel.ResetRepositoryPathCommand.CanExecute(null));
    }

    /// <summary>Clears the Skyrim install path back to unset, saves immediately, and disables Open again.</summary>
    [Fact]
    public void ResetSkyrimInstallPathCommandClearsTheValue()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(SkyrimInstallPath: @"D:\Skyrim") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.ResetSkyrimInstallPathCommand.Execute(null);

        Assert.Null(viewModel.SkyrimInstallPath);
        Assert.Null(store.Settings.SkyrimInstallPath);
        Assert.False(viewModel.OpenSkyrimInstallFolderCommand.CanExecute(null));
    }

    /// <summary>Clears the output path override back to auto-detection and saves immediately.</summary>
    [Fact]
    public void ResetOutputPathCommandClearsTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\out") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.ResetOutputPathCommand.Execute(null);

        Assert.Null(viewModel.OutputPath);
        Assert.Null(store.Settings.OutputPath);
    }

    /// <summary>Disables each reset command until its path has an override to clear.</summary>
    [Fact]
    public void ResetCommandsAreDisabledWithoutAnOverride()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.False(viewModel.ResetRepositoryPathCommand.CanExecute(null));
        Assert.False(viewModel.ResetSkyrimInstallPathCommand.CanExecute(null));
        Assert.False(viewModel.ResetOutputPathCommand.CanExecute(null));

        viewModel.RepositoryPath = @"D:\repo";

        Assert.True(viewModel.ResetRepositoryPathCommand.CanExecute(null));
    }

    /// <summary>Disables Reset when the override already matches the resolved repository root, since resetting would not change anything.</summary>
    [Fact]
    public void ResetRepositoryPathCommandIsDisabledWhenTheOverrideMatchesTheResolvedRoot()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\resolved-repo") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.False(viewModel.ResetRepositoryPathCommand.CanExecute(null));
    }

    /// <summary>Treats a case difference and a trailing separator as equal, matching how Windows itself compares paths.</summary>
    [Fact]
    public void ResetRepositoryPathCommandIsDisabledForACaseOrTrailingSeparatorDifferenceOnly()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\RESOLVED-REPO\") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.False(viewModel.ResetRepositoryPathCommand.CanExecute(null));
    }

    /// <summary>Stays enabled when the override genuinely differs from the resolved repository root.</summary>
    [Fact]
    public void ResetRepositoryPathCommandIsEnabledWhenTheOverrideDiffersFromTheResolvedRoot()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\other-repo") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.True(viewModel.ResetRepositoryPathCommand.CanExecute(null));
    }

    /// <summary>Disables Reset when the override already matches the Release profile's default output root.</summary>
    [Fact]
    public void ResetOutputPathCommandIsDisabledWhenTheOverrideMatchesTheDefault()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: BuildProfile.Release.ToOutputRoot(@"D:\resolved-repo")) };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.False(viewModel.ResetOutputPathCommand.CanExecute(null));
    }

    /// <summary>Stays enabled when the override genuinely differs from the default output root.</summary>
    [Fact]
    public void ResetOutputPathCommandIsEnabledWhenTheOverrideDiffersFromTheDefault()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\custom-out") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.True(viewModel.ResetOutputPathCommand.CanExecute(null));
    }

    /// <summary>Saves the updated settings immediately when the open-output-folder toggle changes.</summary>
    [Fact]
    public void SettingOpenOutputFolderAfterSuccessfulBuildSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.OpenOutputFolderAfterSuccessfulBuild = false;

        Assert.False(store.Settings.OpenOutputFolderAfterSuccessfulBuild);
    }

    /// <summary>Saves the updated settings immediately when the auto-scroll-logs toggle changes.</summary>
    [Fact]
    public void SettingAutoScrollLogsSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.AutoScrollLogs = false;

        Assert.False(store.Settings.AutoScrollLogs);
    }

    /// <summary>Saves the updated settings immediately when the verbose-command-output toggle changes.</summary>
    [Fact]
    public void SettingVerboseCommandOutputSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.VerboseCommandOutput = true;

        Assert.True(store.Settings.VerboseCommandOutput);
    }

    /// <summary>Saves the updated settings immediately when the notify-when-build-completes toggle changes.</summary>
    [Fact]
    public void SettingNotifyWhenBuildCompletesSavesImmediately()
    {
        var store = new FakeSettingsStore();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.NotifyWhenBuildCompletes = true;

        Assert.True(store.Settings.NotifyWhenBuildCompletes);
    }

    /// <summary>Falls back to the resolved repository root when there is no override.</summary>
    [Fact]
    public void EffectiveRepositoryPathFallsBackToTheResolvedRootWithoutAnOverride()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.Equal(@"D:\resolved-repo", viewModel.EffectiveRepositoryPath);
    }

    /// <summary>Prefers the override over the resolved repository root once one is set.</summary>
    [Fact]
    public void EffectiveRepositoryPathPrefersTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(RepositoryPath: @"D:\override") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

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
        var viewModel = new SettingsPageViewModel(store, picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

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
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.BrowseRepositoryPathCommand.Execute(null);

        Assert.Equal(repositoryRoot, viewModel.RepositoryPath);
    }

    /// <summary>Reports an error and leaves the override unset when the picked folder is not a DovahLink repository.</summary>
    [Fact]
    public void BrowseRepositoryPathCommandReportsAnErrorForAnInvalidFolder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var picker = new FakeFolderPicker { NextPick = temporaryDirectory.Path };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.BrowseRepositoryPathCommand.Execute(null);

        Assert.Null(viewModel.RepositoryPath);
        Assert.NotNull(viewModel.RepositoryPathError);
    }

    /// <summary>Does nothing when the folder picker is cancelled.</summary>
    [Fact]
    public void BrowseRepositoryPathCommandDoesNothingWhenCancelled()
    {
        var picker = new FakeFolderPicker { NextPick = null };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

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
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));
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
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.OpenRepositoryFolderCommand.Execute(null);

        Assert.Equal([@"D:\override"], openedPaths);
    }

    /// <summary>Opens the resolved repository root when there is no override.</summary>
    [Fact]
    public void OpenRepositoryFolderCommandOpensTheResolvedRootWithoutAnOverride()
    {
        var openedPaths = new List<string>();
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.OpenRepositoryFolderCommand.Execute(null);

        Assert.Equal([@"D:\resolved-repo"], openedPaths);
    }

    /// <summary>Swallows a failure from opening the folder, since it is a convenience action and must not affect any reported state.</summary>
    [Fact]
    public void OpenRepositoryFolderCommandSwallowsAFailureFromOpenFolder()
    {
        var viewModel = new SettingsPageViewModel(
            new FakeSettingsStore(), new FakeFolderPicker(), _ => throw new InvalidOperationException("boom"), @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Exception? thrown = Record.Exception(() => viewModel.OpenRepositoryFolderCommand.Execute(null));

        Assert.Null(thrown);
    }

    /// <summary>Falls back to the Release profile's default output root when there is no override.</summary>
    [Fact]
    public void EffectiveOutputPathFallsBackToTheReleaseDefaultWithoutAnOverride()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.Equal(BuildProfile.Release.ToOutputRoot(@"D:\resolved-repo"), viewModel.EffectiveOutputPath);
    }

    /// <summary>Prefers the override over the default output root once one is set.</summary>
    [Fact]
    public void EffectiveOutputPathPrefersTheOverride()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\custom-out") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.Equal(@"D:\custom-out", viewModel.EffectiveOutputPath);
    }

    /// <summary>
    /// Tracks the currently active repository once a repository path override is set, not the
    /// auto-detected repository the override just replaced -- otherwise this page would display and
    /// open a different repository's output folder than the one a build actually targets.
    /// </summary>
    [Fact]
    public void EffectiveOutputPathTracksTheCurrentRepositoryOnceAnOverrideIsSet()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\auto-detected-repo", new RepositoryContext(@"D:\auto-detected-repo"), new OutputPathContext(null));

        viewModel.RepositoryPath = @"D:\override-repo";

        Assert.Equal(BuildProfile.Release.ToOutputRoot(@"D:\override-repo"), viewModel.EffectiveOutputPath);
    }

    /// <summary>
    /// Compares against the currently active repository's default, not the auto-detected repository a
    /// override just replaced, when deciding whether Reset would actually change anything.
    /// </summary>
    [Fact]
    public void ResetOutputPathCommandComparesAgainstTheCurrentRepositoryOnceAnOverrideIsSet()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: BuildProfile.Release.ToOutputRoot(@"D:\override-repo")) };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\auto-detected-repo", new RepositoryContext(@"D:\auto-detected-repo"), new OutputPathContext(null));

        viewModel.RepositoryPath = @"D:\override-repo";

        Assert.False(viewModel.ResetOutputPathCommand.CanExecute(null));
    }

    /// <summary>Sets the output path override unconditionally to whatever folder the picker returns.</summary>
    [Fact]
    public void BrowseOutputPathCommandSetsTheOverride()
    {
        var store = new FakeSettingsStore();
        var picker = new FakeFolderPicker { NextPick = @"D:\custom-out" };
        var viewModel = new SettingsPageViewModel(store, picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.BrowseOutputPathCommand.Execute(null);

        Assert.Equal(@"D:\custom-out", viewModel.OutputPath);
        Assert.Equal(@"D:\custom-out", store.Settings.OutputPath);
    }

    /// <summary>Does nothing when the folder picker is cancelled.</summary>
    [Fact]
    public void BrowseOutputPathCommandDoesNothingWhenCancelled()
    {
        var picker = new FakeFolderPicker { NextPick = null };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.BrowseOutputPathCommand.Execute(null);

        Assert.Null(viewModel.OutputPath);
    }

    /// <summary>Opens the override when one is set.</summary>
    [Fact]
    public void OpenOutputFolderCommandOpensTheOverrideWhenSet()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: @"D:\custom-out") };
        var openedPaths = new List<string>();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.OpenOutputFolderCommand.Execute(null);

        Assert.Equal([@"D:\custom-out"], openedPaths);
    }

    /// <summary>Opens the Release profile's default output root when there is no override.</summary>
    [Fact]
    public void OpenOutputFolderCommandOpensTheDefaultWithoutAnOverride()
    {
        var openedPaths = new List<string>();
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.OpenOutputFolderCommand.Execute(null);

        Assert.Equal([BuildProfile.Release.ToOutputRoot(@"D:\resolved-repo")], openedPaths);
    }

    /// <summary>Shows "Not set" rather than a false claim of auto-detection, since none exists.</summary>
    [Fact]
    public void SkyrimInstallPathDisplayTextShowsNotSetWithoutAValue()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.Equal("Not set", viewModel.SkyrimInstallPathDisplayText);
    }

    /// <summary>Shows the configured value once one is set.</summary>
    [Fact]
    public void SkyrimInstallPathDisplayTextShowsTheValueWhenSet()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(SkyrimInstallPath: @"D:\Skyrim") };
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.Equal(@"D:\Skyrim", viewModel.SkyrimInstallPathDisplayText);
    }

    /// <summary>Sets the Skyrim install path unconditionally to whatever folder the picker returns.</summary>
    [Fact]
    public void BrowseSkyrimInstallPathCommandSetsTheValue()
    {
        var store = new FakeSettingsStore();
        var picker = new FakeFolderPicker { NextPick = @"D:\Skyrim" };
        var viewModel = new SettingsPageViewModel(store, picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.BrowseSkyrimInstallPathCommand.Execute(null);

        Assert.Equal(@"D:\Skyrim", viewModel.SkyrimInstallPath);
        Assert.Equal(@"D:\Skyrim", store.Settings.SkyrimInstallPath);
    }

    /// <summary>Does nothing when the folder picker is cancelled.</summary>
    [Fact]
    public void BrowseSkyrimInstallPathCommandDoesNothingWhenCancelled()
    {
        var picker = new FakeFolderPicker { NextPick = null };
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), picker, _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.BrowseSkyrimInstallPathCommand.Execute(null);

        Assert.Null(viewModel.SkyrimInstallPath);
    }

    /// <summary>Disables Open until a Skyrim install path is set, since there is no resolved fallback to open.</summary>
    [Fact]
    public void OpenSkyrimInstallFolderCommandIsDisabledUntilSet()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsStore(), new FakeFolderPicker(), _ => { }, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        Assert.False(viewModel.OpenSkyrimInstallFolderCommand.CanExecute(null));

        viewModel.SkyrimInstallPath = @"D:\Skyrim";

        Assert.True(viewModel.OpenSkyrimInstallFolderCommand.CanExecute(null));
    }

    /// <summary>Opens the configured Skyrim install path.</summary>
    [Fact]
    public void OpenSkyrimInstallFolderCommandOpensTheConfiguredPath()
    {
        var store = new FakeSettingsStore { Settings = new BuilderSettings(SkyrimInstallPath: @"D:\Skyrim") };
        var openedPaths = new List<string>();
        var viewModel = new SettingsPageViewModel(store, new FakeFolderPicker(), openedPaths.Add, @"D:\resolved-repo", new RepositoryContext(@"D:\resolved-repo"), new OutputPathContext(null));

        viewModel.OpenSkyrimInstallFolderCommand.Execute(null);

        Assert.Equal([@"D:\Skyrim"], openedPaths);
    }

    /// <summary>An in-memory <see cref="ISettingsStore"/>, avoiding real disk I/O for tests over Builder settings.</summary>
    private sealed class FakeSettingsStore : ISettingsStore
    {
        /// <summary>Gets or sets the currently persisted settings; defaults to <see cref="BuilderSettings"/>'s own defaults.</summary>
        public BuilderSettings Settings { get; set; } = new();

        /// <summary>Gets or sets the exception <see cref="Save"/> throws instead of persisting, or <see langword="null"/>.</summary>
        public Exception? ThrownExceptionOnSave { get; set; }

        /// <inheritdoc/>
        public BuilderSettings Load() => Settings;

        /// <inheritdoc/>
        public void Save(BuilderSettings settings)
        {
            if (ThrownExceptionOnSave is not null)
            {
                throw ThrownExceptionOnSave;
            }

            Settings = settings;
        }
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
