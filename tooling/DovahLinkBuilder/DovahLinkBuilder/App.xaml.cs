using System.Diagnostics;
using System.IO;
using System.Windows;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Persistence;
using DovahLink.DovahLinkBuilder.Preflight;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder;

/// <summary>The DovahLink Builder WPF application; composes and shows the main window.</summary>
public partial class App : Application
{
    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string repositoryRoot = RepositoryRootLocator.Find(AppContext.BaseDirectory);
        string appDataDirectory = GetAppDataDirectory();
        ICommandRunner commandRunner = new ProcessCommandRunner();
        var preflightService = new PreflightService(commandRunner);
        var gitStatusService = new GitStatusService(commandRunner);
        var buildCoordinator = new AdapterHostBuildCoordinator(
            commandRunner, VisualStudioToolchainLocator.Find, PapyrusToolchainLocator.Find);
        var buildHistoryStore = new BuildHistoryStore(appDataDirectory);
        var settingsStore = new SettingsStore(appDataDirectory);

        var buildPage = new BuildPageViewModel(
            preflightService,
            gitStatusService,
            buildCoordinator,
            buildHistoryStore,
            settingsStore,
            OpenFolderInExplorer,
            Clipboard.SetText,
            repositoryRoot);
        var environmentPage = new EnvironmentPageViewModel(preflightService, gitStatusService, repositoryRoot);
        var settingsPage = new SettingsPageViewModel(settingsStore);
        var mainWindowViewModel = new MainWindowViewModel(buildPage, environmentPage, settingsPage);
        new MainWindow(mainWindowViewModel).Show();

        _ = buildPage.InitializeAsync();
        _ = environmentPage.InitializeAsync();
    }

    /// <summary>Gets the local application-data directory the Builder persists its settings and build history under.</summary>
    private static string GetAppDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DovahLinkBuilder");

    /// <summary>Opens <paramref name="folderPath"/> in the system file explorer.</summary>
    /// <param name="folderPath">The folder to open.</param>
    private static void OpenFolderInExplorer(string folderPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { folderPath },
            UseShellExecute = false,
        });
    }
}
