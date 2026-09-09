using System.Windows;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
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
        ICommandRunner commandRunner = new ProcessCommandRunner();
        var preflightService = new PreflightService(commandRunner);
        var gitStatusService = new GitStatusService(commandRunner);
        var buildCoordinator = new AdapterHostBuildCoordinator(
            commandRunner, VisualStudioToolchainLocator.Find, PapyrusToolchainLocator.Find);

        var buildPage = new BuildPageViewModel(preflightService, gitStatusService, buildCoordinator, repositoryRoot);
        var mainWindowViewModel = new MainWindowViewModel(buildPage, new EnvironmentPageViewModel(), new SettingsPageViewModel());
        new MainWindow(mainWindowViewModel).Show();

        _ = buildPage.InitializeAsync();
    }
}
