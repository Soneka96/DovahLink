using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Persistence;
using DovahLink.DovahLinkBuilder.Preflight;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>
/// A small composition test proving the Build and Environment pages share one real
/// <see cref="GitStatusStore"/> instance rather than each independently caching its own copy: a
/// refresh triggered from either page must be reflected on the other.
/// </summary>
public sealed class GitStatusSharingTests
{
    /// <summary>A refresh triggered by the Environment page's Recheck is reflected in the Build page's GitNeedsAttention.</summary>
    [Fact]
    public async Task RecheckOnTheEnvironmentPageUpdatesTheBuildPagesGitNeedsAttention()
    {
        var gitStatusService = new FakeGitStatusService();
        var gitStatusStore = new GitStatusStore(gitStatusService, @"C:\repo");
        var buildPage = new BuildPageViewModel(
            new FakePreflightService(),
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            @"C:\repo");
        var environmentPage = new EnvironmentPageViewModel(new FakePreflightService(), gitStatusStore, @"C:\repo");
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
        var gitStatusStore = new GitStatusStore(gitStatusService, @"C:\repo");
        var buildPage = new BuildPageViewModel(
            new FakePreflightService(),
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            @"C:\repo");
        var environmentPage = new EnvironmentPageViewModel(new FakePreflightService(), gitStatusStore, @"C:\repo");
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
        var gitStatusStore = new GitStatusStore(gitStatusService, @"C:\repo");
        var buildPage = new BuildPageViewModel(
            new FakePreflightService(),
            gitStatusStore,
            new StubAdapterHostBuildCoordinator(),
            new StubBuildHistoryStore(),
            new StubSettingsStore(),
            _ => { },
            _ => { },
            @"C:\repo");
        var environmentPage = new EnvironmentPageViewModel(new FakePreflightService(), gitStatusStore, @"C:\repo");
        await buildPage.InitializeAsync();
        await environmentPage.InitializeAsync();
        Assert.Null(buildPage.BuildBlockedReason);

        gitStatusService.ThrownException = new InvalidOperationException("not a git repository");
        environmentPage.RecheckCommand.Execute(null);
        await environmentPage.RunningRecheckTask!;

        Assert.Equal("not a git repository", environmentPage.GitStatusError);
        Assert.Contains("not a git repository", buildPage.BuildBlockedReason!);
    }

    /// <summary>Reports every required tool as Found; a stub for tests that never inspect preflight behavior.</summary>
    private sealed class FakePreflightService : IPreflightService
    {
        /// <inheritdoc/>
        public Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ToolchainCheckResult>>([]);
    }

    /// <summary>Reports a clean, pushed git status unless reconfigured; the only fake this test suite mutates.</summary>
    private sealed class FakeGitStatusService : IGitStatusService
    {
        /// <summary>Gets or sets the status to return; defaults to a clean tree already pushed to its upstream.</summary>
        public GitSourceStatus Status { get; set; } = new("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123");

        /// <summary>Gets or sets the exception to throw instead of returning <see cref="Status"/>, or <see langword="null"/>.</summary>
        public Exception? ThrownException { get; set; }

        /// <inheritdoc/>
        public Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            ThrownException is not null ? throw ThrownException : Task.FromResult(Status);
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
