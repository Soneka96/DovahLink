using System.ComponentModel;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the shared git status store's refresh, failure reporting, and change notification.</summary>
public sealed class GitStatusStoreTests
{
    /// <summary>Starts with no status loaded and no error.</summary>
    [Fact]
    public void StartsWithNoStatusLoaded()
    {
        var store = new GitStatusStore(new FakeGitStatusService(), @"C:\repo");

        Assert.Null(store.Status);
        Assert.Null(store.StatusError);
    }

    /// <summary>Loads the git status and clears any previous error on a successful refresh.</summary>
    [Fact]
    public async Task RefreshAsyncLoadsStatusAndClearsError()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"),
        };
        var store = new GitStatusStore(gitStatusService, @"C:\repo");

        await store.RefreshAsync();

        Assert.Equal(RemoteSyncState.Pushed, store.Status?.RemoteSyncState);
        Assert.Null(store.StatusError);
    }

    /// <summary>Reports a failure message instead of throwing when git status cannot be determined.</summary>
    [Fact]
    public async Task RefreshAsyncReportsFailureInsteadOfThrowing()
    {
        var gitStatusService = new FakeGitStatusService { ThrownException = new InvalidOperationException("not a git repository") };
        var store = new GitStatusStore(gitStatusService, @"C:\repo");

        await store.RefreshAsync();

        Assert.Null(store.Status);
        Assert.Equal("not a git repository", store.StatusError);
    }

    /// <summary>Clears a previous failure once a later refresh succeeds.</summary>
    [Fact]
    public async Task RefreshAsyncClearsAPreviousFailureOnceItSucceeds()
    {
        var gitStatusService = new FakeGitStatusService { ThrownException = new InvalidOperationException("not a git repository") };
        var store = new GitStatusStore(gitStatusService, @"C:\repo");
        await store.RefreshAsync();
        Assert.NotNull(store.StatusError);

        gitStatusService.ThrownException = null;
        gitStatusService.Status = new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123");
        await store.RefreshAsync();

        Assert.Null(store.StatusError);
        Assert.NotNull(store.Status);
    }

    /// <summary>Raises PropertyChanged for Status and StatusError when a refresh actually changes either value.</summary>
    [Fact]
    public async Task RefreshAsyncRaisesPropertyChangedWhenValuesChange()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"),
        };
        var store = new GitStatusStore(gitStatusService, @"C:\repo");
        var raisedProperties = new List<string?>();
        store.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        await store.RefreshAsync();

        Assert.Contains(nameof(GitStatusStore.Status), raisedProperties);
    }

    /// <summary>Raises PropertyChanged for StatusError, symmetrically with Status, when a refresh reports a new failure.</summary>
    [Fact]
    public async Task RefreshAsyncRaisesPropertyChangedForStatusErrorWhenItChanges()
    {
        var gitStatusService = new FakeGitStatusService { ThrownException = new InvalidOperationException("not a git repository") };
        var store = new GitStatusStore(gitStatusService, @"C:\repo");
        var raisedProperties = new List<string?>();
        store.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        await store.RefreshAsync();

        Assert.Contains(nameof(GitStatusStore.StatusError), raisedProperties);
    }

    /// <summary>Raises no further notification when a refresh's result is unchanged from the current value.</summary>
    [Fact]
    public async Task RefreshAsyncRaisesNoNotificationWhenTheResultIsUnchanged()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"),
        };
        var store = new GitStatusStore(gitStatusService, @"C:\repo");
        await store.RefreshAsync();
        var raisedProperties = new List<string?>();
        store.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        await store.RefreshAsync();

        Assert.Empty(raisedProperties);
    }

    /// <summary>Reports a clean, pushed git status unless configured to throw or report otherwise.</summary>
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
}
