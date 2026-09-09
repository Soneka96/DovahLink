using System.IO.Compression;
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
        FakeSettingsStore? settingsStore = null,
        Action<string>? openOutputFolder = null,
        Action<string>? setClipboardText = null,
        string? repositoryRoot = null,
        IRepositoryContext? repositoryContext = null)
    {
        string resolvedRepositoryRoot = repositoryRoot ?? @"C:\repo";
        IRepositoryContext resolvedRepositoryContext = repositoryContext ?? new RepositoryContext(resolvedRepositoryRoot);
        var gitStatusStore = new GitStatusStore(gitStatusService ?? new FakeGitStatusService(), resolvedRepositoryContext);
        var environmentStore = new EnvironmentStore(preflightService ?? new FakePreflightService(), gitStatusStore, resolvedRepositoryContext);
        return new(
            environmentStore,
            gitStatusStore,
            buildCoordinator ?? new FakeAdapterHostBuildCoordinator(),
            buildHistoryStore ?? new FakeBuildHistoryStore(),
            settingsStore ?? new FakeSettingsStore(),
            openOutputFolder ?? (_ => { }),
            setClipboardText ?? (_ => { }),
            resolvedRepositoryContext);
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
        var environmentStore = new EnvironmentStore(preflightService, gitStatusStore, repositoryContext);
        var buildCoordinator = new FakeAdapterHostBuildCoordinator();
        var viewModel = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            buildCoordinator,
            new FakeBuildHistoryStore(),
            new FakeSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext);
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
        var environmentStore = new EnvironmentStore(new FakePreflightService(), gitStatusStore, repositoryContext);
        var viewModel = new BuildPageViewModel(
            environmentStore,
            gitStatusStore,
            new FakeAdapterHostBuildCoordinator(),
            new FakeBuildHistoryStore(),
            new FakeSettingsStore(),
            _ => { },
            _ => { },
            repositoryContext);
        await viewModel.InitializeAsync();
        Assert.False(viewModel.GitNeedsAttention);

        gitStatusService.Status = new GitSourceStatus("main", WorkingTreeState.Dirty, RemoteSyncState.Pushed, "abc123");
        await gitStatusStore.RefreshAsync();

        Assert.True(viewModel.GitNeedsAttention);
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

    /// <summary>Keeps the log populated through a Building-to-Cancelling-to-Cancelled transition (correction #6).</summary>
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

    /// <summary>Seeds the log panel's auto-scroll from the persisted setting at construction.</summary>
    [Fact]
    public void ConstructorSeedsLogAutoScrollFromSettings()
    {
        var settingsStore = new FakeSettingsStore { Settings = new BuilderSettings(AutoScrollLogs: false) };

        var viewModel = BuildViewModel(settingsStore: settingsStore);

        Assert.False(viewModel.Log.AutoScroll);
    }

    /// <summary>Opens the archive's containing folder after a successful build when the setting is enabled (correction #12).</summary>
    [Fact]
    public async Task BuildCommandOpensTheOutputFolderOnSuccessWhenEnabled()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        var settingsStore = new FakeSettingsStore { Settings = new BuilderSettings(OpenOutputFolderAfterSuccessfulBuild: true) };
        var openedFolders = new List<string>();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, settingsStore: settingsStore, openOutputFolder: openedFolders.Add);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Equal([Path.GetDirectoryName(archivePath)], openedFolders);
    }

    /// <summary>Does not open the output folder after a successful build when the setting is disabled.</summary>
    [Fact]
    public async Task BuildCommandDoesNotOpenTheOutputFolderOnSuccessWhenDisabled()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string archivePath = CreateRealZip(temporaryDirectory.Path, ("manifest.json", "{}"));
        var settingsStore = new FakeSettingsStore { Settings = new BuilderSettings(OpenOutputFolderAfterSuccessfulBuild: false) };
        var openedFolders = new List<string>();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, settingsStore: settingsStore, openOutputFolder: openedFolders.Add);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Empty(openedFolders);
    }

    /// <summary>Never opens the output folder for a failed build, even when the setting is enabled (correction #12).</summary>
    [Fact]
    public async Task BuildCommandDoesNotOpenTheOutputFolderOnFailure()
    {
        var settingsStore = new FakeSettingsStore { Settings = new BuilderSettings(OpenOutputFolderAfterSuccessfulBuild: true) };
        var openedFolders = new List<string>();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { ThrownException = new InvalidOperationException("the adapter build failed") };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, settingsStore: settingsStore, openOutputFolder: openedFolders.Add);
        await viewModel.InitializeAsync();

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.Empty(openedFolders);
    }

    /// <summary>Never opens the output folder for a cancelled build, even when the setting is enabled (correction #12).</summary>
    [Fact]
    public async Task CancelCommandDoesNotOpenTheOutputFolder()
    {
        var settingsStore = new FakeSettingsStore { Settings = new BuilderSettings(OpenOutputFolderAfterSuccessfulBuild: true) };
        var openedFolders = new List<string>();
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { WaitForCancellation = true };
        var viewModel = BuildViewModel(buildCoordinator: buildCoordinator, settingsStore: settingsStore, openOutputFolder: openedFolders.Add);
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
        var settingsStore = new FakeSettingsStore { Settings = new BuilderSettings(OpenOutputFolderAfterSuccessfulBuild: true) };
        var buildCoordinator = new FakeAdapterHostBuildCoordinator { Result = new AdapterHostBuildResult(archivePath) };
        var viewModel = BuildViewModel(
            buildCoordinator: buildCoordinator,
            settingsStore: settingsStore,
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
    /// a build (correction #9's freshness intent, now decoupled from auto-building).
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
    /// Deletes only the scoped generated output directories for a clean build -- adapter/build/windows-x64-release
    /// and tooling/out/{publish,package} -- leaving a sibling directory (standing in for vcpkg's shared
    /// package cache) untouched (correction #10).
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
        var settingsStore = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: outputOverride) };
        var viewModel = BuildViewModel(repositoryRoot: temporaryDirectory.Path, buildCoordinator: buildCoordinator, settingsStore: settingsStore);
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
        var settingsStore = new FakeSettingsStore { Settings = new BuilderSettings(OutputPath: outputOverride) };
        var viewModel = BuildViewModel(repositoryRoot: repositoryRoot, settingsStore: settingsStore);
        await viewModel.InitializeAsync();
        viewModel.IsCleanBuild = true;

        viewModel.BuildCommand.Execute(null);
        await viewModel.RunningBuildTask!;

        Assert.False(Directory.Exists(overridePublishDir));
        Assert.True(Directory.Exists(defaultPublishDir));
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

        /// <summary>Gets every <paramref name="startPath"/> a caller has requested a check for, in call order.</summary>
        public List<string> CapturedStartPaths { get; } = [];

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ToolchainCheckResult>> CheckAllAsync(string startPath, CancellationToken cancellationToken = default)
        {
            CapturedStartPaths.Add(startPath);
            if (PauseSignal is not null)
            {
                await PauseSignal.Task;
            }

            return Results;
        }

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

    /// <summary>An in-memory <see cref="IBuildHistoryStore"/>, avoiding real disk I/O for tests that record build history.</summary>
    private sealed class FakeBuildHistoryStore : IBuildHistoryStore
    {
        /// <summary>The recorded entries, most recent first.</summary>
        private readonly List<BuildHistoryEntry> entries = [];

        /// <inheritdoc/>
        public IReadOnlyList<BuildHistoryEntry> GetRecent() => entries;

        /// <inheritdoc/>
        public void Add(BuildHistoryEntry entry) => entries.Insert(0, entry);
    }

    /// <summary>An in-memory <see cref="ISettingsStore"/>, avoiding real disk I/O for tests over Builder settings.</summary>
    private sealed class FakeSettingsStore : ISettingsStore
    {
        /// <summary>Gets or sets the currently persisted settings; defaults to <see cref="BuilderSettings"/>'s own defaults.</summary>
        public BuilderSettings Settings { get; set; } = new();

        /// <inheritdoc/>
        public BuilderSettings Load() => Settings;

        /// <inheritdoc/>
        public void Save(BuilderSettings settings) => Settings = settings;
    }
}
