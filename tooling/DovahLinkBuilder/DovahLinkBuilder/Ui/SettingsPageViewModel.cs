using System.ComponentModel;
using System.IO;
using DovahLink.DovahLinkBuilder.Persistence;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the Settings page's state: paths -- set only through a folder-picker Browse command, never
/// typed, each with a command to clear it (back to auto-detection for Repository and Output, which
/// have a real resolved default; back to simply unset for Skyrim, which has none) and, where a
/// resolved value exists to open, an Open-folder command -- and the Builder's behavior toggles.
/// Every change saves immediately through <see cref="ISettingsStore"/>; there is no "remember last
/// profile" setting, since there is only one real profile.
/// </summary>
public interface ISettingsPageViewModel : INotifyPropertyChanged
{
    /// <summary>Gets or sets the repository path override, or <see langword="null"/> to use the auto-detected repository.</summary>
    string? RepositoryPath { get; set; }

    /// <summary>Gets or sets the Skyrim / Creation Kit install path, set only through a folder picker, or <see langword="null"/> when not configured.</summary>
    string? SkyrimInstallPath { get; set; }

    /// <summary>Gets or sets the build output path override, or <see langword="null"/> to use the repository's default <c>tooling/out</c>.</summary>
    string? OutputPath { get; set; }

    /// <summary>Gets or sets whether to open the output folder after a build succeeds. Never applies to a failed or cancelled build.</summary>
    bool OpenOutputFolderAfterSuccessfulBuild { get; set; }

    /// <summary>Gets or sets whether the log panel scrolls to the newest line automatically.</summary>
    bool AutoScrollLogs { get; set; }

    /// <summary>Gets or sets whether build and packaging commands report their full raw output rather than a condensed summary.</summary>
    bool VerboseCommandOutput { get; set; }

    /// <summary>Gets or sets whether to show a desktop notification when a build finishes.</summary>
    bool NotifyWhenBuildCompletes { get; set; }

    /// <summary>Gets the command that clears <see cref="RepositoryPath"/> back to auto-detection.</summary>
    RelayCommand ResetRepositoryPathCommand { get; }

    /// <summary>Gets the command that clears <see cref="SkyrimInstallPath"/> back to unset.</summary>
    RelayCommand ResetSkyrimInstallPathCommand { get; }

    /// <summary>Gets the command that clears <see cref="OutputPath"/> back to auto-detection.</summary>
    RelayCommand ResetOutputPathCommand { get; }

    /// <summary>Gets the repository path actually in effect: <see cref="RepositoryPath"/> when set, otherwise the auto-detected repository. Always has a value.</summary>
    string EffectiveRepositoryPath { get; }

    /// <summary>Gets the reason the last <see cref="BrowseRepositoryPathCommand"/> pick was rejected, or <see langword="null"/> when the current override (if any) is valid.</summary>
    string? RepositoryPathError { get; }

    /// <summary>Gets the command that opens a folder picker and, when the chosen folder is a valid DovahLink repository, sets it as <see cref="RepositoryPath"/>.</summary>
    RelayCommand BrowseRepositoryPathCommand { get; }

    /// <summary>Gets the command that opens <see cref="EffectiveRepositoryPath"/> in the system file explorer.</summary>
    RelayCommand OpenRepositoryFolderCommand { get; }

    /// <summary>
    /// Gets the build output path actually in effect: <see cref="OutputPath"/> when set, otherwise
    /// <see cref="BuildProfile.Release"/>'s default output root under the currently active repository
    /// (<see cref="EffectiveRepositoryPath"/>) -- the profile every override, once set, replaces
    /// regardless of which profile a build later targets. Always has a value.
    /// </summary>
    string EffectiveOutputPath { get; }

    /// <summary>Gets the command that opens a folder picker and sets the chosen folder as <see cref="OutputPath"/>. Any folder is accepted; nothing to validate against.</summary>
    RelayCommand BrowseOutputPathCommand { get; }

    /// <summary>Gets the command that opens <see cref="EffectiveOutputPath"/> in the system file explorer.</summary>
    RelayCommand OpenOutputFolderCommand { get; }

    /// <summary>Gets the command that opens a folder picker and sets the chosen folder as <see cref="SkyrimInstallPath"/>. Nothing in the Builder reads this value yet, so any folder is accepted.</summary>
    RelayCommand BrowseSkyrimInstallPathCommand { get; }

    /// <summary>Gets the command that opens <see cref="SkyrimInstallPath"/> in the system file explorer; disabled while it is unset, since there is no resolved fallback to open.</summary>
    RelayCommand OpenSkyrimInstallFolderCommand { get; }

    /// <summary>Gets <see cref="SkyrimInstallPath"/> for display, or "Not set" in its place -- never a false claim of auto-detection, since none exists.</summary>
    string SkyrimInstallPathDisplayText { get; }
}

/// <inheritdoc cref="ISettingsPageViewModel"/>
public sealed class SettingsPageViewModel : ObservableObject, ISettingsPageViewModel
{
    /// <summary>Persists the Builder's settings.</summary>
    private readonly ISettingsStore settingsStore;

    /// <summary>Prompts the user to pick a folder for a path override, for <see cref="BrowseRepositoryPathCommand"/> and its siblings.</summary>
    private readonly IFolderPickerService folderPicker;

    /// <summary>Opens a folder in the system file explorer, for <see cref="OpenRepositoryFolderCommand"/> and its siblings.</summary>
    private readonly Action<string> openFolder;

    /// <summary>The auto-detected repository root, used only when there is no override; the effective value <see cref="EffectiveRepositoryPath"/> falls back to.</summary>
    private readonly string autoDetectedRepositoryRoot;

    /// <summary>The shared repository root every consumer reads, kept in sync with <see cref="EffectiveRepositoryPath"/>.</summary>
    private readonly IRepositoryContext repositoryContext;

    /// <summary>The backing field for <see cref="RepositoryPath"/>.</summary>
    private string? repositoryPath;

    /// <summary>The backing field for <see cref="SkyrimInstallPath"/>.</summary>
    private string? skyrimInstallPath;

    /// <summary>The backing field for <see cref="OutputPath"/>.</summary>
    private string? outputPath;

    /// <summary>The backing field for <see cref="OpenOutputFolderAfterSuccessfulBuild"/>.</summary>
    private bool openOutputFolderAfterSuccessfulBuild;

    /// <summary>The backing field for <see cref="AutoScrollLogs"/>.</summary>
    private bool autoScrollLogs;

    /// <summary>The backing field for <see cref="VerboseCommandOutput"/>.</summary>
    private bool verboseCommandOutput;

    /// <summary>The backing field for <see cref="NotifyWhenBuildCompletes"/>.</summary>
    private bool notifyWhenBuildCompletes;

    /// <summary>The backing field for <see cref="RepositoryPathError"/>.</summary>
    private string? repositoryPathError;

    /// <summary>Initializes the page from the settings currently persisted in <paramref name="settingsStore"/>.</summary>
    /// <param name="settingsStore">Persists the Builder's settings.</param>
    /// <param name="folderPicker">Prompts the user to pick a folder for a path override.</param>
    /// <param name="openFolder">Opens a folder in the system file explorer.</param>
    /// <param name="autoDetectedRepositoryRoot">The auto-detected repository root, used only when there is no override.</param>
    /// <param name="repositoryContext">The shared repository root every consumer reads.</param>
    public SettingsPageViewModel(
        ISettingsStore settingsStore,
        IFolderPickerService folderPicker,
        Action<string> openFolder,
        string autoDetectedRepositoryRoot,
        IRepositoryContext repositoryContext)
    {
        this.settingsStore = settingsStore;
        this.folderPicker = folderPicker;
        this.openFolder = openFolder;
        this.autoDetectedRepositoryRoot = autoDetectedRepositoryRoot;
        this.repositoryContext = repositoryContext;
        BuilderSettings settings = settingsStore.Load();
        repositoryPath = settings.RepositoryPath;
        skyrimInstallPath = settings.SkyrimInstallPath;
        outputPath = settings.OutputPath;
        openOutputFolderAfterSuccessfulBuild = settings.OpenOutputFolderAfterSuccessfulBuild;
        autoScrollLogs = settings.AutoScrollLogs;
        verboseCommandOutput = settings.VerboseCommandOutput;
        notifyWhenBuildCompletes = settings.NotifyWhenBuildCompletes;
        ResetRepositoryPathCommand = new RelayCommand(
            () => RepositoryPath = null, () => RepositoryPath is { } path && !PathsAreEqual(path, autoDetectedRepositoryRoot));
        ResetSkyrimInstallPathCommand = new RelayCommand(() => SkyrimInstallPath = null, () => SkyrimInstallPath is not null);
        ResetOutputPathCommand = new RelayCommand(
            () => OutputPath = null, () => OutputPath is { } path && !PathsAreEqual(path, BuildProfile.Release.ToOutputRoot(EffectiveRepositoryPath)));
        BrowseRepositoryPathCommand = new RelayCommand(OnBrowseRepositoryPath);
        OpenRepositoryFolderCommand = new RelayCommand(() => OpenFolderSafely(EffectiveRepositoryPath));
        BrowseOutputPathCommand = new RelayCommand(OnBrowseOutputPath);
        OpenOutputFolderCommand = new RelayCommand(() => OpenFolderSafely(EffectiveOutputPath));
        BrowseSkyrimInstallPathCommand = new RelayCommand(OnBrowseSkyrimInstallPath);
        OpenSkyrimInstallFolderCommand = new RelayCommand(() => OpenFolderSafely(SkyrimInstallPath!), () => SkyrimInstallPath is not null);
        repositoryContext.SetRepositoryRoot(EffectiveRepositoryPath);
    }

    /// <inheritdoc/>
    public string? RepositoryPath
    {
        get => repositoryPath;
        set
        {
            if (SetProperty(ref repositoryPath, value))
            {
                OnPropertyChanged(nameof(EffectiveRepositoryPath));
                ResetRepositoryPathCommand.RaiseCanExecuteChanged();
                // Shares the new root with every other consumer before the best-effort persistence
                // below, which can fail: the in-memory repository this page now displays must never
                // disagree with the one everything else is already using, even if saving it for next
                // launch does not succeed.
                repositoryContext.SetRepositoryRoot(EffectiveRepositoryPath);
                Save();
            }
        }
    }

    /// <inheritdoc/>
    public string? SkyrimInstallPath
    {
        get => skyrimInstallPath;
        set
        {
            if (SetProperty(ref skyrimInstallPath, value))
            {
                OnPropertyChanged(nameof(SkyrimInstallPathDisplayText));
                ResetSkyrimInstallPathCommand.RaiseCanExecuteChanged();
                OpenSkyrimInstallFolderCommand.RaiseCanExecuteChanged();
                Save();
            }
        }
    }

    /// <inheritdoc/>
    public string? OutputPath
    {
        get => outputPath;
        set
        {
            if (SetProperty(ref outputPath, value))
            {
                OnPropertyChanged(nameof(EffectiveOutputPath));
                ResetOutputPathCommand.RaiseCanExecuteChanged();
                Save();
            }
        }
    }

    /// <inheritdoc/>
    public bool OpenOutputFolderAfterSuccessfulBuild
    {
        get => openOutputFolderAfterSuccessfulBuild;
        set
        {
            if (SetProperty(ref openOutputFolderAfterSuccessfulBuild, value))
            {
                Save();
            }
        }
    }

    /// <inheritdoc/>
    public bool AutoScrollLogs
    {
        get => autoScrollLogs;
        set
        {
            if (SetProperty(ref autoScrollLogs, value))
            {
                Save();
            }
        }
    }

    /// <inheritdoc/>
    public bool VerboseCommandOutput
    {
        get => verboseCommandOutput;
        set
        {
            if (SetProperty(ref verboseCommandOutput, value))
            {
                Save();
            }
        }
    }

    /// <inheritdoc/>
    public bool NotifyWhenBuildCompletes
    {
        get => notifyWhenBuildCompletes;
        set
        {
            if (SetProperty(ref notifyWhenBuildCompletes, value))
            {
                Save();
            }
        }
    }

    /// <inheritdoc/>
    public RelayCommand ResetRepositoryPathCommand { get; }

    /// <inheritdoc/>
    public RelayCommand ResetSkyrimInstallPathCommand { get; }

    /// <inheritdoc/>
    public RelayCommand ResetOutputPathCommand { get; }

    /// <summary>
    /// Persists the current settings, merged onto whatever is currently on disk rather than
    /// constructed fresh -- this page tracks only the fields above, so building a new
    /// <see cref="BuilderSettings"/> from just those would silently reset every field another page
    /// owns (for example the main window's saved geometry) back to its default on every save here.
    /// </summary>
    private void Save()
    {
        try
        {
            settingsStore.Save(settingsStore.Load() with
            {
                RepositoryPath = RepositoryPath,
                SkyrimInstallPath = SkyrimInstallPath,
                OutputPath = OutputPath,
                OpenOutputFolderAfterSuccessfulBuild = OpenOutputFolderAfterSuccessfulBuild,
                AutoScrollLogs = AutoScrollLogs,
                VerboseCommandOutput = VerboseCommandOutput,
                NotifyWhenBuildCompletes = NotifyWhenBuildCompletes,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Persisting a settings change for next launch is best-effort local bookkeeping; a
            // failure here must never surface as an unhandled exception from a property setter, or
            // prevent the in-memory state that change already applied from taking effect.
        }
    }

    /// <inheritdoc/>
    public string EffectiveRepositoryPath => RepositoryPath ?? autoDetectedRepositoryRoot;

    /// <inheritdoc/>
    public string? RepositoryPathError
    {
        get => repositoryPathError;
        private set => SetProperty(ref repositoryPathError, value);
    }

    /// <inheritdoc/>
    public RelayCommand BrowseRepositoryPathCommand { get; }

    /// <inheritdoc/>
    public RelayCommand OpenRepositoryFolderCommand { get; }

    /// <summary>
    /// Prompts for a folder and, when one is chosen, resolves it through <see cref="RepositoryRootLocator"/> so
    /// picking any folder inside the repository (not just its root) still sets the correct override; reports
    /// <see cref="RepositoryPathError"/> instead of saving when the chosen folder is not part of a DovahLink
    /// repository. Does nothing when the picker is cancelled.
    /// </summary>
    private void OnBrowseRepositoryPath()
    {
        string? picked = folderPicker.PickFolder("Select the DovahLink repository", EffectiveRepositoryPath);
        if (picked is null)
        {
            return;
        }

        try
        {
            RepositoryPath = RepositoryRootLocator.Find(picked);
            RepositoryPathError = null;
        }
        catch (InvalidOperationException exception)
        {
            RepositoryPathError = exception.Message;
        }
    }

    /// <summary>Opens <paramref name="folderPath"/> in the system file explorer. A failure here is a convenience-action failure and never changes any reported state.</summary>
    /// <param name="folderPath">The folder to open.</param>
    private void OpenFolderSafely(string folderPath)
    {
        try
        {
            openFolder(folderPath);
        }
        catch (Exception)
        {
            // Opening a folder is a convenience action; a failure here must not affect any reported state.
        }
    }

    /// <inheritdoc/>
    public string EffectiveOutputPath => OutputPath ?? BuildProfile.Release.ToOutputRoot(EffectiveRepositoryPath);

    /// <inheritdoc/>
    public RelayCommand BrowseOutputPathCommand { get; }

    /// <inheritdoc/>
    public RelayCommand OpenOutputFolderCommand { get; }

    /// <summary>Prompts for a folder and, when one is chosen, sets it as <see cref="OutputPath"/>. Does nothing when the picker is cancelled.</summary>
    private void OnBrowseOutputPath()
    {
        if (folderPicker.PickFolder("Select the build output folder", EffectiveOutputPath) is { } picked)
        {
            OutputPath = picked;
        }
    }

    /// <inheritdoc/>
    public RelayCommand BrowseSkyrimInstallPathCommand { get; }

    /// <inheritdoc/>
    public RelayCommand OpenSkyrimInstallFolderCommand { get; }

    /// <summary>Prompts for a folder and, when one is chosen, sets it as <see cref="SkyrimInstallPath"/>. Does nothing when the picker is cancelled.</summary>
    private void OnBrowseSkyrimInstallPath()
    {
        if (folderPicker.PickFolder("Select the Skyrim / Creation Kit install folder", SkyrimInstallPath) is { } picked)
        {
            SkyrimInstallPath = picked;
        }
    }

    /// <inheritdoc/>
    public string SkyrimInstallPathDisplayText => SkyrimInstallPath ?? "Not set";

    /// <summary>
    /// Compares two folder paths the way Windows itself does: case-insensitively, and ignoring a
    /// trailing separator difference between a user-picked folder and a computed default. Used to
    /// disable a Reset command when the current override already matches what it would reset to,
    /// since resetting it would not actually change anything.
    /// </summary>
    /// <param name="first">The first path to compare.</param>
    /// <param name="second">The second path to compare.</param>
    private static bool PathsAreEqual(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);
}
