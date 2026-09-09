using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Persistence;
using DovahLink.DovahLinkBuilder.Preflight;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>
/// A small composition test proving the Build and Environment pages share one real
/// <see cref="GitStatusStore"/> instance and one real <see cref="RepositoryContext"/> instance
/// rather than each independently caching its own copy: a refresh triggered from either page, or a
/// repository root change, must be reflected on the other.
/// </summary>
public sealed class GitStatusSharingTests
{
    /// <summary>A refresh triggered by the Environment page's Recheck is reflected in the Build page's GitNeedsAttention.</summary>
    [Fact]
    public async Task RecheckOnTheEnvironmentPageUpdatesTheBuildPagesGitNeedsAttention()
    {
        var gitStatusService = new FakeGitStatusService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(gitStatusService, repositoryContext);
        var buildPage = new BuildPageViewModel(
            new FakePreflightService(),
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext);
        var environmentPage = new EnvironmentPageViewModel(new FakePreflightService(), gitStatusStore, repositoryContext);
        await buildPage.InitializeAsync();
        await environmentPage.InitializeAsync();
        Assert.False(buildPage.GitNeedsAttention);

        gitStatusService.Status = new GitSourceStatus("main", WorkingTreeState.Dirty, RemoteSyncState.Pushed, "abc123");
        environmentPage.RecheckCommand.Execute(null);
        await environmentPage.RunningRecheckTask!;

        Assert.True(buildPage.GitNeedsAttention);
    }

    /// <summary>A refresh triggered by the Build page is reflected in the Environment page's GitStatus, symmetrically with the other direction.</summary>
    [Fact]
    public async Task ANewBuildRefreshOnTheBuildPageUpdatesTheEnvironmentPagesGitStatus()
    {
        var gitStatusService = new FakeGitStatusService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(gitStatusService, repositoryContext);
        var buildPage = new BuildPageViewModel(
            new FakePreflightService(),
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext);
        var environmentPage = new EnvironmentPageViewModel(new FakePreflightService(), gitStatusStore, repositoryContext);
        await buildPage.InitializeAsync();
        await environmentPage.InitializeAsync();
        Assert.Equal(RemoteSyncState.Pushed, environmentPage.GitStatus?.RemoteSyncState);

        gitStatusService.Status = new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.NotPushed, "def456");
        await buildPage.InitializeAsync();

        Assert.Equal(RemoteSyncState.NotPushed, environmentPage.GitStatus?.RemoteSyncState);
    }

    /// <summary>
    /// A git status failure surfaced by the Environment page's Recheck is also reflected in the Build
    /// page's BuildBlockedReason, symmetrically with the successful-status relay tested above.
    /// </summary>
    [Fact]
    public async Task RecheckOnTheEnvironmentPageWithAFailingGitStatusUpdatesTheBuildPagesBuildBlockedReason()
    {
        var gitStatusService = new FakeGitStatusService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(gitStatusService, repositoryContext);
        var buildPage = new BuildPageViewModel(
            new FakePreflightService(),
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext);
        var environmentPage = new EnvironmentPageViewModel(new FakePreflightService(), gitStatusStore, repositoryContext);
        await buildPage.InitializeAsync();
        await environmentPage.InitializeAsync();
        Assert.Null(buildPage.BuildBlockedReason);

        gitStatusService.ThrownException = new InvalidOperationException("not a git repository");
        environmentPage.RecheckCommand.Execute(null);
        await environmentPage.RunningRecheckTask!;

        Assert.Equal("not a git repository", environmentPage.GitStatusError);
        Assert.Contains("not a git repository", buildPage.BuildBlockedReason!);
    }

    /// <summary>
    /// Changing the shared <see cref="RepositoryContext"/>'s root -- exactly as Settings does when the
    /// repository override changes -- is observed by the shared <see cref="GitStatusStore"/> and by
    /// both pages' own preflight checks on their next refresh, rather than either continuing to check
    /// whatever repository root each was originally constructed with.
    /// </summary>
    [Fact]
    public async Task ChangingTheSharedRepositoryContextIsObservedByGitStatusAndBothPagesPreflight()
    {
        var gitStatusService = new FakeGitStatusService();
        var preflightService = new FakePreflightService();
        var repositoryContext = new RepositoryContext(@"C:\repo-a");
        var gitStatusStore = new GitStatusStore(gitStatusService, repositoryContext);
        var buildPage = new BuildPageViewModel(
            preflightService,
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext);
        var environmentPage = new EnvironmentPageViewModel(preflightService, gitStatusStore, repositoryContext);
        await buildPage.InitializeAsync();
        await environmentPage.InitializeAsync();
        Assert.Equal(@"C:\repo-a", gitStatusService.LastRequestedRepositoryRoot);
        Assert.All(preflightService.CapturedStartPaths, path => Assert.Equal(@"C:\repo-a", path));

        repositoryContext.SetRepositoryRoot(@"C:\repo-b");
        preflightService.CapturedStartPaths.Clear();
        await buildPage.InitializeAsync();
        await environmentPage.InitializeAsync();

        Assert.Equal(@"C:\repo-b", gitStatusService.LastRequestedRepositoryRoot);
        Assert.All(preflightService.CapturedStartPaths, path => Assert.Equal(@"C:\repo-b", path));
    }

    /// <summary>Reports every required tool as Found; a stub for tests that never inspect preflight behavior.</summary>
    private sealed class FakePreflightService : IPreflightService
    {
        /// <summary>Gets every <paramref name="startPath"/> a caller has requested a check for, in call order.</summary>
        public List<string> CapturedStartPaths { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, CancellationToken cancellationToken = default)
        {
            CapturedStartPaths.Add(startPath);
            return Task.FromResult<IReadOnlyList<ToolchainCheckResult>>([]);
        }
    }

    /// <summary>Reports a clean, pushed git status unless reconfigured; the only fake this test suite mutates.</summary>
    private sealed class FakeGitStatusService : IGitStatusService
    {
        /// <summary>Gets or sets the status to return; defaults to a clean tree already pushed to its upstream.</summary>
        public GitSourceStatus Status { get; set; } = new("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123");

        /// <summary>Gets or sets the exception to throw instead of returning <see cref="Status"/>, or <see langword="null"/>.</summary>
        public Exception? ThrownException { get; set; }

        /// <summary>Gets the repository root most recently requested through <see cref="GetStatusAsync"/>.</summary>
        public string? LastRequestedRepositoryRoot { get; private set; }

        /// <inheritdoc/>
        public Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            LastRequestedRepositoryRoot = repositoryRoot;
            return ThrownException is not null ? throw ThrownException : Task.FromResult(Status);
        }
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
            throw new InvalidOperationException("Git status sharing tests should never start a build.");
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
}
