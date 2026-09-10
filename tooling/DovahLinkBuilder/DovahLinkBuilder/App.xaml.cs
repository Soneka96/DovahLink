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
        // Attempted unconditionally, even when an override is already configured, so Settings has a
        // real auto-detected fallback to display and reset to whenever one actually exists. Unlike
        // RepositoryRootLocator.Find, this never throws: a portable or published Builder run outside
        // any real checkout must still start when a configured override is all it needs.
        string? discoveredRepositoryRoot = TryFindRepositoryRoot(AppContext.BaseDirectory);
        (string activeRepositoryRoot, string autoDetectedRepositoryRoot) = ResolveRepositoryRoots(settings.RepositoryPath, discoveredRepositoryRoot);
        var repositoryContext = new RepositoryContext(activeRepositoryRoot);
        var outputPathContext = new OutputPathContext(settings.OutputPath);
        ICommandRunner commandRunner = new ProcessCommandRunner();
        var preflightService = new PreflightService(commandRunner);
        var gitStatusService = new GitStatusService(commandRunner);
        var gitStatusStore = new GitStatusStore(gitStatusService, repositoryContext);
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, outputPathContext);
        var outputOwnershipGuard = new BuildOutputOwnershipGuard();
        var buildCoordinator = new AdapterHostBuildCoordinator(
            commandRunner, VisualStudioToolchainLocator.Find, PapyrusToolchainLocator.Find, outputOwnershipGuard);
        var buildHistoryStore = new BuildHistoryStore(appDataDirectory);
        var logViewModel = new LogViewModel();

        var buildPage = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            buildCoordinator,
            buildHistoryStore,
            settingsStore,
            OpenFolderInExplorer,
            Clipboard.SetText,
            repositoryContext,
            outputPathContext,
            outputOwnershipGuard,
            logViewModel);
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

    /// <summary>Locates the DovahLink repository root, reporting <see langword="null"/> instead of throwing when it cannot be found.</summary>
    /// <param name="startPath">The path from which to begin the search.</param>
    /// <returns>The located repository root, or <see langword="null"/> when none was found.</returns>
    internal static string? TryFindRepositoryRoot(string startPath)
    {
        try
        {
            return RepositoryRootLocator.Find(startPath);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the repository root actually used at startup, and the fallback Settings displays and
    /// resets to. A configured override is used whenever one exists, even if real discovery failed --
    /// the Builder must still start on a valid override alone. When an override exists but discovery
    /// failed, the override doubles as the auto-detected fallback too, since no other value is
    /// available; Settings' Reset affordance then becomes a no-op rather than resetting to a location
    /// this Builder was never able to verify.
    /// </summary>
    /// <param name="configuredOverride">The persisted repository path override, or <see langword="null"/> when none is configured.</param>
    /// <param name="discoveredRoot">The result of <see cref="TryFindRepositoryRoot"/>, or <see langword="null"/> when discovery failed.</param>
    /// <returns>The active repository root, and the fallback Settings displays and resets to.</returns>
    /// <exception cref="InvalidOperationException">Thrown when neither a configured override nor a discovered root is available.</exception>
    internal static (string ActiveRepositoryRoot, string AutoDetectedRepositoryRoot) ResolveRepositoryRoots(string? configuredOverride, string? discoveredRoot)
    {
        string activeRepositoryRoot = configuredOverride ?? discoveredRoot
            ?? throw new InvalidOperationException(
                "Could not find the DovahLink repository, and no repository override is configured. " +
                "Keep this builder inside the repository or its tooling output folder, or configure a " +
                "repository path override on the Settings page.");
        return (activeRepositoryRoot, discoveredRoot ?? activeRepositoryRoot);
    }
}
