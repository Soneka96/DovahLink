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
        var environmentStore = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, new OutputPathContext(null));
        var buildPage = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new StubBuildOutputOwnershipGuard(),
            new LogViewModel(),
            stage => new BuildStageViewModel(stage));
        var environmentPage = new EnvironmentPageViewModel(environmentStore, gitStatusStore);
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
        var environmentStore = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, new OutputPathContext(null));
        var buildPage = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new StubBuildOutputOwnershipGuard(),
            new LogViewModel(),
            stage => new BuildStageViewModel(stage));
        var environmentPage = new EnvironmentPageViewModel(environmentStore, gitStatusStore);
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
        var environmentStore = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, new OutputPathContext(null));
        var buildPage = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new StubBuildOutputOwnershipGuard(),
            new LogViewModel(),
            stage => new BuildStageViewModel(stage));
        var environmentPage = new EnvironmentPageViewModel(environmentStore, gitStatusStore);
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
    /// A configured output path override reaches both the Environment/Build pages' shared preflight
    /// check and the actual build request a subsequent build sends the coordinator -- through the same
    /// <see cref="IOutputPathContext"/> instance both <see cref="EnvironmentStore"/> and
    /// <see cref="BuildPageViewModel"/> read -- so preflight can never report a destination as usable
    /// while the real build targets a different one.
    /// </summary>
    [Fact]
    public async Task ASettingsOutputPathOverrideReachesBothThePreflightCheckAndTheBuildRequest()
    {
        var outputPathContext = new OutputPathContext(@"D:\custom-out");
        var preflightService = new FakePreflightService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, outputPathContext);
        var buildCoordinator = new FakeAdapterHostBuildCoordinatorThatRecordsRequests();
        var buildPage = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            buildCoordinator,
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext,
            outputPathContext,
            new StubBuildOutputOwnershipGuard(),
            new LogViewModel(),
            stage => new BuildStageViewModel(stage));

        await buildPage.InitializeAsync();
        Assert.Equal(@"D:\custom-out", Assert.Single(preflightService.CapturedOutputPathOverrides));

        buildPage.BuildCommand.Execute(null);
        await buildPage.RunningBuildTask!;

        Assert.Equal(@"D:\custom-out", buildCoordinator.LastRequest?.OutputRootOverride);
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
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));
        var buildPage = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new StubBuildOutputOwnershipGuard(),
            new LogViewModel(),
            stage => new BuildStageViewModel(stage));
        var environmentPage = new EnvironmentPageViewModel(environmentStore, gitStatusStore);
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

        /// <summary>Gets every <paramref name="outputPathOverride"/> a caller has requested a check for, in call order.</summary>
        public List<string?> CapturedOutputPathOverrides { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, string? outputPathOverride = null, CancellationToken cancellationToken = default)
        {
            CapturedStartPaths.Add(startPath);
            CapturedOutputPathOverrides.Add(outputPathOverride);
            return Task.FromResult<IReadOnlyList<ToolchainCheckResult>>([]);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ToolchainCheckResult> RefreshOutputFolderCheck(IReadOnlyList<ToolchainCheckResult> previousResults, string? repositoryRoot, string? outputPathOverride) =>
            previousResults;
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

    /// <summary>
    /// Treats every output root as already owned, doing nothing: these tests exercise how a
    /// repository or output-path change propagates, not the ownership guard's own behavior (see
    /// <see cref="BuildOutputOwnershipGuardTests"/>), and must never touch real disk at the
    /// fabricated repository paths (for example <c>C:\repo</c>, <c>D:\custom-out</c>) they construct.
    /// </summary>
    private sealed class StubBuildOutputOwnershipGuard : IBuildOutputOwnershipGuard
    {
        /// <inheritdoc/>
        public void EnsureOwned(string outputRoot, string repositoryRoot)
        {
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

    /// <summary>Succeeds using the test assembly's own DLL as a real, always-present archive; records the request it was given.</summary>
    private sealed class FakeAdapterHostBuildCoordinatorThatRecordsRequests : IAdapterHostBuildCoordinator
    {
        /// <summary>Gets the request passed to the most recent <see cref="BuildAsync"/> call, or <see langword="null"/> before any call.</summary>
        public AdapterHostBuildRequest? LastRequest { get; private set; }

        /// <inheritdoc/>
        public Task<AdapterHostBuildResult> BuildAsync(
            AdapterHostBuildRequest request,
            Action<string>? onOutput = null,
            Action<BuildStageEvent>? onStage = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AdapterHostBuildResult(typeof(GitStatusSharingTests).Assembly.Location));
        }
    }
}
