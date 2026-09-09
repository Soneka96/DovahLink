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
        // Resolved unconditionally, even when an override is already configured, so Settings always
        // has a real auto-detected fallback to display and reset to (RepositoryContext.SetRepositoryRoot
        // below then adopts the persisted override, if any, as the actually active root).
        string autoDetectedRepositoryRoot = RepositoryRootLocator.Find(AppContext.BaseDirectory);
        var repositoryContext = new RepositoryContext(settings.RepositoryPath ?? autoDetectedRepositoryRoot);
        var outputPathContext = new OutputPathContext(settings.OutputPath);
        ICommandRunner commandRunner = new ProcessCommandRunner();
        var preflightService = new PreflightService(commandRunner);
        var gitStatusService = new GitStatusService(commandRunner);
        var gitStatusStore = new GitStatusStore(gitStatusService, repositoryContext);
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, outputPathContext);
        var buildCoordinator = new AdapterHostBuildCoordinator(
            commandRunner, VisualStudioToolchainLocator.Find, PapyrusToolchainLocator.Find);
        var buildHistoryStore = new BuildHistoryStore(appDataDirectory);

        var buildPage = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            buildCoordinator,
            buildHistoryStore,
            settingsStore,
            OpenFolderInExplorer,
            Clipboard.SetText,
            repositoryContext,
            outputPathContext);
        var environmentPage = new EnvironmentPageViewModel(environmentStore, gitStatusStore);
        var settingsPage = new SettingsPageViewModel(settingsStore, new FolderPickerService(), OpenFolderInExplorer, autoDetectedRepositoryRoot, repositoryContext, outputPathContext);
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

        // One shared refresh for both pages, rather than each independently triggering its own
        // preflight-and-git-status check: EnvironmentStore.RefreshAsync's PropertyChanged notifies
        // both buildPage and environmentPage, which already subscribed to it in their own constructors.
        _ = environmentStore.RefreshAsync();
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
