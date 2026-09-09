using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Persistence;
using DovahLink.DovahLinkBuilder.Preflight;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the Build page's state: preflight and git gating, the uncommitted/unpushed acknowledgement
/// flow, and running the Adapter+Host build.
/// </summary>
public sealed class BuildPageViewModel : ObservableObject
{
    /// <summary>The text shown while preflight and git status have not finished loading yet.</summary>
    private const string CheckingEnvironmentReason = "Checking environment and git status...";

    /// <summary>Checks the required build tools before a build is allowed to start.</summary>
    private readonly IPreflightService preflightService;

    /// <summary>Reports the repository's branch, working tree, and remote sync state.</summary>
    private readonly IGitStatusService gitStatusService;

    /// <summary>Builds and packages the production Adapter and Host.</summary>
    private readonly IAdapterHostBuildCoordinator buildCoordinator;

    /// <summary>Persists and retrieves the Builder's recent build history.</summary>
    private readonly IBuildHistoryStore buildHistoryStore;

    /// <summary>Loads the Builder's persisted settings.</summary>
    private readonly ISettingsStore settingsStore;

    /// <summary>Opens a folder in the system file explorer, for <see cref="BuilderSettings.OpenOutputFolderAfterSuccessfulBuild"/>.</summary>
    private readonly Action<string> openOutputFolder;

    /// <summary>Writes text to the system clipboard, for <see cref="CopyDiagnosticsCommand"/>.</summary>
    private readonly Action<string> setClipboardText;

    /// <summary>The repository root this page checks and builds.</summary>
    private readonly string repositoryRoot;

    /// <summary>The most recently loaded git status, or <see langword="null"/> when it could not be determined.</summary>
    private GitSourceStatus? gitStatus;

    /// <summary>The most recently loaded git status failure message, or <see langword="null"/> when git status loaded successfully.</summary>
    private string? gitStatusError;

    /// <summary>The backing field for <see cref="PreflightResults"/>.</summary>
    private IReadOnlyList<ToolchainCheckResult> preflightResults = [];

    /// <summary>The backing field for <see cref="IsCleanBuild"/>.</summary>
    private bool isCleanBuild;

    /// <summary>The backing field for <see cref="IsBuilding"/>.</summary>
    private bool isBuilding;

    /// <summary>The backing field for <see cref="IsAwaitingConfirmation"/>.</summary>
    private bool isAwaitingConfirmation;

    /// <summary>The backing field for <see cref="BuildBlockedReason"/>.</summary>
    private string? buildBlockedReason = CheckingEnvironmentReason;

    /// <summary>The backing field for <see cref="LastOutcome"/>.</summary>
    private BuildHistoryResult? lastOutcome;

    /// <summary>The backing field for <see cref="LastOutcomeMessage"/>.</summary>
    private string? lastOutcomeMessage;

    /// <summary>The cancellation source for the currently running build, or <see langword="null"/> when idle.</summary>
    private CancellationTokenSource? buildCancellation;

    /// <summary>The backing field for <see cref="ArchivePath"/>.</summary>
    private string? archivePath;

    /// <summary>The backing field for <see cref="ArchiveSha256"/>.</summary>
    private string? archiveSha256;

    /// <summary>The backing field for <see cref="IsShowingArchiveContents"/>.</summary>
    private bool isShowingArchiveContents;

    /// <summary>The backing field for <see cref="ArchiveEntries"/>.</summary>
    private IReadOnlyList<string> archiveEntries = [];

    /// <summary>The backing field for <see cref="RecentBuilds"/>.</summary>
    private IReadOnlyList<BuildHistoryEntry> recentBuilds;

    /// <summary>Initializes the page over its collaborators, starting in the "checking environment" state.</summary>
    /// <param name="preflightService">Checks the required build tools before a build is allowed to start.</param>
    /// <param name="gitStatusService">Reports the repository's branch, working tree, and remote sync state.</param>
    /// <param name="buildCoordinator">Builds and packages the production Adapter and Host.</param>
    /// <param name="buildHistoryStore">Persists and retrieves the Builder's recent build history.</param>
    /// <param name="settingsStore">Loads the Builder's persisted settings.</param>
    /// <param name="openOutputFolder">Opens a folder in the system file explorer, for <see cref="BuilderSettings.OpenOutputFolderAfterSuccessfulBuild"/>.</param>
    /// <param name="setClipboardText">Writes text to the system clipboard, for <see cref="CopyDiagnosticsCommand"/>.</param>
    /// <param name="repositoryRoot">The repository root this page checks and builds.</param>
    public BuildPageViewModel(
        IPreflightService preflightService,
        IGitStatusService gitStatusService,
        IAdapterHostBuildCoordinator buildCoordinator,
        IBuildHistoryStore buildHistoryStore,
        ISettingsStore settingsStore,
        Action<string> openOutputFolder,
        Action<string> setClipboardText,
        string repositoryRoot)
    {
        this.preflightService = preflightService;
        this.gitStatusService = gitStatusService;
        this.buildCoordinator = buildCoordinator;
        this.buildHistoryStore = buildHistoryStore;
        this.settingsStore = settingsStore;
        this.openOutputFolder = openOutputFolder;
        this.setClipboardText = setClipboardText;
        this.repositoryRoot = repositoryRoot;
        BuildCommand = new RelayCommand(OnBuild, () => CanBuild);
        ConfirmBuildCommand = new RelayCommand(OnConfirmBuild, () => IsAwaitingConfirmation);
        CancelConfirmationCommand = new RelayCommand(OnCancelConfirmation, () => IsAwaitingConfirmation);
        CancelCommand = new RelayCommand(OnCancel, () => IsBuilding);
        ViewArchiveContentsCommand = new RelayCommand(OnViewArchiveContents, () => ArchivePath is not null);
        RebuildCommand = new RelayCommand(OnRebuild, () => !IsBuilding && !IsAwaitingConfirmation);
        CopyDiagnosticsCommand = new RelayCommand(OnCopyDiagnostics);
        Stages = Enum.GetValues<BuildStage>().Select(stage => new BuildStageViewModel(stage)).ToList();
        Log = new LogViewModel { AutoScroll = settingsStore.Load().AutoScrollLogs };
        recentBuilds = buildHistoryStore.GetRecent();
    }

    /// <summary>Gets the build profile the Builder currently supports.</summary>
    public string Profile => "Release";

    /// <summary>Gets a short summary of the current profile and build options.</summary>
    public string BuildSummaryText => IsCleanBuild ? $"{Profile} · Clean build" : Profile;

    /// <summary>Gets or sets whether generated Adapter and Host build outputs are cleared before building.</summary>
    public bool IsCleanBuild
    {
        get => isCleanBuild;
        set
        {
            if (SetProperty(ref isCleanBuild, value))
            {
                OnPropertyChanged(nameof(BuildSummaryText));
            }
        }
    }

    /// <summary>Gets whether a build is currently running.</summary>
    public bool IsBuilding
    {
        get => isBuilding;
        private set
        {
            if (SetProperty(ref isBuilding, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    /// <summary>Gets whether the page is showing the uncommitted/unpushed build acknowledgement prompt.</summary>
    public bool IsAwaitingConfirmation
    {
        get => isAwaitingConfirmation;
        private set
        {
            if (SetProperty(ref isAwaitingConfirmation, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    /// <summary>
    /// Gets the reason a build cannot currently start, or <see langword="null"/> when building is
    /// allowed. Reflects the first required check that is not <see cref="ToolchainAvailability.Found"/>,
    /// or a git status error, in that order.
    /// </summary>
    public string? BuildBlockedReason
    {
        get => buildBlockedReason;
        private set
        {
            if (SetProperty(ref buildBlockedReason, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    /// <summary>Gets whether <see cref="BuildBlockedReason"/> currently has a value.</summary>
    public bool HasBuildBlockedReason => BuildBlockedReason is not null;

    /// <summary>Gets whether the ordinary Build/Cancel controls should show, as opposed to the acknowledgement prompt.</summary>
    public bool CanShowBuildActions => !IsAwaitingConfirmation;

    /// <summary>Gets whether the Build command can currently start a build.</summary>
    public bool CanBuild => !IsBuilding && !IsAwaitingConfirmation && BuildBlockedReason is null;

    /// <summary>Gets the outcome of the most recently finished build, or <see langword="null"/> before any build has finished.</summary>
    public BuildHistoryResult? LastOutcome
    {
        get => lastOutcome;
        private set
        {
            if (SetProperty(ref lastOutcome, value))
            {
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(ResultBannerText));
                OnPropertyChanged(nameof(FooterStatusText));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    /// <summary>Gets whether a build has finished, and a result banner should show.</summary>
    public bool HasResult => LastOutcome is not null;

    /// <summary>Gets whether the most recently finished build failed, for showing "Copy diagnostics" on the failure banner.</summary>
    public bool IsFailed => LastOutcome == BuildHistoryResult.Failed;

    /// <summary>Gets the result banner text for <see cref="LastOutcome"/>, or <see langword="null"/> before any build has finished.</summary>
    public string? ResultBannerText => LastOutcome switch
    {
        BuildHistoryResult.Succeeded => "Build succeeded.",
        BuildHistoryResult.Failed => "Build failed.",
        BuildHistoryResult.Cancelled => "Build cancelled.",
        _ => null,
    };

    /// <summary>Gets the failure message for the most recently finished build; <see langword="null"/> unless it failed.</summary>
    public string? LastOutcomeMessage
    {
        get => lastOutcomeMessage;
        private set => SetProperty(ref lastOutcomeMessage, value);
    }

    /// <summary>Gets the command that starts a build, or opens the acknowledgement prompt for a dirty/unpushed source.</summary>
    public RelayCommand BuildCommand { get; }

    /// <summary>Gets the command that starts a build after the acknowledgement prompt is accepted.</summary>
    public RelayCommand ConfirmBuildCommand { get; }

    /// <summary>Gets the command that dismisses the acknowledgement prompt without starting a build.</summary>
    public RelayCommand CancelConfirmationCommand { get; }

    /// <summary>Gets the command that cancels the currently running build.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Gets the currently running build's task, or <see langword="null"/> when idle. A test seam only.</summary>
    internal Task? RunningBuildTask { get; private set; }

    /// <summary>Loads the current preflight and git status, updating <see cref="BuildBlockedReason"/>.</summary>
    /// <param name="cancellationToken">The token used to cancel the outstanding checks.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        PreflightResults = await preflightService.CheckAllAsync(repositoryRoot, cancellationToken);
        await RefreshGitStatusAsync(cancellationToken);
        UpdateBuildBlockedReason();
    }

    /// <summary>Loads the current git status into <see cref="gitStatus"/>/<see cref="gitStatusError"/>, reporting a failure instead of throwing.</summary>
    /// <param name="cancellationToken">The token used to cancel the outstanding check.</param>
    private async Task RefreshGitStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            gitStatus = await gitStatusService.GetStatusAsync(repositoryRoot, cancellationToken);
            gitStatusError = null;
        }
        catch (InvalidOperationException exception)
        {
            gitStatus = null;
            gitStatusError = exception.Message;
        }
        finally
        {
            NotifyGitStatusChanged();
        }
    }

    /// <summary>Recomputes <see cref="BuildBlockedReason"/> from the latest <see cref="PreflightResults"/> and git status.</summary>
    private void UpdateBuildBlockedReason()
    {
        List<string> unavailableTools = preflightResults
            .Where(result => result.Availability != ToolchainAvailability.Found)
            .Select(result => result.ToolName)
            .ToList();

        BuildBlockedReason = unavailableTools.Count > 0
            ? $"Environment incomplete: {string.Join(", ", unavailableTools)}."
            : gitStatusError is not null
                ? $"Could not determine git status: {gitStatusError}"
                : null;
    }

    /// <summary>
    /// Starts a build immediately for a clean, pushed source; otherwise opens the acknowledgement
    /// prompt instead of starting (correction #5: uncommitted/unpushed source requires acknowledgement).
    /// </summary>
    private void OnBuild()
    {
        RunningBuildTask = StartBuildOrRequestConfirmation();
    }

    /// <summary>Starts a build after the acknowledgement prompt is accepted.</summary>
    private void OnConfirmBuild()
    {
        if (!IsAwaitingConfirmation)
        {
            return;
        }

        IsAwaitingConfirmation = false;
        RunningBuildTask = RunBuildAsync();
    }

    /// <summary>Dismisses the acknowledgement prompt without starting a build.</summary>
    private void OnCancelConfirmation()
    {
        IsAwaitingConfirmation = false;
    }

    /// <summary>Requests cancellation of the currently running build.</summary>
    private void OnCancel()
    {
        buildCancellation?.Cancel();
    }

    /// <summary>Re-checks preflight and git status, then starts a build (or opens the acknowledgement prompt) exactly as a fresh Build click would.</summary>
    private void OnRebuild()
    {
        RunningBuildTask = RebuildAsync();
    }

    /// <summary>
    /// Re-enters the full preflight+git gate before starting a build (correction #9: Rebuild must
    /// never bypass the gate, since the environment or source may have changed since the referenced
    /// build).
    /// </summary>
    private async Task RebuildAsync()
    {
        await InitializeAsync();
        if (StartBuildOrRequestConfirmation() is { } buildTask)
        {
            await buildTask;
        }
    }

    /// <summary>
    /// Starts a build for a clean, pushed source; otherwise opens the acknowledgement prompt and
    /// returns without building. Does nothing when a build cannot currently start.
    /// </summary>
    /// <returns>The running build's task, or <see langword="null"/> when blocked or awaiting confirmation.</returns>
    private Task? StartBuildOrRequestConfirmation()
    {
        if (!CanBuild)
        {
            return null;
        }

        if (gitStatus is { WorkingTreeState: WorkingTreeState.Dirty } or { RemoteSyncState: RemoteSyncState.NotPushed })
        {
            IsAwaitingConfirmation = true;
            return null;
        }

        return RunBuildAsync();
    }

    /// <summary>Runs the Adapter+Host build, recording its outcome.</summary>
    private async Task RunBuildAsync()
    {
        IsBuilding = true;
        LastOutcome = null;
        LastOutcomeMessage = null;
        ArchivePath = null;
        ArchiveSha256 = null;
        ArchiveEntries = [];
        IsShowingArchiveContents = false;
        ResetStages();
        Log.Clear();
        buildCancellation = new CancellationTokenSource();
        DateTimeOffset startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            AdapterHostBuildResult result = await buildCoordinator.BuildAsync(
                new AdapterHostBuildRequest(repositoryRoot),
                onOutput: Log.AppendLine,
                onStage: OnBuildStageEvent,
                buildCancellation.Token);
            // Computed before any property is set: if hashing the real archive fails, the build is
            // reported as Failed with no stale ArchivePath left over from a partially-applied success.
            string sha256 = ComputeSha256(result.ArchivePath);
            LastOutcome = BuildHistoryResult.Succeeded;
            ArchivePath = result.ArchivePath;
            ArchiveSha256 = sha256;
            TryOpenOutputFolder(result.ArchivePath);
        }
        catch (OperationCanceledException)
        {
            LastOutcome = BuildHistoryResult.Cancelled;
        }
        catch (Exception exception)
        {
            LastOutcome = BuildHistoryResult.Failed;
            LastOutcomeMessage = exception.Message;
        }
        finally
        {
            buildCancellation.Dispose();
            buildCancellation = null;
            RecordBuildHistory(startedAt, stopwatch.Elapsed);
            IsBuilding = false;
        }
    }

    /// <summary>Records the just-finished build and refreshes <see cref="RecentBuilds"/> from the store.</summary>
    /// <param name="startedAt">When this build started.</param>
    /// <param name="duration">How long this build ran before reaching its final outcome.</param>
    private void RecordBuildHistory(DateTimeOffset startedAt, TimeSpan duration)
    {
        string? failedStage = LastOutcome == BuildHistoryResult.Failed
            ? Stages.FirstOrDefault(stage => stage.Status == BuildStageStatus.Failed)?.DisplayName
            : null;

        buildHistoryStore.Add(new BuildHistoryEntry(
            startedAt,
            LastOutcome!.Value,
            TryReadRepositoryVersion(),
            Profile,
            duration,
            ArchivePath,
            failedStage,
            ArchiveSha256,
            Note: null));

        RecentBuilds = buildHistoryStore.GetRecent();
    }

    /// <summary>Reads the repository's VERSION file, reporting "unknown" instead of throwing when it cannot be read.</summary>
    private string TryReadRepositoryVersion()
    {
        try
        {
            return File.ReadAllText(Path.Combine(repositoryRoot, "VERSION")).Trim();
        }
        catch (IOException)
        {
            return "unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    /// <summary>Notifies bound commands and computed properties that depend on <see cref="CanBuild"/>'s inputs.</summary>
    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(HasBuildBlockedReason));
        OnPropertyChanged(nameof(CanShowBuildActions));
        OnPropertyChanged(nameof(FooterStatusText));
        BuildCommand.RaiseCanExecuteChanged();
        ConfirmBuildCommand.RaiseCanExecuteChanged();
        CancelConfirmationCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        RebuildCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Gets the pipeline's stages in order, one segment per <see cref="BuildStage"/> value.</summary>
    public IReadOnlyList<BuildStageViewModel> Stages { get; }

    /// <summary>Gets the number of stages that have actually succeeded in the current or most recent build.</summary>
    public int CompletedStageCount => Stages.Count(stage => stage.Status == BuildStageStatus.Succeeded);

    /// <summary>
    /// Gets an honest "Stage N of 8" summary reflecting <see cref="CompletedStageCount"/> exactly --
    /// never an invented percentage (correction #3).
    /// </summary>
    public string StageProgressText => $"Stage {CompletedStageCount} of {Stages.Count}";

    /// <summary>Resets every stage segment back to Pending before a new build starts.</summary>
    private void ResetStages()
    {
        foreach (BuildStageViewModel stage in Stages)
        {
            stage.Reset();
        }

        NotifyStageProgressChanged();
    }

    /// <summary>Applies one reported stage transition to its matching segment.</summary>
    /// <param name="stageEvent">The reported transition.</param>
    private void OnBuildStageEvent(BuildStageEvent stageEvent)
    {
        BuildStageViewModel? stage = Stages.FirstOrDefault(candidate => candidate.Stage == stageEvent.Stage);
        stage?.Apply(stageEvent);
        NotifyStageProgressChanged();
    }

    /// <summary>Notifies bound properties that summarize the stage segments as a whole.</summary>
    private void NotifyStageProgressChanged()
    {
        OnPropertyChanged(nameof(CompletedStageCount));
        OnPropertyChanged(nameof(StageProgressText));
    }

    /// <summary>Gets the Build page's log panel, kept alive for the application's lifetime.</summary>
    public LogViewModel Log { get; }

    /// <summary>Gets the produced archive's path on success; <see langword="null"/> otherwise.</summary>
    public string? ArchivePath
    {
        get => archivePath;
        private set
        {
            if (SetProperty(ref archivePath, value))
            {
                OnPropertyChanged(nameof(HasArchivePath));
                ViewArchiveContentsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether <see cref="ArchivePath"/> currently has a value.</summary>
    public bool HasArchivePath => ArchivePath is not null;

    /// <summary>Gets the produced archive's SHA-256 hash, as lowercase hex, on success; <see langword="null"/> otherwise.</summary>
    public string? ArchiveSha256
    {
        get => archiveSha256;
        private set => SetProperty(ref archiveSha256, value);
    }

    /// <summary>Gets whether the archive's real contents are currently shown.</summary>
    public bool IsShowingArchiveContents
    {
        get => isShowingArchiveContents;
        private set => SetProperty(ref isShowingArchiveContents, value);
    }

    /// <summary>Gets the produced archive's real entry names, read directly from the ZIP; empty until <see cref="ViewArchiveContentsCommand"/> runs.</summary>
    public IReadOnlyList<string> ArchiveEntries
    {
        get => archiveEntries;
        private set => SetProperty(ref archiveEntries, value);
    }

    /// <summary>Gets the command that toggles showing the produced archive's real contents.</summary>
    public RelayCommand ViewArchiveContentsCommand { get; }

    /// <summary>Computes a file's SHA-256 hash as lowercase hex.</summary>
    /// <param name="filePath">The file to hash.</param>
    private static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Toggles the archive contents display; reads the real ZIP's entries directly (no second
    /// hardcoded package-layout list) the first time it is shown.
    /// </summary>
    private void OnViewArchiveContents()
    {
        if (ArchivePath is null)
        {
            return;
        }

        if (IsShowingArchiveContents)
        {
            IsShowingArchiveContents = false;
            return;
        }

        using ZipArchive archive = ZipFile.OpenRead(ArchivePath);
        ArchiveEntries = archive.Entries.Select(entry => entry.FullName).ToList();
        IsShowingArchiveContents = true;
    }

    /// <summary>Gets the retained build history, most recent first.</summary>
    public IReadOnlyList<BuildHistoryEntry> RecentBuilds
    {
        get => recentBuilds;
        private set => SetProperty(ref recentBuilds, value);
    }

    /// <summary>
    /// Gets the command that re-checks preflight and git status and then builds (or opens the
    /// acknowledgement prompt), the same as a fresh Build click -- never bypassing the gate
    /// (correction #9).
    /// </summary>
    public RelayCommand RebuildCommand { get; }

    /// <summary>
    /// Opens the archive's containing folder when <see cref="BuilderSettings.OpenOutputFolderAfterSuccessfulBuild"/>
    /// is enabled, only ever on a successful build (correction #12 -- never on failure or cancellation).
    /// A failure here is a convenience-action failure, not a build failure, and never changes
    /// <see cref="LastOutcome"/>.
    /// </summary>
    /// <param name="archivePath">The successful build's produced archive path.</param>
    private void TryOpenOutputFolder(string archivePath)
    {
        if (!settingsStore.Load().OpenOutputFolderAfterSuccessfulBuild)
        {
            return;
        }

        string? folderPath = Path.GetDirectoryName(archivePath);
        if (folderPath is null)
        {
            return;
        }

        try
        {
            openOutputFolder(folderPath);
        }
        catch (Exception)
        {
            // Opening the output folder is a convenience action; a failure here must not affect the
            // build's own reported outcome.
        }
    }

    /// <summary>Gets whether the git status banner has anything to show; false while git status has not loaded or could not be determined.</summary>
    public bool HasGitBanner => gitStatus is not null;

    /// <summary>
    /// Gets the git status banner's first line, reflecting one of the four real states (correction #1):
    /// dirty working tree, unpushed commits, committed-but-unverified, or fully ready. Never claims
    /// "pushed" from a stale local tracking ref -- only after <see cref="gitStatusService"/>'s own
    /// successful remote check.
    /// </summary>
    public string? GitBannerLine1 => gitStatus switch
    {
        null => null,
        { WorkingTreeState: WorkingTreeState.Dirty } => "⚠ Uncommitted changes",
        { RemoteSyncState: RemoteSyncState.NotPushed } => "⚠ Unpushed commits",
        { RemoteSyncState: RemoteSyncState.CouldNotVerify } => "✓ All local changes are committed",
        { RemoteSyncState: RemoteSyncState.Pushed } => "✓ Ready to build",
        _ => null,
    };

    /// <summary>
    /// Gets the git status banner's second line, present only for the "committed but remote unverified"
    /// state -- never collapsed into a single contradictory "pushed" claim (correction #1).
    /// </summary>
    public string? GitBannerLine2 => gitStatus is { WorkingTreeState: WorkingTreeState.Clean, RemoteSyncState: RemoteSyncState.CouldNotVerify }
        ? "⚠ Remote status could not be confirmed"
        : null;

    /// <summary>Gets whether <see cref="GitBannerLine2"/> currently has a value.</summary>
    public bool HasGitBannerLine2 => GitBannerLine2 is not null;

    /// <summary>Gets whether the git banner represents the fully-ready state: a clean tree, verified pushed.</summary>
    public bool IsGitReady => gitStatus is { WorkingTreeState: WorkingTreeState.Clean, RemoteSyncState: RemoteSyncState.Pushed };

    /// <summary>Gets the current branch name, shown only under "See details" -- never a raw commit SHA in the normal UI (correction #1).</summary>
    public string? GitBranch => gitStatus?.Branch;

    /// <summary>Gets the full current commit SHA, shown only under "See details" (correction #1).</summary>
    public string? GitCommitSha => gitStatus?.CommitSha;

    /// <summary>
    /// Gets the footer status text reflecting the Build page's real current state (correction #7):
    /// Building while a build runs; Failed/Cancelled/Complete for the most recent finished build;
    /// "Environment incomplete" while idle with a missing required check; Ready only while idle with
    /// everything passing. Never a static "Ready".
    /// </summary>
    public string FooterStatusText => (IsBuilding, LastOutcome, HasBuildBlockedReason) switch
    {
        (true, _, _) => "Building",
        (false, BuildHistoryResult.Failed, _) => "Failed",
        (false, BuildHistoryResult.Cancelled, _) => "Cancelled",
        (false, BuildHistoryResult.Succeeded, _) => "Complete",
        (false, null, true) => "Environment incomplete",
        (false, null, false) => "Ready",
        _ => "Ready",
    };

    /// <summary>Notifies bound properties that summarize the current git status.</summary>
    private void NotifyGitStatusChanged()
    {
        OnPropertyChanged(nameof(HasGitBanner));
        OnPropertyChanged(nameof(GitBannerLine1));
        OnPropertyChanged(nameof(GitBannerLine2));
        OnPropertyChanged(nameof(HasGitBannerLine2));
        OnPropertyChanged(nameof(IsGitReady));
        OnPropertyChanged(nameof(GitBranch));
        OnPropertyChanged(nameof(GitCommitSha));
    }

    /// <summary>Gets the most recently loaded preflight results, in preflight order.</summary>
    public IReadOnlyList<ToolchainCheckResult> PreflightResults
    {
        get => preflightResults;
        private set => SetProperty(ref preflightResults, value);
    }

    /// <summary>Gets the command that copies a plain-text diagnostics report -- environment checks, git status, and the last build -- to the clipboard.</summary>
    public RelayCommand CopyDiagnosticsCommand { get; }

    /// <summary>Formats the current diagnostics report and writes it to the clipboard.</summary>
    private void OnCopyDiagnostics()
    {
        setClipboardText(DiagnosticsFormatter.Format(PreflightResults, gitStatus, gitStatusError, LastOutcome, LastOutcomeMessage));
    }
}
