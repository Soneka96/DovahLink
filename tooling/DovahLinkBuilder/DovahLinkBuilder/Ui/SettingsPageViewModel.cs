using DovahLink.DovahLinkBuilder.Persistence;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the Settings page's state: path overrides with reset-to-auto-detect, and the Builder's
/// behavior toggles. Every change saves immediately through <see cref="ISettingsStore"/>; there is
/// no "remember last profile" setting, since there is only one real profile (correction #11).
/// </summary>
public sealed class SettingsPageViewModel : ObservableObject
{
    /// <summary>Persists the Builder's settings.</summary>
    private readonly ISettingsStore settingsStore;

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

    /// <summary>Initializes the page from the settings currently persisted in <paramref name="settingsStore"/>.</summary>
    /// <param name="settingsStore">Persists the Builder's settings.</param>
    public SettingsPageViewModel(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
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
    }

    /// <summary>Gets or sets the repository path override, or <see langword="null"/> to use the auto-detected repository.</summary>
    public string? RepositoryPath
    {
        get => repositoryPath;
        set
        {
            if (SetProperty(ref repositoryPath, value))
            {
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
}
