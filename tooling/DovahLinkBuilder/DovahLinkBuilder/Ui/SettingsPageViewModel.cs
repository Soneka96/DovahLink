using DovahLink.DovahLinkBuilder.Persistence;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the Settings page's state: path overrides -- set only through a folder-picker Browse
/// command, never typed, with reset-to-auto-detect and (where a resolved value exists to open)
/// an Open-folder command -- and the Builder's behavior toggles. Every change saves immediately
/// through <see cref="ISettingsStore"/>; there is no "remember last profile" setting, since there
/// is only one real profile (correction #11).
/// </summary>
public sealed class SettingsPageViewModel : ObservableObject
{
    /// <summary>Persists the Builder's settings.</summary>
    private readonly ISettingsStore settingsStore;

    /// <summary>Prompts the user to pick a folder for a path override, for <see cref="BrowseRepositoryPathCommand"/> and its siblings.</summary>
    private readonly IFolderPickerService folderPicker;

    /// <summary>Opens a folder in the system file explorer, for <see cref="OpenRepositoryFolderCommand"/> and its siblings.</summary>
    private readonly Action<string> openFolder;

    /// <summary>The repository root resolved at startup (an override, or the auto-detected repository); the effective value <see cref="EffectiveRepositoryPath"/> falls back to.</summary>
    private readonly string repositoryRoot;

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
    /// <param name="repositoryRoot">The repository root resolved at startup (an override, or the auto-detected repository).</param>
    public SettingsPageViewModel(ISettingsStore settingsStore, IFolderPickerService folderPicker, Action<string> openFolder, string repositoryRoot)
    {
        this.settingsStore = settingsStore;
        this.folderPicker = folderPicker;
        this.openFolder = openFolder;
        this.repositoryRoot = repositoryRoot;
        BuilderSettings settings = settingsStore.Load();
        repositoryPath = settings.RepositoryPath;
        skyrimInstallPath = settings.SkyrimInstallPath;
        outputPath = settings.OutputPath;
        openOutputFolderAfterSuccessfulBuild = settings.OpenOutputFolderAfterSuccessfulBuild;
        autoScrollLogs = settings.AutoScrollLogs;
        verboseCommandOutput = settings.VerboseCommandOutput;
        notifyWhenBuildCompletes = settings.NotifyWhenBuildCompletes;
        ResetRepositoryPathCommand = new RelayCommand(() => RepositoryPath = null, () => RepositoryPath is not null);
        ResetSkyrimInstallPathCommand = new RelayCommand(() => SkyrimInstallPath = null, () => SkyrimInstallPath is not null);
        ResetOutputPathCommand = new RelayCommand(() => OutputPath = null, () => OutputPath is not null);
        BrowseRepositoryPathCommand = new RelayCommand(OnBrowseRepositoryPath);
        OpenRepositoryFolderCommand = new RelayCommand(() => OpenFolderSafely(EffectiveRepositoryPath));
        BrowseOutputPathCommand = new RelayCommand(OnBrowseOutputPath);
        OpenOutputFolderCommand = new RelayCommand(() => OpenFolderSafely(EffectiveOutputPath));
    }

    /// <summary>Gets or sets the repository path override, or <see langword="null"/> to use the auto-detected repository.</summary>
    public string? RepositoryPath
    {
        get => repositoryPath;
        set
        {
            if (SetProperty(ref repositoryPath, value))
            {
                OnPropertyChanged(nameof(EffectiveRepositoryPath));
                ResetRepositoryPathCommand.RaiseCanExecuteChanged();
                Save();
            }
        }
    }

    /// <summary>Gets or sets the Skyrim / Creation Kit install path override, or <see langword="null"/> to use auto-detection.</summary>
    public string? SkyrimInstallPath
    {
        get => skyrimInstallPath;
        set
        {
            if (SetProperty(ref skyrimInstallPath, value))
            {
                ResetSkyrimInstallPathCommand.RaiseCanExecuteChanged();
                Save();
            }
        }
    }

    /// <summary>Gets or sets the build output path override, or <see langword="null"/> to use the repository's default <c>tooling/out</c>.</summary>
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

    /// <summary>Gets or sets whether to open the output folder after a build succeeds. Never applies to a failed or cancelled build.</summary>
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

    /// <summary>Gets or sets whether the log panel scrolls to the newest line automatically.</summary>
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

    /// <summary>Gets or sets whether build and packaging commands report their full raw output rather than a condensed summary.</summary>
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

    /// <summary>Gets or sets whether to show a desktop notification when a build finishes.</summary>
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

    /// <summary>Gets the command that clears <see cref="RepositoryPath"/> back to auto-detection.</summary>
    public RelayCommand ResetRepositoryPathCommand { get; }

    /// <summary>Gets the command that clears <see cref="SkyrimInstallPath"/> back to auto-detection.</summary>
    public RelayCommand ResetSkyrimInstallPathCommand { get; }

    /// <summary>Gets the command that clears <see cref="OutputPath"/> back to auto-detection.</summary>
    public RelayCommand ResetOutputPathCommand { get; }

    /// <summary>Persists the current settings.</summary>
    private void Save()
    {
        settingsStore.Save(new BuilderSettings(
            RepositoryPath,
            SkyrimInstallPath,
            OutputPath,
            OpenOutputFolderAfterSuccessfulBuild,
            AutoScrollLogs,
            VerboseCommandOutput,
            NotifyWhenBuildCompletes));
    }

    /// <summary>Gets the repository path actually in effect: <see cref="RepositoryPath"/> when set, otherwise the resolved <see cref="repositoryRoot"/>. Always has a value.</summary>
    public string EffectiveRepositoryPath => RepositoryPath ?? repositoryRoot;

    /// <summary>Gets the reason the last <see cref="BrowseRepositoryPathCommand"/> pick was rejected, or <see langword="null"/> when the current override (if any) is valid.</summary>
    public string? RepositoryPathError
    {
        get => repositoryPathError;
        private set => SetProperty(ref repositoryPathError, value);
    }

    /// <summary>Gets the command that opens a folder picker and, when the chosen folder is a valid DovahLink repository, sets it as <see cref="RepositoryPath"/>.</summary>
    public RelayCommand BrowseRepositoryPathCommand { get; }

    /// <summary>Gets the command that opens <see cref="EffectiveRepositoryPath"/> in the system file explorer.</summary>
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

    /// <summary>
    /// Gets the build output path actually in effect: <see cref="OutputPath"/> when set, otherwise
    /// <see cref="BuildProfile.Release"/>'s default output root (the profile every override, once set,
    /// replaces regardless of which profile a build later targets). Always has a value.
    /// </summary>
    public string EffectiveOutputPath => OutputPath ?? BuildProfile.Release.ToOutputRoot(repositoryRoot);

    /// <summary>Gets the command that opens a folder picker and sets the chosen folder as <see cref="OutputPath"/>. Any folder is accepted; nothing to validate against.</summary>
    public RelayCommand BrowseOutputPathCommand { get; }

    /// <summary>Gets the command that opens <see cref="EffectiveOutputPath"/> in the system file explorer.</summary>
    public RelayCommand OpenOutputFolderCommand { get; }

    /// <summary>Prompts for a folder and, when one is chosen, sets it as <see cref="OutputPath"/>. Does nothing when the picker is cancelled.</summary>
    private void OnBrowseOutputPath()
    {
        if (folderPicker.PickFolder("Select the build output folder", EffectiveOutputPath) is { } picked)
        {
            OutputPath = picked;
        }
    }
}
