using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Persistence;
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
        FakeAdapterHostBuildCoordinator? buildCoordinator = null,
        FakeBuildHistoryStore? buildHistoryStore = null,
        Action<string>? openOutputFolder = null,
        Action<string>? setClipboardText = null,
        string? repositoryRoot = null,
        IRepositoryContext? repositoryContext = null,
        IOutputPathContext? outputPathContext = null,
        IBuildOutputOwnershipGuard? outputOwnershipGuard = null,
        ILogViewModel? log = null,
        Func<BuildStage, IBuildStageViewModel>? buildStageViewModelFactory = null,
        IRuntimeBuildSettingsContext? runtimeBuildSettingsContext = null)
    {
        string resolvedRepositoryRoot = repositoryRoot ?? @"C:\repo";
        IRepositoryContext resolvedRepositoryContext = repositoryContext ?? new RepositoryContext(resolvedRepositoryRoot);
        IOutputPathContext resolvedOutputPathContext = outputPathContext ?? new OutputPathContext(null);
        var gitStatusStore = new GitStatusStore(gitStatusService ?? new FakeGitStatusService(), resolvedRepositoryContext);
        var environmentStore = new EnvironmentStore(preflightService ?? new FakePreflightService(), gitStatusStore, resolvedRepositoryContext, resolvedOutputPathContext);
        return new(
            environmentStore,
            gitStatusStore,
            buildCoordinator ?? new FakeAdapterHostBuildCoordinator(),
            buildHistoryStore ?? new FakeBuildHistoryStore(),
            openOutputFolder ?? (_ => { }),
            setClipboardText ?? (_ => { }),
            resolvedRepositoryContext,
            resolvedOutputPathContext,
            outputOwnershipGuard ?? new FakeBuildOutputOwnershipGuard(),
            log ?? new LogViewModel(_ => { }),
            buildStageViewModelFactory ?? (stage => new BuildStageViewModel(stage)),
            runtimeBuildSettingsContext ?? new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true));
    }

    /// <summary>Creates a real ZIP archive under <paramref name="temporaryDirectoryPath"/> containing the given entries.</summary>
    /// <param name="temporaryDirectoryPath">The temporary directory to create the source files and archive under.</param>
    /// <param name="entries">Each entry's flat file name and text content.</param>
    /// <returns>The created archive's path.</returns>
    private static string CreateRealZip(string temporaryDirectoryPath, params (string Name, string Content)[] entries)
    {
        string sourceDirectory = Path.Combine(temporaryDirectoryPath, "source");
        Directory.CreateDirectory(sourceDirectory);
        foreach ((string name, string content) in entries)
        {
            File.WriteAllText(Path.Combine(sourceDirectory, name), content);
        }

        string archivePath = Path.Combine(temporaryDirectoryPath, "archive.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);
        return archivePath;
    }

    /// <summary>Disables the Build command with an explanatory reason before preflight and git status have loaded.</summary>
    [Fact]
    public void CanBuildIsFalseBeforeInitialization()
    {
        var viewModel = BuildViewModel();

        Assert.False(viewModel.CanBuild);
        Assert.NotNull(viewModel.BuildBlockedReason);
    }

    /// <summary>
    /// Blocks building for the entire duration of the refresh a repository change triggers, and never
    /// lets a build attempt made during that window reach the coordinator with the previous root --
    /// even though the last-known preflight and git results (still describing the old repository)
    /// have not been overwritten yet when the change first happens.
    /// </summary>
    [Fact]
    public async Task ChangingTheRepositoryBlocksBuildingUntilTheRefreshForTheNewRootCompletes()
    {
        var preflightService = new FakePreflightService();
        var repositoryContext = new RepositoryContext(@"C:\repo-a");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            buildCoordinator,
            new FakeBuildHistoryStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new FakeBuildOutputOwnershipGuard(),
            new LogViewModel(_ => { }),
            stage => new BuildStageViewModel(stage),
            new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true));
        await viewModel.InitializeAsync();
        Assert.True(viewModel.CanBuild);

        var pauseSignal = new TaskCompletionSource();
        preflightService.PauseSignal = pauseSignal;
        repositoryContext.SetRepositoryRoot(@"C:\repo-b");

        Assert.False(viewModel.CanBuild);
        viewModel.BuildCommand.Execute(null);
        Assert.Null(viewModel.RunningBuildTask);
        Assert.Null(buildCoordinator.LastRequest);

        // Coalesces onto the same in-flight refresh the repository change itself already started,
        // giving the test a handle to await it without EnvironmentStore exposing one of its own.
        Task pendingRefresh = environmentStore.RefreshAsync();
        pauseSignal.SetResult();
        await pendingRefresh;

        Assert.True(viewModel.CanBuild);
        Assert.Equal(@"C:\repo-b", preflightService.CapturedStartPaths[^1]);
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

    /// <summary>
    /// Reacts to a git status refresh made directly on the shared store rather than one this page's
    /// own InitializeAsync triggered, proving the subscription that keeps GitNeedsAttention live when
    /// a different page (Environment's Recheck) refreshes the same shared store.
    /// </summary>
    [Fact]
    public async Task GitNeedsAttentionReactsToARefreshMadeDirectlyOnTheSharedStore()
    {
        var gitStatusService = new FakeGitStatusService();
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(gitStatusService, repositoryContext);
        var environmentStore = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, new OutputPathContext(null));
        var viewModel = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new FakeAdapterHostBuildCoordinator(),
            new FakeBuildHistoryStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new FakeBuildOutputOwnershipGuard(),
            new LogViewModel(_ => { }),
            stage => new BuildStageViewModel(stage),
            new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true));
        await viewModel.InitializeAsync();
        Assert.False(viewModel.GitNeedsAttention);

        gitStatusService.Status = new GitSourceStatus("main", WorkingTreeState.Dirty, RemoteSyncState.Pushed, "abc123");
        await gitStatusStore.RefreshAsync();

        Assert.True(viewModel.GitNeedsAttention);
    }

    /// <summary>
    /// Re-reads the VERSION file from the newly active repository once a refresh completes, proving
    /// <see cref="BuildPageViewModel.RepositoryVersion"/> doesn't keep showing the previous repository's
    /// version after a repository change that this page did not itself request (for example, one made
    /// from Settings).
    /// </summary>
    [Fact]
    public async Task RepositoryVersionReflectsTheNewRepositoryAfterARefreshMadeElsewhere()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryARoot = Path.Combine(temporaryDirectory.Path, "repo-a");
        string repositoryBRoot = Path.Combine(temporaryDirectory.Path, "repo-b");
        Directory.CreateDirectory(repositoryARoot);
        Directory.CreateDirectory(repositoryBRoot);
        File.WriteAllText(Path.Combine(repositoryARoot, "VERSION"), "1.0.0");
        File.WriteAllText(Path.Combine(repositoryBRoot, "VERSION"), "2.0.0");
        var repositoryContext = new RepositoryContext(repositoryARoot);
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var environmentStore = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, new OutputPathContext(null));
        var viewModel = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new FakeAdapterHostBuildCoordinator(),
            new FakeBuildHistoryStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new FakeBuildOutputOwnershipGuard(),
            new LogViewModel(_ => { }),
            stage => new BuildStageViewModel(stage),
            new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true));
        await viewModel.InitializeAsync();
        Assert.Equal("1.0.0", viewModel.RepositoryVersion);

        repositoryContext.SetRepositoryRoot(repositoryBRoot);
        await environmentStore.RefreshAsync();

        Assert.Equal("2.0.0", viewModel.RepositoryVersion);
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

    /// <summary>
    /// Completes build lifecycle teardown -- IsBuilding and IsCancelling both false, Cancel disabled --
    /// even when writing the finished build to local history throws, and still reports the build's own
    /// real outcome rather than silently reporting it differently because persistence failed.
    /// </summary>
    [Fact]
    public async Task BuildLifecycleTeardownCompletesWhenRecordingHistoryFails()
    {
        var buildHistoryStore = new FakeBuildHistoryStore { ThrownExceptionOnAdd = new IOException("disk full") };
        var viewModel = BuildViewModel(buildHistoryStore: buildHistoryStore);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.IsBuilding);
        Assert.False(viewModel.IsCancelling);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal(BuildHistoryResult.Succeeded, viewModel.LastOutcome);
        Assert.Empty(viewModel.RecentBuilds);
    }

    /// <summary>
    /// Reports the real build failure -- not the unrelated history-persistence failure -- when both
    /// happen in the same build.
    /// </summary>
    [Fact]
    public async Task ReportsTheRealBuildFailureWhenRecordingHistoryAlsoFails()
    {
        var buildHistoryStore = new FakeBuildHistoryStore { ThrownExceptionOnAdd = new IOException("disk full") };
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("Adapter build failed with exit code 1.") };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, buildHistoryStore: buildHistoryStore);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.IsBuilding);
        Assert.Equal(BuildHistoryResult.Failed, viewModel.LastOutcome);
        Assert.Equal("Adapter build failed with exit code 1.", viewModel.LastOutcomeMessage);
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

    /// <summary>Reports the Adapter and Host summary rows for each profile: Debug builds Debug, Beta shares Release's Adapter/Host configuration.</summary>
    /// <param name="profile">The profile to select.</param>
    /// <param name="expectedAdapterSummaryText">The expected Adapter row text.</param>
    /// <param name="expectedHostSummaryText">The expected Host row text.</param>
    [Theory]
    [InlineData(BuildProfile.Debug, "Debug x64", "self-contained win-x64 (Debug)")]
    [InlineData(BuildProfile.Beta, "Release x64", "self-contained win-x64 (Release)")]
    [InlineData(BuildProfile.Release, "Release x64", "self-contained win-x64 (Release)")]
    public void SummaryRowsReflectTheSelectedProfile(BuildProfile profile, string expectedAdapterSummaryText, string expectedHostSummaryText)
    {
        var viewModel = BuildViewModel();

        viewModel.SelectedProfile = profile;

        Assert.Equal(expectedAdapterSummaryText, viewModel.AdapterSummaryText);
        Assert.Equal(expectedHostSummaryText, viewModel.HostSummaryText);
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

    /// <summary>Applies reported stage transitions to their matching segments and updates the honest progress count.</summary>
    [Fact]
    public async Task BuildCommandAppliesReportedStageTransitionsToTheMatchingSegments()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator
        {
            StageEventsToEmit =
            [
                new BuildStageEvent(BuildStage.ValidateRepository, BuildStageStatus.Running),
                new BuildStageEvent(BuildStage.ValidateRepository, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(1)),
                new BuildStageEvent(BuildStage.ConfigureAdapter, BuildStageStatus.Running),
                new BuildStageEvent(BuildStage.ConfigureAdapter, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(2)),
            ],
        };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildStageStatus.Succeeded, viewModel.Stages[0].Status);
        Assert.Equal(BuildStageStatus.Succeeded, viewModel.Stages[1].Status);
        Assert.Equal(BuildStageStatus.Pending, viewModel.Stages[2].Status);
        Assert.Equal(2, viewModel.CompletedStageCount);
        Assert.Equal("Stage 2 of 8", viewModel.StageProgressText);
    }

    /// <summary>Marks a failed stage as Failed rather than Succeeded, and does not count it toward the completed total.</summary>
    [Fact]
    public async Task BuildCommandMarksAFailedStageWithoutCountingItAsCompleted()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator
        {
            StageEventsToEmit =
            [
                new BuildStageEvent(BuildStage.ValidateRepository, BuildStageStatus.Running),
                new BuildStageEvent(BuildStage.ValidateRepository, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(1)),
                new BuildStageEvent(BuildStage.ConfigureAdapter, BuildStageStatus.Running),
                new BuildStageEvent(BuildStage.ConfigureAdapter, BuildStageStatus.Failed, TimeSpan.FromSeconds(1)),
            ],
            ThrownException = new InvalidOperationException("the adapter build failed"),
        };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildStageStatus.Succeeded, viewModel.Stages[0].Status);
        Assert.Equal(BuildStageStatus.Failed, viewModel.Stages[1].Status);
        Assert.Equal(1, viewModel.CompletedStageCount);
    }

    /// <summary>Resets every stage back to Pending at the start of a new build, discarding the previous run's progress.</summary>
    [Fact]
    public async Task StartingANewBuildResetsStagesFromThePreviousRun()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator
        {
            StageEventsToEmit = [new BuildStageEvent(BuildStage.ValidateRepository, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(1))],
        };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        Assert.Equal(1, viewModel.CompletedStageCount);

        buildCoordinator.StageEventsToEmit = [];
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildStageStatus.Pending, viewModel.Stages[0].Status);
        Assert.Equal(0, viewModel.CompletedStageCount);
    }

    /// <summary>Reports zero completed stages and "Stage 0 of 8" before any build has run.</summary>
    [Fact]
    public void StageProgressStartsAtZeroOfEightBeforeAnyBuild()
    {
        var viewModel = BuildViewModel();

        Assert.Equal(0, viewModel.CompletedStageCount);
        Assert.Equal("Stage 0 of 8", viewModel.StageProgressText);
    }

    /// <summary>Reports no result banner before any build has run.</summary>
    [Fact]
    public void ResultBannerIsAbsentBeforeAnyBuild()
    {
        var viewModel = BuildViewModel();

        Assert.False(viewModel.HasResult);
        Assert.Null(viewModel.ResultBannerText);
    }

    /// <summary>Reports all 8 stages completed and "Stage 8 of 8" once every stage has succeeded.</summary>
    [Fact]
    public async Task StageProgressReachesEightOfEightWhenEveryStageSucceeds()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator
        {
            StageEventsToEmit = Enum.GetValues<BuildStage>()
                .Select(stage => new BuildStageEvent(stage, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(1)))
                .ToList(),
        };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(8, viewModel.CompletedStageCount);
        Assert.Equal("Stage 8 of 8", viewModel.StageProgressText);
    }

    /// <summary>Ignores a stage transition for a value outside the tracked pipeline stages instead of throwing.</summary>
    [Fact]
    public async Task BuildCommandIgnoresATransitionForAnUnrecognizedStage()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator
        {
            StageEventsToEmit = [new BuildStageEvent((BuildStage)(-1), BuildStageStatus.Succeeded, TimeSpan.FromSeconds(1))],
        };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(0, viewModel.CompletedStageCount);
    }

    /// <summary>Forwards each output line reported by the coordinator into the log panel, in order.</summary>
    [Fact]
    public async Task BuildCommandAppendsCoordinatorOutputToTheLog()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { OutputLinesToEmit = ["Building the DovahLink Adapter...", "Packaging..."] };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(["Building the DovahLink Adapter...", "Packaging..."], viewModel.Log.Lines);
    }

    /// <summary>Keeps the log populated through a Building-to-Cancelling-to-Cancelled transition.</summary>
    [Fact]
    public async Task LogSurvivesACancellingToCancelledTransition()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator
        {
            OutputLinesToEmit = ["Building the DovahLink Adapter..."],
            WaitForCancellation = true,
        };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);

        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Cancelled, viewModel.LastOutcome);
        Assert.Equal(["Building the DovahLink Adapter..."], viewModel.Log.Lines);
    }

    /// <summary>Clears the previous run's log when a new build starts, so runs are not mixed together.</summary>
    [Fact]
    public async Task StartingANewBuildClearsTheLogFromThePreviousRun()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { OutputLinesToEmit = ["first run"] };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        Assert.Equal(["first run"], viewModel.Log.Lines);

        buildCoordinator.OutputLinesToEmit = ["second run"];
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(["second run"], viewModel.Log.Lines);
    }

    /// <summary>Computes the real archive's SHA-256 and reports its path and a success banner on success.</summary>
    [Fact]
    public async Task BuildCommandComputesTheArchiveShaAndBannerOnSuccess()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        string expectedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath))).ToLowerInvariant();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.True(viewModel.HasResult);
        Assert.Equal("Build succeeded.", viewModel.ResultBannerText);
        Assert.True(viewModel.HasArchivePath);
        Assert.Equal(archivePath, viewModel.ArchivePath);
        Assert.Equal(expectedSha256, viewModel.ArchiveSha256);
    }

    /// <summary>Reports no archive path, hash, or entries, and the failure banner, when the build fails.</summary>
    [Fact]
    public async Task BuildCommandReportsNoArchiveAndTheFailureBannerOnFailure()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("the adapter build failed") };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal("Build failed.", viewModel.ResultBannerText);
        Assert.False(viewModel.HasArchivePath);
        Assert.Null(viewModel.ArchivePath);
        Assert.Null(viewModel.ArchiveSha256);
    }

    /// <summary>Reports no archive path or hash, and the cancelled banner, when the build is cancelled.</summary>
    [Fact]
    public async Task CancelCommandReportsNoArchiveAndTheCancelledBanner()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);

        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal("Build cancelled.", viewModel.ResultBannerText);
        Assert.False(viewModel.HasArchivePath);
    }

    /// <summary>Reads the real ZIP's entries and toggles the display on, off, and back on again.</summary>
    [Fact]
    public async Task ViewArchiveContentsCommandTogglesTheRealZipEntries()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"), ("DovahLinkAdapter.dll", "binary"));
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        var expectedEntries = new[] { "manifest.json", "DovahLinkAdapter.dll" }.OrderBy(name => name, StringComparer.Ordinal);

        viewModel.ViewArchiveContentsCommand.Execute(null);

        Assert.True(viewModel.IsShowingArchiveContents);
        Assert.Equal(expectedEntries, viewModel.ArchiveEntries.OrderBy(name => name, StringComparer.Ordinal));

        viewModel.ViewArchiveContentsCommand.Execute(null);

        Assert.False(viewModel.IsShowingArchiveContents);

        viewModel.ViewArchiveContentsCommand.Execute(null);

        Assert.True(viewModel.IsShowingArchiveContents);
        Assert.Equal(expectedEntries, viewModel.ArchiveEntries.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>Does nothing when View contents is executed directly with no archive produced yet.</summary>
    [Fact]
    public void ViewArchiveContentsCommandDoesNothingWithoutAnArchive()
    {
        var viewModel = BuildViewModel();

        viewModel.ViewArchiveContentsCommand.Execute(null);

        Assert.False(viewModel.IsShowingArchiveContents);
        Assert.Empty(viewModel.ArchiveEntries);
    }

    /// <summary>
    /// Does nothing rather than crashing when the produced archive is no longer a readable ZIP -- for
    /// example moved, deleted, or overwritten by something else in the time since the build finished.
    /// </summary>
    [Fact]
    public async Task ViewArchiveContentsCommandDoesNothingWhenTheArchiveIsNotAReadableZip()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string corruptArchivePath = Path.Combine(temporaryDirectory.Path, "archive.zip");
        File.WriteAllText(corruptArchivePath, "not a zip file");
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(corruptArchivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        viewModel.ViewArchiveContentsCommand.Execute(null);

        Assert.False(viewModel.IsShowingArchiveContents);
        Assert.Empty(viewModel.ArchiveEntries);
    }

    /// <summary>Clears the previous run's archive path, hash, and entries when a new build starts.</summary>
    [Fact]
    public async Task StartingANewBuildClearsThePreviousRunsArchiveState()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        viewModel.ViewArchiveContentsCommand.Execute(null);
        Assert.True(viewModel.HasArchivePath);

        buildCoordinator.ThrownException = new InvalidOperationException("the adapter build failed");
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.HasArchivePath);
        Assert.Null(viewModel.ArchiveSha256);
        Assert.False(viewModel.IsShowingArchiveContents);
        Assert.Empty(viewModel.ArchiveEntries);
    }

    /// <summary>Clears the previous run's failure message once a new build succeeds.</summary>
    [Fact]
    public async Task StartingANewBuildClearsThePreviousRunsFailureMessage()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("first failure") };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        Assert.Equal("first failure", viewModel.LastOutcomeMessage);

        buildCoordinator.ThrownException = null;
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Null(viewModel.LastOutcomeMessage);
        Assert.Equal(BuildHistoryResult.Succeeded, viewModel.LastOutcome);
    }

    /// <summary>Seeds the log panel's auto-scroll from the runtime setting at construction.</summary>
    [Fact]
    public void ConstructorSeedsLogAutoScrollFromTheRuntimeSetting()
    {
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: false);

        var viewModel = BuildViewModel(runtimeBuildSettingsContext: runtimeBuildSettingsContext);

        Assert.False(viewModel.Log.AutoScroll);
    }

    /// <summary>Opens the archive's containing folder after a successful build when the setting is enabled.</summary>
    [Fact]
    public async Task BuildCommandOpensTheOutputFolderOnSuccessWhenEnabled()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var openedFolders = new List<string>();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, runtimeBuildSettingsContext: runtimeBuildSettingsContext, openOutputFolder: openedFolders.Add);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal([Path.GetDirectoryName(archivePath)!], openedFolders);
    }

    /// <summary>Does not open the output folder after a successful build when the setting is disabled.</summary>
    [Fact]
    public async Task BuildCommandDoesNotOpenTheOutputFolderOnSuccessWhenDisabled()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: false, autoScrollLogs: true);
        var openedFolders = new List<string>();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, runtimeBuildSettingsContext: runtimeBuildSettingsContext, openOutputFolder: openedFolders.Add);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Empty(openedFolders);
    }

    /// <summary>Never opens the output folder for a failed build, even when the setting is enabled.</summary>
    [Fact]
    public async Task BuildCommandDoesNotOpenTheOutputFolderOnFailure()
    {
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var openedFolders = new List<string>();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("the adapter build failed") };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, runtimeBuildSettingsContext: runtimeBuildSettingsContext, openOutputFolder: openedFolders.Add);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Empty(openedFolders);
    }

    /// <summary>Never opens the output folder for a cancelled build, even when the setting is enabled.</summary>
    [Fact]
    public async Task CancelCommandDoesNotOpenTheOutputFolder()
    {
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var openedFolders = new List<string>();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, runtimeBuildSettingsContext: runtimeBuildSettingsContext, openOutputFolder: openedFolders.Add);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);

        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Empty(openedFolders);
    }

    /// <summary>A convenience-action failure opening the output folder does not change the build's own reported outcome.</summary>
    [Fact]
    public async Task BuildCommandStillReportsSuccessWhenOpeningTheOutputFolderFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(
            buildCoordinator: buildCoordinator,
            runtimeBuildSettingsContext: runtimeBuildSettingsContext,
            openOutputFolder: _ => throw new InvalidOperationException("explorer.exe could not be started"));
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Succeeded, viewModel.LastOutcome);
        Assert.True(viewModel.HasArchivePath);
    }

    /// <summary>Loads the store's existing entries as <see cref="BuildPageViewModel.RecentBuilds"/> on construction.</summary>
    [Fact]
    public void ConstructorLoadsRecentBuildsFromTheStore()
    {
        var buildHistoryStore = new FakeBuildHistoryStore();
        buildHistoryStore.Add(Fixtures.BuildBuildHistoryEntry());

        var viewModel = BuildViewModel(buildHistoryStore: buildHistoryStore);

        Assert.Single(viewModel.RecentBuilds);
    }

    /// <summary>Records a successful build's outcome, version, profile, archive path, and hash.</summary>
    [Fact]
    public async Task BuildCommandRecordsASuccessfulBuildInHistory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        var buildHistoryStore = new FakeBuildHistoryStore();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, buildHistoryStore: buildHistoryStore);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        BuildHistoryEntry recorded = Assert.Single(buildHistoryStore.GetRecent());
        Assert.Equal(BuildHistoryResult.Succeeded, recorded.Result);
        Assert.Equal("Release", recorded.Profile);
        Assert.Equal(archivePath, recorded.ArtifactPath);
        Assert.NotNull(recorded.Sha256);
        Assert.Null(recorded.FailedStage);
        Assert.Same(recorded, viewModel.RecentBuilds[0]);
    }

    /// <summary>Falls back to "unknown" instead of throwing when the repository's VERSION file cannot be read.</summary>
    [Fact]
    public async Task BuildCommandRecordsUnknownVersionWhenTheVersionFileCannotBeRead()
    {
        var buildHistoryStore = new FakeBuildHistoryStore();
        var viewModel = BuildViewModel(buildHistoryStore: buildHistoryStore);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal("unknown", Assert.Single(buildHistoryStore.GetRecent()).Version);
    }

    /// <summary>
    /// Records the repository version and profile the build actually started with, even when the
    /// active repository and selected profile both change while that build is still running --
    /// proving <see cref="BuildPageViewModel.RunBuildAsync"/>'s snapshot, not the live
    /// <c>repositoryContext</c>/<c>SelectedProfile</c>, is what history is drawn from.
    /// </summary>
    [Fact]
    public async Task BuildCommandRecordsTheStartingRepositoryVersionAndProfileEvenWhenBothChangeMidBuild()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryARoot = Path.Combine(temporaryDirectory.Path, "repo-a");
        string repositoryBRoot = Path.Combine(temporaryDirectory.Path, "repo-b");
        Directory.CreateDirectory(repositoryARoot);
        Directory.CreateDirectory(repositoryBRoot);
        File.WriteAllText(Path.Combine(repositoryARoot, "VERSION"), "1.0.0");
        File.WriteAllText(Path.Combine(repositoryBRoot, "VERSION"), "2.0.0");
        var repositoryContext = new RepositoryContext(repositoryARoot);
        var buildHistoryStore = new FakeBuildHistoryStore();
        var viewModel = BuildViewModel(repositoryContext: repositoryContext, buildHistoryStore: buildHistoryStore);
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = BuildProfile.Release;

        viewModel.BuildCommand.Execute(null);
        repositoryContext.SetRepositoryRoot(repositoryBRoot);
        viewModel.SelectedProfile = BuildProfile.Debug;
        await viewModel.RunningBuildTask!;

        BuildHistoryEntry recorded = Assert.Single(buildHistoryStore.GetRecent());
        Assert.Equal("1.0.0", recorded.Version);
        Assert.Equal("Release", recorded.Profile);
    }

    /// <summary>Records a failed build with the stage that was running when it failed.</summary>
    [Fact]
    public async Task BuildCommandRecordsAFailedBuildWithItsFailedStage()
    {
        var buildHistoryStore = new FakeBuildHistoryStore();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator
        {
            StageEventsToEmit =
            [
                new BuildStageEvent(BuildStage.ValidateRepository, BuildStageStatus.Running),
                new BuildStageEvent(BuildStage.ValidateRepository, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(1)),
                new BuildStageEvent(BuildStage.ConfigureAdapter, BuildStageStatus.Running),
                new BuildStageEvent(BuildStage.ConfigureAdapter, BuildStageStatus.Failed, TimeSpan.FromSeconds(1)),
            ],
            ThrownException = new InvalidOperationException("the adapter build failed"),
        };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, buildHistoryStore: buildHistoryStore);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        BuildHistoryEntry recorded = Assert.Single(buildHistoryStore.GetRecent());
        Assert.Equal(BuildHistoryResult.Failed, recorded.Result);
        Assert.Equal("Configure Adapter", recorded.FailedStage);
        Assert.Null(recorded.ArtifactPath);
        Assert.Null(recorded.Sha256);
    }

    /// <summary>Records a cancelled build with no failed stage and no archive.</summary>
    [Fact]
    public async Task CancelCommandRecordsACancelledBuild()
    {
        var buildHistoryStore = new FakeBuildHistoryStore();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, buildHistoryStore: buildHistoryStore);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);

        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        BuildHistoryEntry recorded = Assert.Single(buildHistoryStore.GetRecent());
        Assert.Equal(BuildHistoryResult.Cancelled, recorded.Result);
        Assert.Null(recorded.FailedStage);
        Assert.Null(recorded.ArtifactPath);
    }

    /// <summary>
    /// Leaves no stage shown as Running once a real, cancelled build finishes -- exercising the real
    /// <see cref="AdapterHostBuildCoordinator"/>'s own cancellation-to-stage-event reporting through
    /// the page's bound <see cref="BuildPageViewModel.Stages"/>, not just a fake coordinator that
    /// never emits realistic stage events at all.
    /// </summary>
    [Fact]
    public async Task CancellingARealBuildLeavesNoStageShownAsRunning()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Fixtures.CreateAdapterHostBuildInputs(temporaryDirectory.Path);
        var buildCoordinator = new AdapterHostBuildCoordinator(
            new CancellingCommandRunner(),
            () => Fixtures.BuildVisualStudioToolchain(temporaryDirectory.Path),
            () => Fixtures.BuildPapyrusToolchain(temporaryDirectory.Path),
            new FakeBuildOutputOwnershipGuard());
        var repositoryContext = new RepositoryContext(temporaryDirectory.Path);
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var viewModel = new BuildPageViewModel(
            new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext, new OutputPathContext(null)),
            gitStatusStore,
            buildCoordinator,
            new FakeBuildHistoryStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new FakeBuildOutputOwnershipGuard(),
            new LogViewModel(_ => { }),
            stage => new BuildStageViewModel(stage),
            new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true));
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Cancelled, viewModel.LastOutcome);
        Assert.DoesNotContain(viewModel.Stages, stage => stage.Status == BuildStageStatus.Running);
    }

    /// <summary>Returns to the idle form and clears the previous result after a succeeded build, without starting a new one.</summary>
    [Fact]
    public async Task NewBuildCommandResetsToIdleFormFromASucceededResult()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        viewModel.NewBuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        AssertResetToIdleForm(viewModel, buildCoordinator, expectedCallCount: 1);
    }

    /// <summary>
    /// Re-seeds Log.AutoScroll from the current AutoScrollLogs setting at the next build, so a
    /// Settings-page change takes effect without waiting for a restart -- while still letting the
    /// user override it live from the log panel's own checkbox during a single build (see
    /// <see cref="BuildPageViewModel.ResetBuildResultState"/>'s own doc for why this seeds only at a
    /// build boundary rather than tracking the setting continuously).
    /// </summary>
    [Fact]
    public async Task NewBuildCommandReSeedsAutoScrollFromTheCurrentSetting()
    {
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, runtimeBuildSettingsContext: runtimeBuildSettingsContext);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        Assert.True(viewModel.Log.AutoScroll);

        runtimeBuildSettingsContext.SetAutoScrollLogs(false);
        viewModel.NewBuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.Log.AutoScroll);
    }

    /// <summary>
    /// Re-seeds Log.AutoScroll at the start of a fresh build too, not only through NewBuildCommand --
    /// proven by changing the setting before the very first build, since the constructor's own initial
    /// seed would otherwise make this indistinguishable from RunBuildAsync never having re-seeded at all.
    /// </summary>
    [Fact]
    public async Task BuildCommandReSeedsAutoScrollFromTheCurrentSettingAtStartup()
    {
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var viewModel = BuildViewModel(runtimeBuildSettingsContext: runtimeBuildSettingsContext);
        runtimeBuildSettingsContext.SetAutoScrollLogs(false);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.Log.AutoScroll);
    }

    /// <summary>
    /// Leaves a live, mid-session manual toggle of Log.AutoScroll untouched by an environment refresh
    /// that is not itself a build boundary -- re-seeding happens only where ResetBuildResultState is
    /// actually called (a build starting, or NewBuildCommand), never merely because preflight or git
    /// status refreshed.
    /// </summary>
    [Fact]
    public async Task ALiveAutoScrollToggleSurvivesAnEnvironmentRefreshThatIsNotABuildBoundary()
    {
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var viewModel = BuildViewModel(runtimeBuildSettingsContext: runtimeBuildSettingsContext);
        await viewModel.InitializeAsync();
        viewModel.Log.AutoScroll = false;

        await viewModel.InitializeAsync();

        Assert.False(viewModel.Log.AutoScroll);
    }

    /// <summary>
    /// Composition test proving disk is no longer read as runtime authority for AutoScrollLogs: with
    /// <see cref="SettingsPageViewModel"/> and this page sharing one real
    /// <see cref="RuntimeBuildSettingsContext"/> instance (as they do in the real composition root),
    /// a Settings-page change whose persistence fails still reaches this page's next build -- even
    /// though the persisted settings on "disk" (a fake store here) disagree with it the whole time.
    /// </summary>
    [Fact]
    public async Task BuildPageReflectsTheRuntimeContextsCurrentAutoScrollValueEvenWhenDiskDisagrees()
    {
        var settingsStore = new FakeSettingsStore { ThrownExceptionOnSave = new IOException("disk full") };
        var runtimeBuildSettingsContext = new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true);
        var settingsPage = new SettingsPageViewModel(
            settingsStore, new FakeFolderPicker(), _ => { }, @"C:\repo", new RepositoryContext(@"C:\repo"), new OutputPathContext(null), runtimeBuildSettingsContext);
        var viewModel = BuildViewModel(runtimeBuildSettingsContext: runtimeBuildSettingsContext);
        await viewModel.InitializeAsync();

        settingsPage.AutoScrollLogs = false;
        viewModel.NewBuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.Log.AutoScroll);
        Assert.True(settingsStore.Settings.AutoScrollLogs);
    }

    /// <summary>Returns to the idle form and clears the previous result after a failed build, without starting a new one.</summary>
    [Fact]
    public async Task NewBuildCommandResetsToIdleFormFromAFailedResult()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("the adapter build failed") };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        viewModel.NewBuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        AssertResetToIdleForm(viewModel, buildCoordinator, expectedCallCount: 1);
    }

    /// <summary>Returns to the idle form and clears the previous result after a cancelled build, without starting a new one.</summary>
    [Fact]
    public async Task NewBuildCommandResetsToIdleFormFromACancelledResult()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        viewModel.NewBuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        AssertResetToIdleForm(viewModel, buildCoordinator, expectedCallCount: 1);
    }

    /// <summary>Leaves a note the user has typed for the next build untouched, unlike an actual build starting, which consumes it.</summary>
    [Fact]
    public async Task NewBuildCommandLeavesBuildNoteUntouched()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        viewModel.BuildNote = "for the next attempt";

        viewModel.NewBuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal("for the next attempt", viewModel.BuildNote);
    }

    /// <summary>Asserts the idle form is showing with every previous result, archive, stage, and log line cleared.</summary>
    /// <param name="viewModel">The view model under test.</param>
    /// <param name="buildCoordinator">The fake build coordinator, to assert no new build was started.</param>
    /// <param name="expectedCallCount">The build coordinator's expected call count, unchanged by returning to idle.</param>
    private static void AssertResetToIdleForm(BuildPageViewModel viewModel, FakeAdapterHostBuildCoordinator buildCoordinator, int expectedCallCount)
    {
        Assert.True(viewModel.ShowIdleForm);
        Assert.Null(viewModel.LastOutcome);
        Assert.Null(viewModel.LastOutcomeMessage);
        Assert.False(viewModel.HasArchivePath);
        Assert.Null(viewModel.ArchivePath);
        Assert.Empty(viewModel.Log.Lines);
        Assert.Equal(0, viewModel.CompletedStageCount);
        Assert.Equal(expectedCallCount, buildCoordinator.CallCount);
    }

    /// <summary>
    /// Refreshes preflight and git status in the background when returning to the idle form, so a
    /// newly-failing check is reflected before the user presses Build again, without itself starting
    /// a build.
    /// </summary>
    [Fact]
    public async Task NewBuildCommandRefreshesPreflightAndGitStatusInTheBackground()
    {
        var preflightService = new FakePreflightService();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(preflightService: preflightService, buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        Assert.True(viewModel.CanBuild);

        preflightService.Results = [new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH")];
        viewModel.NewBuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.CanBuild);
        Assert.Contains("CMake", viewModel.BuildBlockedReason!);
        Assert.Equal(1, buildCoordinator.CallCount);
    }

    /// <summary>
    /// Does nothing when New build is executed directly while a build is already running, protecting
    /// the in-flight build's own log/stage/archive state from being reset out from under it.
    /// </summary>
    [Fact]
    public async Task NewBuildCommandDoesNothingWhileABuildIsRunning()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        Task originalBuildTask = viewModel.RunningBuildTask!;

        Assert.False(viewModel.NewBuildCommand.CanExecute(null));
        viewModel.NewBuildCommand.Execute(null);

        Assert.Same(originalBuildTask, viewModel.RunningBuildTask);
        Assert.True(viewModel.IsBuilding);

        viewModel.CancelCommand.Execute(null);
        await originalBuildTask;
    }

    /// <summary>Reports no attention needed before any check has loaded.</summary>
    [Fact]
    public void GitNeedsAttentionIsFalseBeforeInitialization()
    {
        var viewModel = BuildViewModel();

        Assert.False(viewModel.GitNeedsAttention);
    }

    /// <summary>Reports no attention needed when git status cannot be determined, since that failure is already surfaced via <see cref="BuildPageViewModel.BuildBlockedReason"/>.</summary>
    [Fact]
    public async Task GitNeedsAttentionIsFalseWhenGitStatusCannotBeDetermined()
    {
        var gitStatusService = new FakeGitStatusService { ThrownException = new InvalidOperationException("not a git repository") };
        var viewModel = BuildViewModel(gitStatusService: gitStatusService);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.GitNeedsAttention);
    }

    /// <summary>Reports whether attention is needed for every working-tree/remote-sync combination: true for a dirty tree or unpushed commits, false otherwise.</summary>
    /// <param name="workingTreeState">The reported working tree state.</param>
    /// <param name="remoteSyncState">The reported remote sync state.</param>
    /// <param name="expectedNeedsAttention">Whether this combination is expected to need attention.</param>
    [Theory]
    [InlineData(WorkingTreeState.Dirty, RemoteSyncState.Pushed, true)]
    [InlineData(WorkingTreeState.Dirty, RemoteSyncState.NotPushed, true)]
    [InlineData(WorkingTreeState.Dirty, RemoteSyncState.CouldNotVerify, true)]
    [InlineData(WorkingTreeState.Clean, RemoteSyncState.NotPushed, true)]
    [InlineData(WorkingTreeState.Clean, RemoteSyncState.CouldNotVerify, false)]
    [InlineData(WorkingTreeState.Clean, RemoteSyncState.Pushed, false)]
    public async Task GitNeedsAttentionReflectsEachStateCombination(
        WorkingTreeState workingTreeState, RemoteSyncState remoteSyncState, bool expectedNeedsAttention)
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", workingTreeState, remoteSyncState, "abc123"),
        };
        var viewModel = BuildViewModel(gitStatusService: gitStatusService);

        await viewModel.InitializeAsync();

        Assert.Equal(expectedNeedsAttention, viewModel.GitNeedsAttention);
    }

    /// <summary>Exposes the current branch for the footer status strip.</summary>
    [Fact]
    public async Task InitializeAsyncExposesBranchForTheFooter()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("feature/x", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abcdef1234567890"),
        };
        var viewModel = BuildViewModel(gitStatusService: gitStatusService);

        await viewModel.InitializeAsync();

        Assert.Equal("feature/x", viewModel.GitBranch);
    }

    /// <summary>Reports "Environment incomplete" before preflight and git status have finished loading.</summary>
    [Fact]
    public void FooterStatusTextIsEnvironmentIncompleteBeforeInitialization()
    {
        var viewModel = BuildViewModel();

        Assert.Equal("Environment incomplete", viewModel.FooterStatusText);
    }

    /// <summary>Reports "Ready" once every required check passes and no build has run yet.</summary>
    [Fact]
    public async Task FooterStatusTextIsReadyWhenEverythingPasses()
    {
        var viewModel = BuildViewModel();

        await viewModel.InitializeAsync();

        Assert.Equal("Ready", viewModel.FooterStatusText);
    }

    /// <summary>Reports "Environment incomplete" when a required check is missing.</summary>
    [Fact]
    public async Task FooterStatusTextIsEnvironmentIncompleteWhenARequiredCheckIsMissing()
    {
        var preflightService = new FakePreflightService
        {
            Results = [new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH")],
        };
        var viewModel = BuildViewModel(preflightService: preflightService);

        await viewModel.InitializeAsync();

        Assert.Equal("Environment incomplete", viewModel.FooterStatusText);
    }

    /// <summary>Reports "Building" while a build is running, regardless of the environment or last outcome.</summary>
    [Fact]
    public async Task FooterStatusTextIsBuildingDuringABuild()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);

        Assert.Equal("Building", viewModel.FooterStatusText);

        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;
    }

    /// <summary>Reports "Complete" after a successful build.</summary>
    [Fact]
    public async Task FooterStatusTextIsCompleteAfterASuccessfulBuild()
    {
        var viewModel = BuildViewModel();
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal("Complete", viewModel.FooterStatusText);
    }

    /// <summary>Reports "Failed" after a failed build.</summary>
    [Fact]
    public async Task FooterStatusTextIsFailedAfterAFailedBuild()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("the adapter build failed") };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal("Failed", viewModel.FooterStatusText);
    }

    /// <summary>Reports "Cancelled" after a cancelled build.</summary>
    [Fact]
    public async Task FooterStatusTextIsCancelledAfterACancelledBuild()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);

        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal("Cancelled", viewModel.FooterStatusText);
    }

    /// <summary>
    /// Keeps showing the last build's outcome rather than falling back to "Environment incomplete"
    /// when a required check goes missing only after that build already finished (for example, a tool
    /// was uninstalled between builds).
    /// </summary>
    [Fact]
    public async Task FooterStatusTextStaysCompleteWhenARequiredCheckBecomesMissingAfterTheBuildFinished()
    {
        var preflightService = new FakePreflightService();
        var viewModel = BuildViewModel(preflightService: preflightService);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        Assert.Equal("Complete", viewModel.FooterStatusText);

        preflightService.Results = [new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH")];
        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasBuildBlockedReason);
        Assert.Equal("Complete", viewModel.FooterStatusText);
    }

    /// <summary>
    /// Reports "Checking" rather than "Environment incomplete" while a refresh is still in flight, so a
    /// repository change or startup check-in-progress isn't misread as an already-failed check.
    /// </summary>
    [Fact]
    public async Task FooterStatusTextIsCheckingWhileTheEnvironmentIsRefreshing()
    {
        var preflightService = new FakePreflightService();
        var repositoryContext = new RepositoryContext(@"C:\repo-a");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));
        var viewModel = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new FakeAdapterHostBuildCoordinator(),
            new FakeBuildHistoryStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new FakeBuildOutputOwnershipGuard(),
            new LogViewModel(_ => { }),
            stage => new BuildStageViewModel(stage),
            new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true));
        await viewModel.InitializeAsync();
        var pauseSignal = new TaskCompletionSource();
        preflightService.PauseSignal = pauseSignal;

        Task pendingRefresh = environmentStore.RefreshAsync();

        Assert.Equal("Checking", viewModel.FooterStatusText);

        pauseSignal.SetResult();
        await pendingRefresh;
    }

    /// <summary>Loads the full preflight results, not just the summarized blocked reason, for diagnostics.</summary>
    [Fact]
    public async Task InitializeAsyncLoadsPreflightResults()
    {
        var viewModel = BuildViewModel();

        await viewModel.InitializeAsync();

        Assert.Equal(8, viewModel.PreflightResults.Count);
    }

    /// <summary>Reflects the shared environment store's actual mixed results, not just their count, through the same refresh path other pages also trigger.</summary>
    [Fact]
    public async Task InitializeAsyncLoadsPreflightResultsReflectingAMixOfAvailabilityValues()
    {
        var preflightService = new FakePreflightService
        {
            Results =
            [
                new ToolchainCheckResult("CMake", ToolchainAvailability.Missing, null, "not on PATH"),
                new ToolchainCheckResult("Python", ToolchainAvailability.CouldNotCheck, null, "timed out"),
            ],
        };
        var viewModel = BuildViewModel(preflightService: preflightService);

        await viewModel.InitializeAsync();

        Assert.Equal(ToolchainAvailability.Missing, viewModel.PreflightResults[0].Availability);
        Assert.Equal(ToolchainAvailability.CouldNotCheck, viewModel.PreflightResults[1].Availability);
    }

    /// <summary>Reports IsFailed only for a failed build, not for success or cancellation.</summary>
    [Fact]
    public async Task IsFailedReflectsOnlyAFailedOutcome()
    {
        var viewModel = BuildViewModel();
        await viewModel.InitializeAsync();
        Assert.False(viewModel.IsFailed);

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;
        Assert.False(viewModel.IsFailed);

        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("the adapter build failed") };
        var failedViewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await failedViewModel.InitializeAsync();
        failedViewModel.BuildCommand.Execute(null);
        await failedViewModel.RunningBuildTask!;

        Assert.True(failedViewModel.IsFailed);
    }

    /// <summary>Copies a diagnostics report reflecting the current preflight results, git status, and last build outcome to the clipboard.</summary>
    [Fact]
    public async Task CopyDiagnosticsCommandWritesTheCurrentStateToTheClipboard()
    {
        var gitStatusService = new FakeGitStatusService
        {
            Status = new GitSourceStatus("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123"),
        };
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("the adapter build failed") };
        string? copiedText = null;
        var viewModel = BuildViewModel(gitStatusService: gitStatusService, buildCoordinator: buildCoordinator, setClipboardText: text => copiedText = text);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        viewModel.CopyDiagnosticsCommand.Execute(null);

        Assert.NotNull(copiedText);
        Assert.Contains("Repository: Found", copiedText);
        Assert.Contains("Branch: main", copiedText);
        Assert.Contains("Outcome: Failed", copiedText);
        Assert.Contains("the adapter build failed", copiedText);
    }

    /// <summary>
    /// Swallows a transient clipboard-access failure (another process briefly holding the clipboard, a
    /// real and fairly common Windows condition) rather than crashing the application or changing any
    /// reported state.
    /// </summary>
    [Fact]
    public async Task CopyDiagnosticsCommandSwallowsAClipboardAccessFailure()
    {
        var viewModel = BuildViewModel(setClipboardText: _ => throw new ExternalException("clipboard busy"));
        await viewModel.InitializeAsync();

        Exception? thrown = Record.Exception(() => viewModel.CopyDiagnosticsCommand.Execute(null));

        Assert.Null(thrown);
    }

    /// <summary>
    /// Swallows a transient clipboard-access failure when copying the produced archive's path, the same
    /// way <see cref="CopyDiagnosticsCommandSwallowsAClipboardAccessFailure"/> does for diagnostics.
    /// </summary>
    [Fact]
    public async Task CopyArchivePathCommandSwallowsAClipboardAccessFailure()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, setClipboardText: _ => throw new ExternalException("clipboard busy"));
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Exception? thrown = Record.Exception(() => viewModel.CopyArchivePathCommand.Execute(null));

        Assert.Null(thrown);
    }

    /// <summary>
    /// Deletes only the scoped generated output directories for a clean build -- adapter/build/windows-x64-release
    /// and tooling/out/{publish,package} -- leaving a sibling directory (standing in for vcpkg's shared
    /// package cache) untouched.
    /// </summary>
    [Fact]
    public async Task BuildCommandWithCleanBuildDeletesOnlyTheScopedOutputDirectories()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string releaseDir = Path.Combine(repositoryRoot, "adapter", "build", "windows-x64-release");
        string publishDir = Path.Combine(repositoryRoot, "tooling", "out", "publish");
        string packageDir = Path.Combine(repositoryRoot, "tooling", "out", "package");
        string vcpkgCacheDir = Path.Combine(repositoryRoot, "adapter", "build", "vcpkg_installed");
        CreateDirectoryWithMarkerFile(releaseDir);
        CreateDirectoryWithMarkerFile(publishDir);
        CreateDirectoryWithMarkerFile(packageDir);
        CreateDirectoryWithMarkerFile(vcpkgCacheDir);
        var viewModel = BuildViewModel(repositoryRoot: repositoryRoot);
        await viewModel.InitializeAsync();
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(Directory.Exists(releaseDir));
        Assert.False(Directory.Exists(publishDir));
        Assert.False(Directory.Exists(packageDir));
        Assert.True(Directory.Exists(vcpkgCacheDir));
    }

    /// <summary>Passes the picker's selected profile through to the build coordinator's request.</summary>
    [Fact]
    public async Task BuildCommandPassesTheSelectedProfileToTheCoordinator()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(repositoryRoot: temporaryDirectory.Path, buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = BuildProfile.Debug;

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildProfile.Debug, buildCoordinator.LastRequest?.Profile);
    }

    /// <summary>Passes the Settings page's output path override through to the build coordinator's request.</summary>
    [Fact]
    public async Task BuildCommandPassesTheOutputPathOverrideToTheCoordinator()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputOverride = Path.Combine(temporaryDirectory.Path, "custom-output");
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var outputPathContext = new OutputPathContext(outputOverride);
        var viewModel = BuildViewModel(repositoryRoot: temporaryDirectory.Path, buildCoordinator: buildCoordinator, outputPathContext: outputPathContext);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(outputOverride, buildCoordinator.LastRequest?.OutputRootOverride);
    }

    /// <summary>Deletes only the selected non-Release profile's own scoped output directories for a clean build.</summary>
    [Fact]
    public async Task CleanBuildForANonReleaseProfileDeletesThatProfilesOwnOutputDirectories()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string debugAdapterDir = Path.Combine(repositoryRoot, "adapter", "build", "windows-x64-debug");
        string releaseAdapterDir = Path.Combine(repositoryRoot, "adapter", "build", "windows-x64-release");
        string debugPublishDir = Path.Combine(repositoryRoot, "tooling", "out", "debug", "publish");
        string releasePublishDir = Path.Combine(repositoryRoot, "tooling", "out", "publish");
        CreateDirectoryWithMarkerFile(debugAdapterDir);
        CreateDirectoryWithMarkerFile(releaseAdapterDir);
        CreateDirectoryWithMarkerFile(debugPublishDir);
        CreateDirectoryWithMarkerFile(releasePublishDir);
        var viewModel = BuildViewModel(repositoryRoot: repositoryRoot);
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = BuildProfile.Debug;
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(Directory.Exists(debugAdapterDir));
        Assert.False(Directory.Exists(debugPublishDir));
        Assert.True(Directory.Exists(releaseAdapterDir));
        Assert.True(Directory.Exists(releasePublishDir));
    }

    /// <summary>Leaves every existing output directory untouched when Clean build is not enabled.</summary>
    [Fact]
    public async Task BuildCommandWithoutCleanBuildLeavesOutputDirectoriesUntouched()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string releaseDir = Path.Combine(repositoryRoot, "adapter", "build", "windows-x64-release");
        CreateDirectoryWithMarkerFile(releaseDir);
        var viewModel = BuildViewModel(repositoryRoot: repositoryRoot);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.True(Directory.Exists(releaseDir));
    }

    /// <summary>
    /// Succeeds on a Clean build when none of the scoped output directories exist yet -- the common
    /// case on a fresh clone's first build -- rather than failing on a missing-directory error.
    /// </summary>
    [Fact]
    public async Task BuildCommandWithCleanBuildSucceedsWhenTheOutputDirectoriesDoNotExistYet()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var viewModel = BuildViewModel(repositoryRoot: temporaryDirectory.Path);
        await viewModel.InitializeAsync();
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Succeeded, viewModel.LastOutcome);
    }

    /// <summary>Deletes under the output path override, not the default output root, for a clean build when one is set.</summary>
    [Fact]
    public async Task CleanBuildDeletesUnderTheOutputPathOverrideWhenSet()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string outputOverride = Path.Combine(repositoryRoot, "custom-output");
        string overridePublishDir = Path.Combine(outputOverride, "publish");
        string defaultPublishDir = Path.Combine(repositoryRoot, "tooling", "out", "publish");
        CreateDirectoryWithMarkerFile(overridePublishDir);
        CreateDirectoryWithMarkerFile(defaultPublishDir);
        var outputPathContext = new OutputPathContext(outputOverride);
        var viewModel = BuildViewModel(repositoryRoot: repositoryRoot, outputPathContext: outputPathContext);
        await viewModel.InitializeAsync();
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(Directory.Exists(overridePublishDir));
        Assert.True(Directory.Exists(defaultPublishDir));
    }

    /// <summary>
    /// Refuses a normal (non-clean-flagged) build before any destructive work -- including before the
    /// coordinator, and therefore its packaging, ever runs -- when the output path override resolves
    /// to an arbitrary existing folder that already has unrelated content and no DovahLink Builder
    /// ownership mark. Uses a real <see cref="BuildOutputOwnershipGuard"/>, unlike this file's other
    /// tests, since this is what actually enforces the refusal.
    /// </summary>
    [Fact]
    public async Task NormalBuildRefusesAnUnrelatedNonEmptyCustomOutputPathBeforeAnyDestructiveWork()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string outputOverride = Path.Combine(repositoryRoot, "unrelated-user-folder");
        string unrelatedFilePath = Path.Combine(outputOverride, "package");
        Directory.CreateDirectory(outputOverride);
        byte[] unrelatedContent = "the user's own real data, not DovahLink's"u8.ToArray();
        File.WriteAllBytes(unrelatedFilePath, unrelatedContent);
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var outputPathContext = new OutputPathContext(outputOverride);
        var viewModel = BuildViewModel(
            repositoryRoot: repositoryRoot,
            buildCoordinator: buildCoordinator,
            outputPathContext: outputPathContext,
            outputOwnershipGuard: new BuildOutputOwnershipGuard());
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Failed, viewModel.LastOutcome);
        Assert.Contains(outputOverride, viewModel.LastOutcomeMessage);
        Assert.Equal(0, buildCoordinator.CallCount);
        Assert.False(File.Exists(Path.Combine(outputOverride, BuildOutputOwnershipGuard.MarkerFileName)));
        Assert.Equal(unrelatedContent, File.ReadAllBytes(unrelatedFilePath));
    }

    /// <summary>
    /// Refuses a Clean build before <see cref="BuildPageViewModel"/>'s own output-clearing step deletes
    /// anything, for the same unrelated, unmarked custom output path scenario as the normal-build
    /// refusal above -- Clean must not be a way to route around the ownership check a normal build
    /// already enforces.
    /// </summary>
    [Fact]
    public async Task CleanBuildRefusesAnUnrelatedNonEmptyCustomOutputPathBeforeDeletingAnything()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string outputOverride = Path.Combine(repositoryRoot, "unrelated-user-folder");
        string unrelatedPublishDir = Path.Combine(outputOverride, "publish");
        string unrelatedPackageDir = Path.Combine(outputOverride, "package");
        CreateDirectoryWithMarkerFile(unrelatedPublishDir);
        CreateDirectoryWithMarkerFile(unrelatedPackageDir);
        var outputPathContext = new OutputPathContext(outputOverride);
        var viewModel = BuildViewModel(
            repositoryRoot: repositoryRoot,
            outputPathContext: outputPathContext,
            outputOwnershipGuard: new BuildOutputOwnershipGuard());
        await viewModel.InitializeAsync();
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Failed, viewModel.LastOutcome);
        Assert.True(Directory.Exists(unrelatedPublishDir));
        Assert.True(Directory.Exists(unrelatedPackageDir));
    }

    /// <summary>
    /// Still cleans a custom output path that is already marked as DovahLink Builder-owned from a
    /// previous run, proving the ownership check only refuses an unproven folder -- it does not block
    /// legitimate reuse of a custom output location across builds.
    /// </summary>
    [Fact]
    public async Task CleanBuildStillWorksForAnAlreadyOwnedCustomOutputPath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string outputOverride = Path.Combine(repositoryRoot, "previously-owned-output");
        string ownedPublishDir = Path.Combine(outputOverride, "publish");
        CreateDirectoryWithMarkerFile(ownedPublishDir);
        File.WriteAllText(Path.Combine(outputOverride, BuildOutputOwnershipGuard.MarkerFileName), "owned");
        var outputPathContext = new OutputPathContext(outputOverride);
        var viewModel = BuildViewModel(
            repositoryRoot: repositoryRoot,
            outputPathContext: outputPathContext,
            outputOwnershipGuard: new BuildOutputOwnershipGuard());
        await viewModel.InitializeAsync();
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Succeeded, viewModel.LastOutcome);
        Assert.False(Directory.Exists(ownedPublishDir));
    }

    /// <summary>
    /// A normal (non-clean-flagged) build also succeeds against a custom output path already marked
    /// as DovahLink Builder-owned, symmetrically with the Clean-build case above -- the ownership
    /// check gates both paths the same way, not just Clean.
    /// </summary>
    [Fact]
    public async Task NormalBuildStillWorksForAnAlreadyOwnedCustomOutputPath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string outputOverride = Path.Combine(repositoryRoot, "previously-owned-output");
        Directory.CreateDirectory(outputOverride);
        File.WriteAllText(Path.Combine(outputOverride, BuildOutputOwnershipGuard.MarkerFileName), "owned");
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var outputPathContext = new OutputPathContext(outputOverride);
        var viewModel = BuildViewModel(
            repositoryRoot: repositoryRoot,
            buildCoordinator: buildCoordinator,
            outputPathContext: outputPathContext,
            outputOwnershipGuard: new BuildOutputOwnershipGuard());
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Succeeded, viewModel.LastOutcome);
        Assert.Equal(1, buildCoordinator.CallCount);
    }

    /// <summary>
    /// Fails, through the same normalized message, when the output path override is malformed --
    /// here an embedded null character -- proving <see cref="BuildPageViewModel"/>'s own call site
    /// into the ownership guard is protected the same way as the coordinator's.
    /// </summary>
    [Fact]
    public async Task BuildFailsWithAnUnderstandableReasonForAMalformedOutputPathOverride()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string malformedOutputOverride = Path.Combine(repositoryRoot, "custom-out\0bad");
        var outputPathContext = new OutputPathContext(malformedOutputOverride);
        var viewModel = BuildViewModel(
            repositoryRoot: repositoryRoot,
            outputPathContext: outputPathContext,
            outputOwnershipGuard: new BuildOutputOwnershipGuard());
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Failed, viewModel.LastOutcome);
        Assert.NotNull(viewModel.LastOutcomeMessage);
    }

    /// <summary>
    /// Regression test for the ownership check's own cancellation safety: cancelling immediately after
    /// Build, before the check has had any chance to run, must not race it out and let the build reach
    /// the coordinator with nothing ever verified. The check is a single fast, synchronous filesystem
    /// operation not tied to the build's own cancellation token (see RunBuildAsync), so it always runs
    /// to completion and reports Failed -- not Cancelled -- for an unowned custom output path.
    /// </summary>
    [Fact]
    public async Task CancellingImmediatelyAfterBuildDoesNotSkipTheOwnershipCheck()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string outputOverride = Path.Combine(repositoryRoot, "unrelated-user-folder");
        string unrelatedFilePath = Path.Combine(outputOverride, "package");
        Directory.CreateDirectory(outputOverride);
        byte[] unrelatedContent = "the user's own real data, not DovahLink's"u8.ToArray();
        File.WriteAllBytes(unrelatedFilePath, unrelatedContent);
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var outputPathContext = new OutputPathContext(outputOverride);
        var viewModel = BuildViewModel(
            repositoryRoot: repositoryRoot,
            buildCoordinator: buildCoordinator,
            outputPathContext: outputPathContext,
            outputOwnershipGuard: new BuildOutputOwnershipGuard());
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        viewModel.CancelCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(BuildHistoryResult.Failed, viewModel.LastOutcome);
        Assert.Equal(0, buildCoordinator.CallCount);
        Assert.Equal(unrelatedContent, File.ReadAllBytes(unrelatedFilePath));
    }

    /// <summary>
    /// The core state-coherence invariant this build's snapshot exists to guarantee: once a clean build
    /// has started against repo A / Release / no output override, changing the active repository,
    /// selected profile, and output path override before that build finishes must not affect it in any
    /// way -- not the directories Clean deletes, not the coordinator request, not the recorded history --
    /// and the very next build must pick up exactly those changed values. Repo A carries a real Release
    /// adapter-build directory and default publish output that clean-build must delete; repo B carries
    /// its own Debug adapter-build directory that must survive untouched, since this run never targets it.
    /// </summary>
    [Fact]
    public async Task ChangingRepositoryProfileAndOutputPathMidBuildDoesNotAffectTheAlreadyStartedBuild()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryARoot = Path.Combine(temporaryDirectory.Path, "repo-a");
        string repositoryBRoot = Path.Combine(temporaryDirectory.Path, "repo-b");
        Directory.CreateDirectory(repositoryARoot);
        Directory.CreateDirectory(repositoryBRoot);
        File.WriteAllText(Path.Combine(repositoryARoot, "VERSION"), "1.0.0");
        File.WriteAllText(Path.Combine(repositoryBRoot, "VERSION"), "2.0.0");
        string repositoryAReleaseAdapterDir = Path.Combine(repositoryARoot, "adapter", "build", "windows-x64-release");
        string repositoryADefaultPublishDir = Path.Combine(repositoryARoot, "tooling", "out", "publish");
        string repositoryBDebugAdapterDir = Path.Combine(repositoryBRoot, "adapter", "build", "windows-x64-debug");
        CreateDirectoryWithMarkerFile(repositoryAReleaseAdapterDir);
        CreateDirectoryWithMarkerFile(repositoryADefaultPublishDir);
        CreateDirectoryWithMarkerFile(repositoryBDebugAdapterDir);
        var repositoryContext = new RepositoryContext(repositoryARoot);
        var outputPathContext = new OutputPathContext(null);
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var buildHistoryStore = new FakeBuildHistoryStore();
        var viewModel = BuildViewModel(
            repositoryContext: repositoryContext,
            outputPathContext: outputPathContext,
            buildCoordinator: buildCoordinator,
            buildHistoryStore: buildHistoryStore);
        await viewModel.InitializeAsync();
        viewModel.SelectedProfile = BuildProfile.Release;
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        // Mutates every piece of mutable state this build's snapshot must be immune to, immediately
        // after starting it and before awaiting its completion below.
        repositoryContext.SetRepositoryRoot(repositoryBRoot);
        viewModel.SelectedProfile = BuildProfile.Debug;
        outputPathContext.SetOutputPath(@"D:\should-not-be-used");
        await viewModel.RunningBuildTask!;

        Assert.False(Directory.Exists(repositoryAReleaseAdapterDir));
        Assert.False(Directory.Exists(repositoryADefaultPublishDir));
        Assert.True(Directory.Exists(repositoryBDebugAdapterDir));

        Assert.Equal(repositoryARoot, buildCoordinator.LastRequest?.RepositoryRoot);
        Assert.Equal(BuildProfile.Release, buildCoordinator.LastRequest?.Profile);
        Assert.Null(buildCoordinator.LastRequest?.OutputRootOverride);

        BuildHistoryEntry recorded = Assert.Single(buildHistoryStore.GetRecent());
        Assert.Equal("1.0.0", recorded.Version);
        Assert.Equal("Release", recorded.Profile);

        // The mid-build repository change above triggered its own environment refresh in the
        // background; waits for it here so CanBuild reflects repo B before starting the next build,
        // exactly as the real UI would after a repository change settles.
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(repositoryBRoot, buildCoordinator.LastRequest?.RepositoryRoot);
        Assert.Equal(BuildProfile.Debug, buildCoordinator.LastRequest?.Profile);
        Assert.Equal(@"D:\should-not-be-used", buildCoordinator.LastRequest?.OutputRootOverride);
    }

    /// <summary>
    /// Keeps using a configured output path override for an already-started build even when it is
    /// cleared back to the profile's default mid-build -- the mirror of the override being set
    /// mid-build, which <see cref="ChangingRepositoryProfileAndOutputPathMidBuildDoesNotAffectTheAlreadyStartedBuild"/>
    /// already covers in the other direction.
    /// </summary>
    [Fact]
    public async Task ClearingTheOutputPathOverrideMidBuildDoesNotAffectTheAlreadyStartedBuild()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string outputOverride = Path.Combine(temporaryDirectory.Path, "custom-output");
        var outputPathContext = new OutputPathContext(outputOverride);
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = BuildViewModel(repositoryRoot: temporaryDirectory.Path, outputPathContext: outputPathContext, buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        outputPathContext.SetOutputPath(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal(outputOverride, buildCoordinator.LastRequest?.OutputRootOverride);
    }

    /// <summary>
    /// Keeps cleaning generated outputs for a build that already started as a clean build, even when
    /// Clean build is turned off before it finishes -- the toggle only ever affects the next build.
    /// </summary>
    [Fact]
    public async Task TogglingCleanBuildOffMidBuildDoesNotAffectTheAlreadyStartedCleanBuild()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string repositoryRoot = temporaryDirectory.Path;
        string releaseAdapterDir = Path.Combine(repositoryRoot, "adapter", "build", "windows-x64-release");
        CreateDirectoryWithMarkerFile(releaseAdapterDir);
        var viewModel = BuildViewModel(repositoryRoot: repositoryRoot);
        await viewModel.InitializeAsync();
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        viewModel.IsCleanBuild = false;
        await viewModel.RunningBuildTask!;

        Assert.False(Directory.Exists(releaseAdapterDir));
    }

    /// <summary>Creates a directory containing a marker file, so an empty-directory quirk can't hide a real deletion bug.</summary>
    /// <param name="path">The directory to create.</param>
    private static void CreateDirectoryWithMarkerFile(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "marker.txt"), "x");
    }

    /// <summary>Enters the Cancelling transient state and disables further cancellation while it is in progress.</summary>
    [Fact]
    public async Task CancelCommandEntersTheCancellingStateAndDisablesFurtherCancellation()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);

        viewModel.CancelCommand.Execute(null);

        Assert.True(viewModel.IsCancelling);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal("Stopping build… Terminating active build processes.", viewModel.CancellingMessage);
        Assert.Equal("Cancelling", viewModel.FooterStatusText);

        await viewModel.RunningBuildTask!;

        Assert.False(viewModel.IsCancelling);
        Assert.Null(viewModel.CancellingMessage);
        Assert.Equal("Cancelled", viewModel.FooterStatusText);
    }

    /// <summary>Does nothing when Cancel is executed directly while already cancelling, bypassing the bound command's own CanExecute gate.</summary>
    [Fact]
    public async Task CancelCommandDoesNothingWhenAlreadyCancelling()
    {
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator);
        await viewModel.InitializeAsync();
        viewModel.BuildCommand.Execute(null);
        viewModel.CancelCommand.Execute(null);
        Assert.True(viewModel.IsCancelling);

        viewModel.CancelCommand.Execute(null);

        Assert.True(viewModel.IsCancelling);
        await viewModel.RunningBuildTask!;
    }

    /// <summary>Does nothing when Cancel is executed directly while no build is running.</summary>
    [Fact]
    public void CancelCommandDoesNothingWhenNoBuildIsRunning()
    {
        var viewModel = BuildViewModel();

        viewModel.CancelCommand.Execute(null);

        Assert.False(viewModel.IsCancelling);
    }

    /// <summary>Reports every required build tool as available, for a fake that does not otherwise override <see cref="FakePreflightService.Results"/>.</summary>
    private sealed class FakePreflightService : IPreflightService
    {
        /// <summary>
        /// Gets or sets the results to return; defaults to every required tool reporting Found. Mutable
        /// so a test can reconfigure it between two calls on the same fake instance.
        /// </summary>
        public IReadOnlyList<ToolchainCheckResult> Results { get; set; } = BuildAllFoundResults();

        /// <summary>Gets or sets a signal <see cref="CheckAllAsync"/> awaits before completing, or <see langword="null"/> to complete immediately.</summary>
        public TaskCompletionSource? PauseSignal { get; set; }

        /// <summary>Gets or sets the exception <see cref="CheckAllAsync"/> throws instead of returning <see cref="Results"/>, or <see langword="null"/> to succeed normally.</summary>
        public Exception? ExceptionToThrow { get; set; }

        /// <summary>Gets every <paramref name="startPath"/> a caller has requested a check for, in call order.</summary>
        public List<string> CapturedStartPaths { get; } = [];

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, string? outputPathOverride = null, CancellationToken cancellationToken = default)
        {
            CapturedStartPaths.Add(startPath);
            if (PauseSignal is not null)
            {
                await PauseSignal.Task;
            }

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Results;
        }

        /// <inheritdoc/>
        public IReadOnlyList<ToolchainCheckResult> RefreshOutputFolderCheck(IReadOnlyList<ToolchainCheckResult> previousResults, string? repositoryRoot, string? outputPathOverride) =>
            previousResults;

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
        /// <summary>
        /// Gets or sets the status to return; defaults to a clean tree already pushed to its upstream.
        /// Mutable so a test can reconfigure it between two calls on the same fake instance.
        /// </summary>
        public GitSourceStatus Status { get; set; } = new("main", WorkingTreeState.Clean, RemoteSyncState.Pushed, "abc123");

        /// <summary>
        /// Gets or sets the exception to throw instead of returning <see cref="Status"/>, or
        /// <see langword="null"/>. Mutable so a test can reconfigure it between two calls on the same
        /// fake instance.
        /// </summary>
        public Exception? ThrownException { get; set; }

        /// <inheritdoc/>
        public Task<GitSourceStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            ThrownException is not null ? throw ThrownException : Task.FromResult(Status);
    }

    /// <summary>
    /// Treats every output root as already owned, doing nothing. The real ownership behavior itself
    /// is covered by <see cref="BuildOutputOwnershipGuardTests"/> and this file's own
    /// ownership-specific tests, which construct a real <see cref="BuildOutputOwnershipGuard"/>
    /// explicitly; every other test in this file uses this fake so it never touches real disk at the
    /// fabricated repository paths (for example <c>C:\repo</c>) those tests construct.
    /// </summary>
    private sealed class FakeBuildOutputOwnershipGuard : IBuildOutputOwnershipGuard
    {
        /// <inheritdoc/>
        public void EnsureOwned(string outputRoot, string repositoryRoot)
        {
        }
    }

    /// <summary>Returns a successful build result unless configured to throw, hang, or count invocations.</summary>
    private sealed class FakeAdapterHostBuildCoordinator : IAdapterHostBuildCoordinator
    {
        /// <summary>
        /// Gets the result to return on success. Defaults to the test assembly's own DLL path, a real
        /// file that always exists (needing no setup or cleanup), for a test that only cares that the
        /// build succeeded rather than the archive's actual contents; a test that hashes or reads the
        /// archive overrides this with a real, purpose-built ZIP.
        /// </summary>
        public AdapterHostBuildResult Result { get; init; } = new(typeof(BuildPageViewModelTests).Assembly.Location);

        /// <summary>
        /// Gets or sets the exception <see cref="BuildAsync"/> throws instead of succeeding, or
        /// <see langword="null"/>. Mutable so a test can reconfigure it between two calls on the same
        /// fake instance.
        /// </summary>
        public Exception? ThrownException { get; set; }

        /// <summary>Gets whether <see cref="BuildAsync"/> waits for cancellation instead of completing immediately.</summary>
        public bool WaitForCancellation { get; init; }

        /// <summary>Gets the number of times <see cref="BuildAsync"/> was called.</summary>
        public int CallCount { get; private set; }

        /// <summary>Gets the request passed to the most recent <see cref="BuildAsync"/> call, or <see langword="null"/> before any call.</summary>
        public AdapterHostBuildRequest? LastRequest { get; private set; }

        /// <summary>
        /// Gets or sets the stage transitions <see cref="BuildAsync"/> reports through <c>onStage</c>
        /// before returning or throwing. Mutable so a test can reconfigure it between two calls on the
        /// same fake instance.
        /// </summary>
        public IReadOnlyList<BuildStageEvent> StageEventsToEmit { get; set; } = [];

        /// <summary>Gets or sets the output lines <see cref="BuildAsync"/> reports through <c>onOutput</c> before returning or throwing.</summary>
        public IReadOnlyList<string> OutputLinesToEmit { get; set; } = [];

        /// <inheritdoc/>
        public async Task<AdapterHostBuildResult> BuildAsync(
            AdapterHostBuildRequest request,
            Action<string>? onOutput = null,
            Action<BuildStageEvent>? onStage = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            foreach (BuildStageEvent stageEvent in StageEventsToEmit)
            {
                onStage?.Invoke(stageEvent);
            }

            foreach (string line in OutputLinesToEmit)
            {
                onOutput?.Invoke(line);
            }

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

    /// <summary>Throws <see cref="OperationCanceledException"/> from its very first command, simulating a build cancelled during ConfigureAdapter.</summary>
    private sealed class CancellingCommandRunner : ICommandRunner
    {
        /// <inheritdoc/>
        public Task<int> RunAsync(
            BuildCommand command,
            Action<string>? onStandardOutput,
            Action<string>? onStandardError,
            CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }

    /// <summary>An in-memory <see cref="IBuildHistoryStore"/>, avoiding real disk I/O for tests that record build history.</summary>
    private sealed class FakeBuildHistoryStore : IBuildHistoryStore
    {
        /// <summary>The recorded entries, most recent first.</summary>
        private readonly List<BuildHistoryEntry> entries = [];

        /// <summary>Gets or sets the exception <see cref="Add"/> throws instead of recording, or <see langword="null"/>.</summary>
        public Exception? ThrownExceptionOnAdd { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<BuildHistoryEntry> GetRecent() => entries;

        /// <inheritdoc/>
        public void Add(BuildHistoryEntry entry)
        {
            if (ThrownExceptionOnAdd is not null)
            {
                throw ThrownExceptionOnAdd;
            }

            entries.Insert(0, entry);
        }
    }

    /// <summary>An in-memory <see cref="ISettingsStore"/>, avoiding real disk I/O for tests over Builder settings.</summary>
    private sealed class FakeSettingsStore : ISettingsStore
    {
        /// <summary>Gets or sets the currently persisted settings; defaults to <see cref="BuilderSettings"/>'s own defaults.</summary>
        public BuilderSettings Settings { get; set; } = new();

        /// <summary>Gets or sets the exception <see cref="Save"/> throws instead of persisting, or <see langword="null"/>.</summary>
        public Exception? ThrownExceptionOnSave { get; set; }

        /// <inheritdoc/>
        public BuilderSettings Load() => Settings;

        /// <inheritdoc/>
        public void Save(BuilderSettings settings)
        {
            if (ThrownExceptionOnSave is not null)
            {
                throw ThrownExceptionOnSave;
            }

            Settings = settings;
        }
    }

    /// <summary>A scripted <see cref="IFolderPickerService"/>, avoiding a real OS dialog in tests.</summary>
    private sealed class FakeFolderPicker : IFolderPickerService
    {
        /// <summary>Gets or sets the folder <see cref="PickFolder"/> returns, or <see langword="null"/> to simulate the user cancelling the dialog.</summary>
        public string? NextPick { get; set; }

        /// <inheritdoc/>
        public string? PickFolder(string title, string? initialDirectory) => NextPick;
    }

    /// <summary>
    /// Blocks building and reports the failure when the environment refresh itself fails, rather than
    /// falling through as if the environment were simply unchecked -- a failed refresh leaves
    /// PreflightResults empty, which names no specific tool as unavailable.
    /// </summary>
    [Fact]
    public async Task InitializeAsyncBlocksBuildingWhenTheEnvironmentRefreshFails()
    {
        var preflightService = new FakePreflightService { ExceptionToThrow = new InvalidOperationException("disk full") };
        var viewModel = BuildViewModel(preflightService: preflightService);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.CanBuild);
        Assert.Contains("disk full", viewModel.BuildBlockedReason!);
    }

    /// <summary>Recovers once a subsequent refresh succeeds after an earlier one failed, clearing the reported reason and allowing the build to proceed again.</summary>
    [Fact]
    public async Task RecoversOnceARefreshSucceedsAfterAnEarlierOneFailed()
    {
        var preflightService = new FakePreflightService { ExceptionToThrow = new InvalidOperationException("disk full") };
        var repositoryContext = new RepositoryContext(@"C:\repo");
        var gitStatusStore = new GitStatusStore(new FakeGitStatusService(), repositoryContext);
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext, new OutputPathContext(null));
        var viewModel = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new FakeAdapterHostBuildCoordinator(),
            new FakeBuildHistoryStore(),
            _ => { },
            _ => { },
            repositoryContext,
            new OutputPathContext(null),
            new FakeBuildOutputOwnershipGuard(),
            new LogViewModel(_ => { }),
            stage => new BuildStageViewModel(stage),
            new RuntimeBuildSettingsContext(openOutputFolderAfterSuccessfulBuild: true, autoScrollLogs: true));
        await viewModel.InitializeAsync();
        Assert.False(viewModel.CanBuild);

        preflightService.ExceptionToThrow = null;
        await environmentStore.RefreshAsync();

        Assert.True(viewModel.CanBuild);
        Assert.Null(viewModel.BuildBlockedReason);
    }
}
