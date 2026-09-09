using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Preflight;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies the Build page's preflight/git gating, acknowledgement flow, and build execution.</summary>
public sealed class BuildPageViewModelTests
{
    /// <summary>Builds a view model over the given (or default all-passing) fakes.</summary>
    private static BuildPageViewModel BuildViewModel(
        FakePreflightService? preflightService = null,
        FakeGitStatusService? gitStatusService = null,
        FakeAdapterHostBuildCoordinator? buildCoordinator = null) => new(
        preflightService ?? new FakePreflightService(),
        gitStatusService ?? new FakeGitStatusService(),
        buildCoordinator ?? new FakeAdapterHostBuildCoordinator(),
        @"C:\repo");

    /// <summary>Disables the Build command with an explanatory reason before preflight and git status have loaded.</summary>
    [Fact]
    public void CanBuildIsFalseBeforeInitialization()
    {
        var viewModel = BuildViewModel();

        Assert.False(viewModel.CanBuild);
        Assert.NotNull(viewModel.BuildBlockedReason);
    }

    /// <summary>Allows building once every required check passes and git status is clean and pushed.</summary>
    [Fact]
    public async Task InitializeAsyncAllowsBuildingWhenEverythingPasses()
    {
        var viewModel = BuildViewModel();

        await viewModel.InitializeAsync();

        Assert.True(viewModel.CanBuild);
        Assert.Null(viewModel.BuildBlockedReason);
    }

    /// <summary>Blocks building and names the missing tool when a required preflight check is not Found.</summary>
    [Fact]
    public async Task InitializeAsyncBlocksBuildingWhenARequiredCheckIsMissing()
    {
        var preflightService = new FakePreflightService
        {
            Results = [new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH")],
        };
        var viewModel = BuildViewModel(preflightService: preflightService);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.CanBuild);
        Assert.Contains("CMake", viewModel.BuildBlockedReason!);
    }

    /// <summary>Blocks building and reports the failure when git status cannot be determined.</summary>
    [Fact]
    public async Task InitializeAsyncBlocksBuildingWhenGitStatusCannotBeDetermined()
    {
        var gitStatusService = new FakeGitStatusService { ThrownException = new InvalidOperationException("not a git repository") };
        var viewModel = BuildViewModel(gitStatusService: gitStatusService);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.CanBuild);
        Assert.Contains("not a git repository", viewModel.BuildBlockedReason!);
    }

    /// <summary>Starts a build immediately when the source is clean and pushed.</summary>
    [Fact]
    public async Task BuildCommandStartsImmediatelyForACleanPushedSource()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(1, buildCoordinator.CallCount);
        Assert.False(viewModel.IsAwaitingConfirmation);
        Assert.Equal(BuildHistoryResult.Succeeded, viewModel.LastOutcome);
    }

    /// <summary>Opens the acknowledgement prompt instead of building immediately when the working tree is dirty.</summary>
    [Fact]
    public async Task BuildCommandOpensConfirmationForADirtySource()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Dirty, RemoteSyncState.Pushed, "abc123"),
        };
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(gitStatusService: gitStatusService, buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);

        Assert.True(viewModel.IsAwaitingConfirmation);
        Assert.Equal(0, buildCoordinator.CallCount);
    }

    /// <summary>Opens the acknowledgement prompt instead of building immediately when commits are not pushed.</summary>
    [Fact]
    public async Task BuildCommandOpensConfirmationForAnUnpushedSource()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.NotPushed, "abc123"),
        };
        var viewModel = BuildViewModel(gitStatusService: gitStatusService);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);

        Assert.True(viewModel.IsAwaitingConfirmation);
    }

    /// <summary>Starts the build once the acknowledgement prompt is confirmed.</summary>
    [Fact]
    public async Task ConfirmBuildCommandStartsTheBuildAndClosesThePrompt()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Dirty, RemoteSyncState.Pushed, "abc123"),
        };
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(gitStatusService: gitStatusService, buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);

        viewModel.ConfirmBuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.IsAwaitingConfirmation);
        Assert.Equal(1, buildCoordinator.CallCount);
    }

    /// <summary>Dismisses the acknowledgement prompt without starting a build.</summary>
    [Fact]
    public async Task CancelConfirmationCommandClosesThePromptWithoutBuilding()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Dirty, RemoteSyncState.Pushed, "abc123"),
        };
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(gitStatusService: gitStatusService, buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);

        viewModel.CancelConfirmationCommand.Execute(null);

        Assert.False(viewModel.IsAwaitingConfirmation);
        Assert.Equal(0, buildCoordinator.CallCount);
        Assert.Null(viewModel.RunningBuildTask);
    }

    /// <summary>Reports a failed build's outcome and message without throwing out of the command.</summary>
    [Fact]
    public async Task BuildCommandReportsFailureWhenTheCoordinatorThrows()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("the adapter build failed") };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Failed, viewModel.LastOutcome);
        Assert.Equal("the adapter build failed", viewModel.LastOutcomeMessage);
        Assert.False(viewModel.IsBuilding);
    }

    /// <summary>Cancels an in-flight build and reports it as Cancelled.</summary>
    [Fact]
    public async Task CancelCommandCancelsAnInFlightBuild()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        Assert.True(viewModel.IsBuilding);

        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.IsBuilding);
        Assert.Equal(BuildHistoryResult.Cancelled, viewModel.LastOutcome);
        Assert.Null(viewModel.LastOutcomeMessage);
    }

    /// <summary>Does nothing when Build is executed directly while blocked, bypassing the bound command's own CanExecute gate.</summary>
    [Fact]
    public void BuildCommandDoesNothingWhenBuildIsBlocked()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);

        viewModel.BuildCommand.Execute(null);

        Assert.Equal(0, buildCoordinator.CallCount);
        Assert.Null(viewModel.RunningBuildTask);
    }

    /// <summary>Does nothing when Confirm is executed directly without an open acknowledgement prompt.</summary>
    [Fact]
    public async Task ConfirmBuildCommandDoesNothingWhenNotAwaitingConfirmation()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.ConfirmBuildCommand.Execute(null);

        Assert.Equal(0, buildCoordinator.CallCount);
        Assert.Null(viewModel.RunningBuildTask);
    }

    /// <summary>Reports the profile pill and the clean-build suffix in the build summary text.</summary>
    [Fact]
    public void BuildSummaryTextReflectsTheCleanBuildToggle()
    {
        var viewModel = BuildViewModel();

        Assert.Equal("Release", viewModel.BuildSummaryText);

        viewModel.IsCleanBuild = true;

        Assert.Equal("Release · Clean build", viewModel.BuildSummaryText);
    }

    /// <summary>Reports every non-Found tool, not only the first, in the blocked reason text.</summary>
    [Fact]
    public async Task InitializeAsyncListsEveryUnavailableToolInTheBlockedReason()
    {
        var preflightService = new FakePreflightService
        {
            Results =
            [
                new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH"),
                new ToolchainCheckResult("Python", ToolchainAvailability.Invalid, null, "exited with code 1"),
            ],
        };
        var viewModel = BuildViewModel(preflightService: preflightService);

        await viewModel.InitializeAsync();

        Assert.Contains("CMake", viewModel.BuildBlockedReason!);
        Assert.Contains("Python", viewModel.BuildBlockedReason!);
    }

    /// <summary>Reports every required build tool as available, for a fake that does not otherwise override <see cref="FakePreflightService.Results"/>.</summary>
    private sealed class FakePreflightService : IPreflightService
    {
        /// <summary>Gets the results to return; defaults to every required tool reporting Found.</summary>
        public IReadOnlyList<ToolchainCheckResult> Results { get; init; } = BuildAllFoundResults();

        /// <inheritdoc/>
        public Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results);

        /// <summary>Builds one Found result per required tool name.</summary>
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
        /// <summary>Gets the status to return; defaults to a clean tree already pushed to its upstream.</summary>
        public GitSourceStatus Status { get; init; } = new("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123");

        /// <summary>Gets the exception to throw instead of returning <see cref="Status"/>, or <see langword="null"/>.</summary>
        public Exception? ThrownException { get; init; }

        /// <inheritdoc/>
        public Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            ThrownException is not null ? throw ThrownException : Task.FromResult(Status);
    }

    /// <summary>Returns a successful build result unless configured to throw, hang, or count invocations.</summary>
    private sealed class FakeAdapterHostBuildCoordinator : IAdapterHostBuildCoordinator
    {
        /// <summary>Gets the result to return on success.</summary>
        public AdapterHostBuildResult Result { get; init; } = new(@"C:\repo\tooling\out\DovahLink-Adapter-0.1.0.zip");

        /// <summary>Gets the exception <see cref="BuildAsync"/> throws instead of succeeding, or <see langword="null"/>.</summary>
        public Exception? ThrownException { get; init; }

        /// <summary>Gets whether <see cref="BuildAsync"/> waits for cancellation instead of completing immediately.</summary>
        public bool WaitForCancellation { get; init; }

        /// <summary>Gets the number of times <see cref="BuildAsync"/> was called.</summary>
        public int CallCount { get; private set; }

        /// <inheritdoc/>
        public async Task<AdapterHostBuildResult> BuildAsync(
            AdapterHostBuildRequest request,
            Action<string>? onOutput = null,
            Action<BuildStageEvent>? onStage = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (ThrownException is not null)
            {
                throw ThrownException;
            }

            return Result;
        }
    }
}
