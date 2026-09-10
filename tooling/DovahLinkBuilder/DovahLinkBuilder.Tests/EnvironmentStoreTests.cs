using System.IO;
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
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));

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
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));

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
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));
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
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));

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
        _ = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));

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
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));

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

    /// <summary>
    /// Eventually settles on the latest output path override even when it changes again while a full
    /// refresh for an earlier change is still running: without this, the in-flight refresh's own
    /// eventually-resolved result (captured with the output path from before the change) would overwrite
    /// whatever <see cref="EnvironmentStore.PreflightResults"/> the change's own narrow patch just set,
    /// silently reverting to a stale Output Folder result.
    /// </summary>
    [Fact]
    public async Task RefreshEventuallyChecksTheLatestOutputPathWhenItChangesAgainWhileARefreshIsInProgress()
    {
        var pauseSignal = new TaskCompletionSource();
        var preflightService = new FakePreflightService { PauseSignal = pauseSignal };
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var outputPathContext = new OutputPathContext(null);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, outputPathContext);

        Task refresh = store.RefreshAsync();
        Assert.True(store.IsRefreshing);

        // Changes while the refresh above is still paused mid-flight, coalescing onto it rather than
        // starting an independent one.
        outputPathContext.SetOutputPath(@"D:\new-out");

        pauseSignal.SetResult();
        await refresh;

        Assert.False(store.IsRefreshing);
        Assert.Equal(@"D:\new-out", preflightService.CapturedOutputPathOverrides[^1]);
    }

    /// <summary>Passes the persisted output path override through to preflight, so it checks the actual configured destination.</summary>
    [Fact]
    public async Task RefreshAsyncPassesTheOutputPathOverrideToPreflight()
    {
        var preflightService = new FakePreflightService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var outputPathContext = new OutputPathContext(@"D:\custom-out");
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, outputPathContext);

        await store.RefreshAsync();

        Assert.Equal(@"D:\custom-out", Assert.Single(preflightService.CapturedOutputPathOverrides));
    }

    /// <summary>
    /// Recomputes only the Output Folder check when the output path context changes, rather than
    /// re-running the full preflight battery -- proving the store distinguishes an output-path-only
    /// change from a repository change, which does need every check re-run.
    /// </summary>
    [Fact]
    public async Task ChangingTheOutputPathContextRefreshesOnlyTheOutputFolderCheck()
    {
        var preflightService = new FakePreflightService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var outputPathContext = new OutputPathContext(null);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, outputPathContext);
        await store.RefreshAsync();
        Assert.Equal(1, preflightService.CallCount);

        outputPathContext.SetOutputPath(@"D:\new-out");

        Assert.Equal(1, preflightService.CallCount);
        Assert.Equal(1, preflightService.RefreshOutputFolderCheckCallCount);
        Assert.False(store.IsRefreshing);
        Assert.Equal(@"D:\new-out", store.PreflightResults[^1].Detail);
    }

    /// <summary>Does nothing when the output path context changes before any full refresh has ever run.</summary>
    [Fact]
    public void ChangingTheOutputPathContextBeforeAnyRefreshDoesNothing()
    {
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var outputPathContext = new OutputPathContext(null);
        var store = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, outputPathContext);

        outputPathContext.SetOutputPath(@"D:\new-out");

        Assert.Empty(store.PreflightResults);
    }

    /// <summary>Raises PropertyChanged for PreflightResults, but not IsRefreshing, when the output path context changes -- a narrow recheck never enters the refreshing state.</summary>
    [Fact]
    public async Task ChangingTheOutputPathContextRaisesPropertyChangedForPreflightResultsOnlyNotIsRefreshing()
    {
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var outputPathContext = new OutputPathContext(null);
        var store = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, outputPathContext);
        await store.RefreshAsync();
        var raisedProperties = new List<string?>();
        store.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        outputPathContext.SetOutputPath(@"D:\new-out");

        Assert.Contains(nameof(EnvironmentStore.PreflightResults), raisedProperties);
        Assert.DoesNotContain(nameof(EnvironmentStore.IsRefreshing), raisedProperties);
    }

    /// <summary>
    /// Regression test for the real chain an invalid Settings output path travels through:
    /// <see cref="OutputPathContext.SetOutputPath"/> raises <see cref="OutputPathContext.PropertyChanged"/>
    /// synchronously, which <see cref="EnvironmentStore"/> handles by calling a REAL
    /// <see cref="PreflightService"/> (not <see cref="FakePreflightService"/>), so a value the real
    /// filesystem check cannot create a directory for must still resolve to
    /// <see cref="ToolchainAvailability.CouldNotCheck"/> rather than throwing out of this synchronous
    /// property-change handler.
    /// </summary>
    [Fact]
    public async Task ChangingTheOutputPathContextToAMalformedValueDoesNotThrowAndReportsCouldNotCheck()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = Path.Combine(temporaryDirectory.Path, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "adapter"));
        File.WriteAllText(Path.Combine(repositoryRoot, "adapter", "vcpkg.json"), "{}");
        var repositoryContext = new RepositoryContext(repositoryRoot);
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var outputPathContext = new OutputPathContext(null);
        var preflightService = new PreflightService(new StubCommandRunner(), TimeSpan.FromMilliseconds(200));
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, outputPathContext);
        await store.RefreshAsync();
        string malformedOverride = Path.Combine(temporaryDirectory.Path, "custom-out\0bad");

        Exception? thrown = Record.Exception(() => outputPathContext.SetOutputPath(malformedOverride));

        Assert.Null(thrown);
        ToolchainCheckResult outputFolderResult = Assert.Single(
            store.PreflightResults, result => result.ToolName == "Output Folder");
        Assert.Equal(ToolchainAvailability.CouldNotCheck, outputFolderResult.Availability);
    }

    /// <summary>Raises PropertyChanged for PreflightResults and IsRefreshing as a refresh starts and finishes.</summary>
    [Fact]
    public async Task RefreshAsyncRaisesPropertyChangedForPreflightResultsAndIsRefreshing()
    {
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, new OutputPathContext(null));
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

        /// <summary>Gets or sets the exception <see cref="CheckAllAsync"/> throws instead of returning results, or <see langword="null"/> to succeed normally.</summary>
        public Exception? ExceptionToThrow { get; set; }

        /// <summary>Gets the number of times <see cref="CheckAllAsync"/> was called.</summary>
        public int CallCount { get; private set; }

        /// <summary>Gets every <paramref name="startPath"/> a caller has requested a check for, in call order.</summary>
        public List<string> CapturedStartPaths { get; } = [];

        /// <summary>Gets every <paramref name="outputPathOverride"/> a caller has requested a check for, in call order.</summary>
        public List<string?> CapturedOutputPathOverrides { get; } = [];

        /// <summary>Gets the number of times <see cref="RefreshOutputFolderCheck"/> was called.</summary>
        public int RefreshOutputFolderCheckCallCount { get; private set; }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, string? outputPathOverride = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            CapturedStartPaths.Add(startPath);
            CapturedOutputPathOverrides.Add(outputPathOverride);
            if (PauseSignal is not null)
            {
                await PauseSignal.Task;
            }

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return
            [
                new ToolchainCheckResult("Repository", ToolchainAvailability.Found, startPath, null),
                new ToolchainCheckResult("Output Folder", ToolchainAvailability.Found, outputPathOverride ?? Path.Combine(startPath, "tooling", "out"), null),
            ];
        }

        /// <inheritdoc/>
        public IReadOnlyList<ToolchainCheckResult> RefreshOutputFolderCheck(IReadOnlyList<ToolchainCheckResult> previousResults, string? repositoryRoot, string? outputPathOverride)
        {
            RefreshOutputFolderCheckCallCount++;
            if (previousResults.Count == 0)
            {
                return previousResults;
            }

            ToolchainCheckResult[] updatedResults = previousResults.ToArray();
            updatedResults[^1] = new ToolchainCheckResult("Output Folder", ToolchainAvailability.Found, outputPathOverride ?? repositoryRoot, null);
            return updatedResults;
        }
    }

    /// <summary>Reports a clean, pushed git status; a fake for tests that never inspect git gating.</summary>
    private sealed class FakeGitStatusService : IGitStatusService
    {
        /// <inheritdoc/>
        public Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"));
    }

    /// <summary>Records the failure and clears preflight results, rather than propagating, when preflight throws.</summary>
    [Fact]
    public async Task RefreshAsyncRecordsTheFailureAndClearsPreflightResultsWhenPreflightThrows()
    {
        var preflightService = new FakePreflightService { ExceptionToThrow = new InvalidOperationException("disk full") };
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));

        await store.RefreshAsync();

        Assert.Equal("disk full", store.RefreshError);
        Assert.Empty(store.PreflightResults);
        Assert.False(store.IsRefreshing);
    }

    /// <summary>Clears a previously recorded refresh error once a later refresh succeeds.</summary>
    [Fact]
    public async Task RefreshAsyncClearsARefreshErrorOnASubsequentSuccessfulRefresh()
    {
        var preflightService = new FakePreflightService { ExceptionToThrow = new InvalidOperationException("disk full") };
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));
        await store.RefreshAsync();
        Assert.NotNull(store.RefreshError);

        preflightService.ExceptionToThrow = null;
        await store.RefreshAsync();

        Assert.Null(store.RefreshError);
        Assert.NotEmpty(store.PreflightResults);
    }

    /// <summary>Propagates cancellation rather than misreporting it as a refresh error.</summary>
    [Fact]
    public async Task RefreshAsyncPropagatesCancellationWithoutRecordingItAsARefreshError()
    {
        var preflightService = new FakePreflightService { ExceptionToThrow = new OperationCanceledException() };
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RefreshAsync());

        Assert.Null(store.RefreshError);
        Assert.False(store.IsRefreshing);
    }

    /// <summary>Raises PropertyChanged for RefreshError when a refresh fails.</summary>
    [Fact]
    public async Task RefreshAsyncRaisesPropertyChangedForRefreshErrorWhenARefreshFails()
    {
        var preflightService = new FakePreflightService { ExceptionToThrow = new InvalidOperationException("disk full") };
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var store = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));
        var raisedProperties = new List<string?>();
        store.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        await store.RefreshAsync();

        Assert.Contains(nameof(EnvironmentStore.RefreshError), raisedProperties);
    }

    /// <summary>
    /// Succeeds every command immediately with no output, for a real <see cref="PreflightService"/>
    /// constructed to prove its own output-folder check's real exception handling, where every other
    /// probed tool's own result is irrelevant to the test.
    /// </summary>
    private sealed class StubCommandRunner : ICommandRunner
    {
        /// <inheritdoc/>
        public Task<int> RunAsync(
            BuildCommand command,
            Action<string>? onStandardOutput,
            Action<string>? onStandardError,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
