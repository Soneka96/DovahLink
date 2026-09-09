using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Preflight;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>
/// Verifies the shared preflight-and-git-status refresh: that it actually loads both, that
/// concurrent callers share one real check rather than duplicating it, and that a later call still
/// starts a genuinely new check once the previous one has finished.
/// </summary>
public sealed class EnvironmentStoreTests
{
    /// <summary>Loads preflight results and refreshes the shared git status in one call.</summary>
    [Fact]
    public async Task RefreshAsyncLoadsPreflightResultsAndRefreshesGitStatus()
    {
        var preflightService = new FakePreflightService();
        var gitStatusService = new FakeGitStatusService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(gitStatusService, repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext);

        await store.RefreshAsync();

        Assert.NotEmpty(store.PreflightResults);
        Assert.NotNull(gitStatusStore.Status);
    }

    /// <summary>Shares one real preflight-and-git check between two concurrent callers instead of duplicating it.</summary>
    [Fact]
    public async Task RefreshAsyncCoalescesConcurrentCallsIntoOneCheck()
    {
        var pauseSignal = new TaskCompletionSource();
        var preflightService = new FakePreflightService { PauseSignal = pauseSignal };
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext);

        Task first = store.RefreshAsync();
        Task second = store.RefreshAsync();
        Assert.Same(first, second);

        pauseSignal.SetResult();
        await first;

        Assert.Equal(1, preflightService.CallCount);
    }

    /// <summary>Starts a genuinely new check once the previous refresh has already completed, rather than reusing its stale result forever.</summary>
    [Fact]
    public async Task RefreshAsyncStartsANewCheckAfterThePreviousOneCompletes()
    {
        var preflightService = new FakePreflightService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext);
        await store.RefreshAsync();
        Assert.Equal(1, preflightService.CallCount);

        await store.RefreshAsync();

        Assert.Equal(2, preflightService.CallCount);
    }

    /// <summary>Reports refreshing only while a check is actually in progress.</summary>
    [Fact]
    public async Task IsRefreshingIsTrueOnlyWhileACheckIsInProgress()
    {
        var pauseSignal = new TaskCompletionSource();
        var preflightService = new FakePreflightService { PauseSignal = pauseSignal };
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext);

        Task refresh = store.RefreshAsync();
        Assert.True(store.IsRefreshing);

        pauseSignal.SetResult();
        await refresh;

        Assert.False(store.IsRefreshing);
    }

    /// <summary>Starts a fresh refresh whenever the shared repository context's root changes.</summary>
    [Fact]
    public void ChangingTheRepositoryContextStartsAFreshRefresh()
    {
        var preflightService = new FakePreflightService();
        var repositoryContext = new RepositoryContext(@"C:\repo-a");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        _ = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext);

        repositoryContext.SetRepositoryRoot(@"C:\repo-b");

        Assert.Equal(@"C:\repo-b", Assert.Single(preflightService.CapturedStartPaths));
    }

    /// <summary>
    /// Eventually settles on the latest repository root even when it changes again while a refresh for
    /// an earlier change is still running: the second change's own RefreshAsync call coalesces onto
    /// that already-running refresh rather than starting a second one, so nothing else would ever check
    /// the newer root unless the running refresh itself catches up before reporting done.
    /// </summary>
    [Fact]
    public async Task RefreshEventuallyChecksTheLatestRootWhenTheRepositoryChangesAgainWhileARefreshIsInProgress()
    {
        var pauseSignal = new TaskCompletionSource();
        var preflightService = new FakePreflightService { PauseSignal = pauseSignal };
        var repositoryContext = new RepositoryContext(@"C:\repo-a");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext);

        repositoryContext.SetRepositoryRoot(@"C:\repo-b");
        Assert.True(store.IsRefreshing);

        // Changes again while the repo-b refresh above is still paused mid-flight; this RefreshAsync
        // call coalesces onto that same in-flight refresh rather than starting an independent one.
        repositoryContext.SetRepositoryRoot(@"C:\repo-c");

        pauseSignal.SetResult();
        await store.RefreshAsync();

        Assert.False(store.IsRefreshing);
        Assert.Equal(@"C:\repo-c", preflightService.CapturedStartPaths[^1]);
    }

    /// <summary>Raises PropertyChanged for PreflightResults and IsRefreshing as a refresh starts and finishes.</summary>
    [Fact]
    public async Task RefreshAsyncRaisesPropertyChangedForPreflightResultsAndIsRefreshing()
    {
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext);
        var raisedProperties = new List<string?>();
        store.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        await store.RefreshAsync();

        Assert.Contains(nameof(EnvironmentStore.IsRefreshing), raisedProperties);
        Assert.Contains(nameof(EnvironmentStore.PreflightResults), raisedProperties);
    }

    /// <summary>Reports every required tool as Found and counts calls; a fake for tests that verify how often preflight actually ran.</summary>
    private sealed class FakePreflightService : IPreflightService
    {
        /// <summary>Gets or sets a signal <see cref="CheckAllAsync"/> awaits before completing, or <see langword="null"/> to complete immediately.</summary>
        public TaskCompletionSource? PauseSignal { get; set; }

        /// <summary>Gets the number of times <see cref="CheckAllAsync"/> was called.</summary>
        public int CallCount { get; private set; }

        /// <summary>Gets every <paramref name="startPath"/> a caller has requested a check for, in call order.</summary>
        public List<string> CapturedStartPaths { get; } = [];

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            CapturedStartPaths.Add(startPath);
            if (PauseSignal is not null)
            {
                await PauseSignal.Task;
            }

            return [new ToolchainCheckResult("Repository", ToolchainAvailability.Found, startPath, null)];
        }
    }

    /// <summary>Reports a clean, pushed git status; a fake for tests that never inspect git gating.</summary>
    private sealed class FakeGitStatusService : IGitStatusService
    {
        /// <inheritdoc/>
        public Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"));
    }
}
