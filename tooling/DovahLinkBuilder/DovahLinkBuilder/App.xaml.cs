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

        string appDataDirectory = GetAppDataDirectory();
        var settingsStore = new SettingsStore(appDataDirectory);
        BuilderSettings settings = settingsStore.Load();
        string repositoryRoot = settings.RepositoryPath ?? RepositoryRootLocator.Find(AppContext.BaseDirectory);
        ICommandRunner commandRunner = new ProcessCommandRunner();
        var preflightService = new PreflightService(commandRunner);
        var gitStatusService = new GitStatusService(commandRunner);
        var buildCoordinator = new AdapterHostBuildCoordinator(
            commandRunner, VisualStudioToolchainLocator.Find, PapyrusToolchainLocator.Find);
        var buildHistoryStore = new BuildHistoryStore(appDataDirectory);

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
        var settingsPage = new SettingsPageViewModel(settingsStore, new FolderPickerService(), OpenFolderInExplorer, repositoryRoot);
        var mainWindowViewModel = new MainWindowViewModel(buildPage, environmentPage, settingsPage);
        var mainWindow = new MainWindow(mainWindowViewModel, settingsStore);
        var virtualScreenBounds = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (WindowPlacement.Resolve(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight, virtualScreenBounds) is { } bounds)
        {
            mainWindow.Left = bounds.Left;
            mainWindow.Top = bounds.Top;
            mainWindow.Width = bounds.Width;
            mainWindow.Height = bounds.Height;
        }

        if (settings.WindowIsMaximized)
        {
            // Setting WindowState before the window has a native handle is unreliable and can be
            // silently ignored; SourceInitialized fires once that handle exists but before the first
            // paint, so the window opens maximized directly instead of flashing normal-sized first.
            mainWindow.SourceInitialized += (_, _) => mainWindow.WindowState = WindowState.Maximized;
        }

        mainWindow.Show();

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
