using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Persistence;
using DovahLink.DovahLinkBuilder.Preflight;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies <see cref="MainWindowViewModel"/>'s navigation behavior.</summary>
public sealed class MainWindowViewModelTests
{
    /// <summary>
    /// The repository root shared by every stub page ViewModel <see cref="BuildViewModel"/>
    /// constructs. An instance field, not static: xUnit constructs a fresh test class instance per
    /// test method, so this stays isolated between tests the same way a fresh local would.
    /// </summary>
    private readonly RepositoryContext repositoryContext = new(@"C:\repo");

    /// <summary>Builds a <see cref="MainWindowViewModel"/> over stub page ViewModels, since these tests exercise navigation only.</summary>
    private MainWindowViewModel BuildViewModel() =>
        new(BuildStubBuildPage(), BuildStubEnvironmentPage(),
            new SettingsPageViewModel(new StubSettingsStore(), new StubFolderPickerService(), _ => { }, @"C:\repo", repositoryContext));

    /// <summary>Builds a <see cref="BuildPageViewModel"/> over stub collaborators that never resolve, since these tests never trigger a build.</summary>
    private BuildPageViewModel BuildStubBuildPage() => new(
        new StubPreflightService(),
        new GitStatusStore(new StubGitStatusService(), repositoryContext),
        new StubAdapterHostBuildCoordinator(),
        new StubBuildHistoryStore(),
        new StubSettingsStore(),
        _ => { },
        _ => { },
        repositoryContext);

    /// <summary>Builds an <see cref="EnvironmentPageViewModel"/> over stub collaborators, since these tests never inspect its checks.</summary>
    private EnvironmentPageViewModel BuildStubEnvironmentPage() =>
        new(new StubPreflightService(), new GitStatusStore(new StubGitStatusService(), repositoryContext), repositoryContext);

    /// <summary>Starts with the Build page selected.</summary>
    [Fact]
    public void StartsOnTheBuildPage()
    {
        var viewModel = BuildViewModel();

        Assert.IsType<BuildPageViewModel>(viewModel.CurrentPage);
    }

    /// <summary>Navigates to each page's ViewModel when its command executes.</summary>
    [Fact]
    public void NavigationCommandsSwitchTheCurrentPage()
    {
        var viewModel = BuildViewModel();

        viewModel.NavigateToEnvironmentCommand.Execute(null);
        Assert.IsType<EnvironmentPageViewModel>(viewModel.CurrentPage);

        viewModel.NavigateToSettingsCommand.Execute(null);
        Assert.IsType<SettingsPageViewModel>(viewModel.CurrentPage);

        viewModel.NavigateToBuildCommand.Execute(null);
        Assert.IsType<BuildPageViewModel>(viewModel.CurrentPage);
    }

    /// <summary>Preserves each page's own ViewModel instance across repeated navigation, so page state survives switching away and back.</summary>
    [Fact]
    public void NavigatingBackToAPageReturnsTheSameViewModelInstance()
    {
        var viewModel = BuildViewModel();
        var initialBuildPage = viewModel.CurrentPage;

        viewModel.NavigateToSettingsCommand.Execute(null);
        viewModel.NavigateToBuildCommand.Execute(null);

        Assert.Same(initialBuildPage, viewModel.CurrentPage);
    }

    /// <summary>
    /// Raises <c>PropertyChanged</c> for <see cref="MainWindowViewModel.CurrentPage"/> and the three
    /// per-page "is active" flags that highlight the current page's nav item, on navigation.
    /// </summary>
    [Fact]
    public void NavigationRaisesPropertyChangedForCurrentPage()
    {
        var viewModel = BuildViewModel();
        var raisedPropertyNames = new List<string?>();
        viewModel.PropertyChanged += (_, args) => raisedPropertyNames.Add(args.PropertyName);

        viewModel.NavigateToEnvironmentCommand.Execute(null);

        Assert.Equal(
            [
                nameof(MainWindowViewModel.CurrentPage),
                nameof(MainWindowViewModel.IsBuildPageActive),
                nameof(MainWindowViewModel.IsEnvironmentPageActive),
                nameof(MainWindowViewModel.IsSettingsPageActive),
            ],
            raisedPropertyNames);
    }

    /// <summary>Does not raise <c>PropertyChanged</c> when navigating to the page that is already current.</summary>
    [Fact]
    public void NavigatingToTheAlreadyCurrentPageDoesNotRaisePropertyChanged()
    {
        var viewModel = BuildViewModel();
        var raisedPropertyNames = new List<string?>();
        viewModel.PropertyChanged += (_, args) => raisedPropertyNames.Add(args.PropertyName);

        viewModel.NavigateToBuildCommand.Execute(null);

        Assert.Empty(raisedPropertyNames);
    }

    /// <summary>Reports every required tool as Found; a stub for tests that never inspect preflight behavior.</summary>
    private sealed class StubPreflightService : IPreflightService
    {
        /// <inheritdoc/>
        public Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ToolchainCheckResult>>([]);
    }

    /// <summary>Reports a clean, pushed git status; a stub for tests that never inspect git gating.</summary>
    private sealed class StubGitStatusService : IGitStatusService
    {
        /// <inheritdoc/>
        public Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"));
    }

    /// <summary>Never invoked by these tests; throws if it ever is.</summary>
    private sealed class StubAdapterHostBuildCoordinator : IAdapterHostBuildCoordinator
    {
        /// <inheritdoc/>
        public Task<AdapterHostBuildResult> BuildAsync(
            AdapterHostBuildRequest request,
            Action<string>? onOutput = null,
            Action<BuildStageEvent>? onStage = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Navigation tests should never start a build.");
    }

    /// <summary>Reports no build history; a stub for tests that never inspect recorded builds.</summary>
    private sealed class StubBuildHistoryStore : IBuildHistoryStore
    {
        /// <inheritdoc/>
        public IReadOnlyList<BuildHistoryEntry> GetRecent() => [];

        /// <inheritdoc/>
        public void Add(BuildHistoryEntry entry)
        {
        }
    }

    /// <summary>Reports <see cref="BuilderSettings"/>'s own defaults and discards writes; a stub for tests that never inspect settings.</summary>
    private sealed class StubSettingsStore : ISettingsStore
    {
        /// <inheritdoc/>
        public BuilderSettings Load() => new();

        /// <inheritdoc/>
        public void Save(BuilderSettings settings)
        {
        }
    }

    /// <summary>Never invoked by these tests; throws if it ever is.</summary>
    private sealed class StubFolderPickerService : IFolderPickerService
    {
        /// <inheritdoc/>
        public string? PickFolder(string title, string? initialDirectory) =>
            throw new InvalidOperationException("Navigation tests should never open a folder picker.");
    }
}
