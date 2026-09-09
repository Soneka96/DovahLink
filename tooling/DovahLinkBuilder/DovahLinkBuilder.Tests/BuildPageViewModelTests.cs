using System.IO.Compression;
using System.Security.Cryptography;
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
}
