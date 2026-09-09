using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Persistence;
using DovahLink.DovahLinkBuilder.Preflight;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the Environment page's preflight checks, informational git status, and manual recheck.</summary>
public sealed class EnvironmentPageViewModelTests
{
    /// <summary>Builds a view model over the given (or default all-passing) fakes.</summary>
    private static EnvironmentPageViewModel BuildViewModel(
        FakePreflightService? preflightService = null,
        FakeGitStatusService? gitStatusService = null,
        IRepositoryContext? repositoryContext = null)
    {
        IRepositoryContext resolvedRepositoryContext = repositoryContext ?? new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(gitStatusService ?? new FakeGitStatusService(), resolvedRepositoryContext);
        var environmentStore = new EnvironmentStore(preflightService ?? new FakePreflightService(), gitStatusStore, resolvedRepositoryContext, new StubSettingsStore());
        return new(environmentStore, gitStatusStore);
    }

    /// <summary>Starts with no checks loaded and no git status.</summary>
    [Fact]
    public void StartsWithNoChecksLoaded()
    {
        var viewModel = BuildViewModel();

        Assert.Empty(viewModel.Checks);
        Assert.Null(viewModel.GitStatus);
    }

    /// <summary>Loads all 8 preflight checks, in preflight order, on initialization.</summary>
    [Fact]
    public async Task InitializeAsyncLoadsAllEightChecksInOrder()
    {
        var viewModel = BuildViewModel();

        await viewModel.InitializeAsync();

        Assert.Equal(8, viewModel.Checks.Count);
        Assert.Equal(
            ["Repository", ".NET SDK", "Visual Studio", "CMake", "vcpkg", "Papyrus Compiler", "Python", "Output Folder"],
            viewModel.Checks.Select(check => check.ToolName));
    }

    /// <summary>Loads the git status as the informational remote row on initialization.</summary>
    [Fact]
    public async Task InitializeAsyncLoadsGitStatus()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"),
        };
        var viewModel = BuildViewModel(gitStatusService: gitStatusService);

        await viewModel.InitializeAsync();

        Assert.Equal(RemoteSyncState.Pushed, viewModel.GitStatus?.RemoteSyncState);
        Assert.Null(viewModel.GitStatusError);
    }

    /// <summary>Reports a failure message instead of throwing when git status cannot be determined.</summary>
    [Fact]
    public async Task InitializeAsyncReportsGitStatusErrorInsteadOfThrowing()
    {
        var gitStatusService = new FakeGitStatusService { ThrownException = new InvalidOperationException("not a git repository") };
        var viewModel = BuildViewModel(gitStatusService: gitStatusService);

        await viewModel.InitializeAsync();

        Assert.Null(viewModel.GitStatus);
        Assert.Equal("not a git repository", viewModel.GitStatusError);
        Assert.False(viewModel.IsChecking);
    }

    /// <summary>Re-runs the preflight checks and refreshes them when Recheck executes.</summary>
    [Fact]
    public async Task RecheckCommandReRunsThePreflightChecks()
    {
        var preflightService = new FakePreflightService();
        var viewModel = BuildViewModel(preflightService: preflightService);
        await viewModel.InitializeAsync();
        Assert.Equal(1, preflightService.CallCount);

        preflightService.Results = [new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH")];
        viewModel.RecheckCommand.Execute(null);
        await viewModel.RunningRecheckTask!;

        Assert.Equal(2, preflightService.CallCount);
        Assert.Equal(ToolchainAvailability.Missing, Assert.Single(viewModel.Checks).Availability);
    }

    /// <summary>Reports whichever <see cref="ToolchainAvailability"/> the preflight service returns for a check.</summary>
    /// <param name="availability">The availability to verify passes through unchanged.</param>
    [Theory]
    [InlineData(ToolchainAvailability.Found)]
    [InlineData(ToolchainAvailability.Missing)]
    [InlineData(ToolchainAvailability.Invalid)]
    [InlineData(ToolchainAvailability.CouldNotCheck)]
    public async Task InitializeAsyncReportsEveryToolchainAvailabilityValue(ToolchainAvailability availability)
    {
        var preflightService = new FakePreflightService
        {
            Results = [new ToolchainCheckResult("CMake", availability, null, null)],
        };
        var viewModel = BuildViewModel(preflightService: preflightService);

        await viewModel.InitializeAsync();

        Assert.Equal(availability, Assert.Single(viewModel.Checks).Availability);
    }

    /// <summary>Disables Recheck while a check is already in progress, then re-enables it once the check finishes.</summary>
    [Fact]
    public async Task RecheckCommandIsDisabledWhileCheckingThenReenabledWhenDone()
    {
        var pauseSignal = new TaskCompletionSource();
        var preflightService = new FakePreflightService { PauseSignal = pauseSignal };
        var viewModel = BuildViewModel(preflightService: preflightService);

        viewModel.RecheckCommand.Execute(null);

        Assert.False(viewModel.RecheckCommand.CanExecute(null));

        pauseSignal.SetResult();
        await viewModel.RunningRecheckTask!;

        Assert.True(viewModel.RecheckCommand.CanExecute(null));
    }

    /// <summary>
    /// Disables Recheck during a refresh the shared environment store started on its own -- because
    /// the active repository changed -- not just one this page's own Recheck command triggered.
    /// </summary>
    [Fact]
    public async Task RecheckCommandIsDisabledDuringARepositoryChangeTriggeredRefresh()
    {
        var pauseSignal = new TaskCompletionSource();
        var preflightService = new FakePreflightService { PauseSignal = pauseSignal };
        var repositoryContext = new RepositoryContext(@"C:\repo-a");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new StubSettingsStore());
        var viewModel = new EnvironmentPageViewModel(environmentStore, gitStatusStore);

        repositoryContext.SetRepositoryRoot(@"C:\repo-b");

        Assert.False(viewModel.RecheckCommand.CanExecute(null));

        pauseSignal.SetResult();
        await environmentStore.RefreshAsync();

        Assert.True(viewModel.RecheckCommand.CanExecute(null));
    }

    /// <summary>Does nothing when Recheck is executed directly while already checking, bypassing the bound command's own CanExecute gate.</summary>
    [Fact]
    public async Task RecheckCommandDoesNothingWhileAlreadyChecking()
    {
        var pauseSignal = new TaskCompletionSource();
        var preflightService = new FakePreflightService { PauseSignal = pauseSignal };
        var viewModel = BuildViewModel(preflightService: preflightService);
        viewModel.RecheckCommand.Execute(null);
        Task firstTask = viewModel.RunningRecheckTask!;

        viewModel.RecheckCommand.Execute(null);

        Assert.Same(firstTask, viewModel.RunningRecheckTask);
        Assert.Equal(1, preflightService.CallCount);

        pauseSignal.SetResult();
        await firstTask;
    }

    /// <summary>Reports every required build tool as available, for a fake that does not otherwise override <see cref="Results"/>.</summary>
    private sealed class FakePreflightService : IPreflightService
    {
        /// <summary>
        /// Gets or sets the results to return; defaults to all 8 required tools reporting Found, in
        /// preflight order. Mutable so a test can reconfigure it between two calls on the same fake instance.
        /// </summary>
        public IReadOnlyList<ToolchainCheckResult> Results { get; set; } = BuildAllFoundResults();

        /// <summary>Gets or sets a signal <see cref="CheckAllAsync"/> awaits before completing, or <see langword="null"/> to complete immediately.</summary>
        public TaskCompletionSource? PauseSignal { get; set; }

        /// <summary>Gets the number of times <see cref="CheckAllAsync"/> was called.</summary>
        public int CallCount { get; private set; }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, string? outputPathOverride = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (PauseSignal is not null)
            {
                await PauseSignal.Task;
            }

            return Results;
        }

        /// <summary>Builds one Found result per required tool name, in preflight order.</summary>
        private static IReadOnlyList<ToolchainCheckResult> BuildAllFoundResults() =>
        [
            new ToolchainCheckResult("Repository", ToolchainAvailability.Found, @"C:\repo", null),
            new ToolchainCheckResult(".NET SDK", ToolchainAvailability.Found, "9.0.0", null),
            new ToolchainCheckResult("Visual Studio", ToolchainAvailability.Found, @"C:\vs", null),
            new ToolchainCheckResult("CMake", ToolchainAvailability.Found, "3.30.0", null),
            new ToolchainCheckResult("vcpkg", ToolchainAvailability.Found, @"C:\vs\vcpkg", null),
            new ToolchainCheckResult("Papyrus Compiler", ToolchainAvailability.Found, @"C:\skyrim\PapyrusCompiler.exe", null),
            new ToolchainCheckResult("Python", ToolchainAvailability.Found, "3.12.0", null),
            new ToolchainCheckResult("Output Folder", ToolchainAvailability.Found, @"C:\repo\tooling\out", null),
        ];
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
