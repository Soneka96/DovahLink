using System.ComponentModel;
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

    /// <summary>The shared preflight-and-git-status refresh both the Build and Environment pages trigger.</summary>
    private readonly IEnvironmentStore environmentStore;

    /// <summary>The shared git status both the Build and Environment pages read and refresh.</summary>
    private readonly IGitStatusStore gitStatusStore;

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

    /// <summary>The shared repository root this page checks and builds.</summary>
    private readonly IRepositoryContext repositoryContext;

    /// <summary>The backing field for <see cref="PreflightResults"/>.</summary>
    private IReadOnlyList<ToolchainCheckResult> preflightResults = [];

    /// <summary>The backing field for <see cref="IsCleanBuild"/>.</summary>
    private bool isCleanBuild;

    /// <summary>The backing field for <see cref="IsBuilding"/>.</summary>
    private bool isBuilding;

    /// <summary>The backing field for <see cref="IsCancelling"/>.</summary>
    private bool isCancelling;

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

    /// <summary>The backing field for <see cref="IsShowingAllRecentBuilds"/>.</summary>
    private bool isShowingAllRecentBuilds;

    /// <summary>The backing field for <see cref="BuildNote"/>.</summary>
    private string? buildNote;

    /// <summary>The backing field for <see cref="ArchiveSizeText"/>.</summary>
    private string? archiveSizeText;

    /// <summary>The backing field for <see cref="LastBuildDuration"/>.</summary>
    private TimeSpan? lastBuildDuration;

    /// <summary>The number of most recent builds shown before "Show all" is used; see <see cref="VisibleRecentBuilds"/>.</summary>
    private const int RecentBuildsPreviewCount = 3;

    /// <summary>The backing field for <see cref="SelectedProfile"/>.</summary>
    private BuildProfile selectedProfile = BuildProfile.Release;

    /// <summary>Initializes the page over its collaborators, starting in the "checking environment" state.</summary>
    /// <param name="environmentStore">The shared preflight-and-git-status refresh both the Build and Environment pages trigger.</param>
    /// <param name="gitStatusStore">The shared git status both the Build and Environment pages read and refresh.</param>
    /// <param name="buildCoordinator">Builds and packages the production Adapter and Host.</param>
    /// <param name="buildHistoryStore">Persists and retrieves the Builder's recent build history.</param>
    /// <param name="settingsStore">Loads the Builder's persisted settings.</param>
    /// <param name="openOutputFolder">Opens a folder in the system file explorer, for <see cref="BuilderSettings.OpenOutputFolderAfterSuccessfulBuild"/>.</param>
    /// <param name="setClipboardText">Writes text to the system clipboard, for <see cref="CopyDiagnosticsCommand"/>.</param>
    /// <param name="repositoryContext">The shared repository root this page checks and builds.</param>
    public BuildPageViewModel(
        IEnvironmentStore environmentStore,
        IGitStatusStore gitStatusStore,
        IAdapterHostBuildCoordinator buildCoordinator,
        IBuildHistoryStore buildHistoryStore,
        ISettingsStore settingsStore,
        Action<string> openOutputFolder,
        Action<string> setClipboardText,
        IRepositoryContext repositoryContext)
    {
        this.environmentStore = environmentStore;
        this.gitStatusStore = gitStatusStore;
        this.buildCoordinator = buildCoordinator;
        this.buildHistoryStore = buildHistoryStore;
        this.settingsStore = settingsStore;
        this.openOutputFolder = openOutputFolder;
        this.setClipboardText = setClipboardText;
        this.repositoryContext = repositoryContext;
        gitStatusStore.PropertyChanged += OnGitStatusStoreChanged;
        environmentStore.PropertyChanged += OnEnvironmentStoreChanged;
        BuildCommand = new RelayCommand(OnBuild, () => CanBuild);
        ConfirmBuildCommand = new RelayCommand(OnConfirmBuild, () => IsAwaitingConfirmation);
        CancelConfirmationCommand = new RelayCommand(OnCancelConfirmation, () => IsAwaitingConfirmation);
        CancelCommand = new RelayCommand(OnCancel, () => IsBuilding && !IsCancelling);
        ViewArchiveContentsCommand = new RelayCommand(OnViewArchiveContents, () => ArchivePath is not null);
        NewBuildCommand = new RelayCommand(OnNewBuild, () => !IsBuilding);
        CopyDiagnosticsCommand = new RelayCommand(OnCopyDiagnostics);
        OpenArchiveFolderCommand = new RelayCommand(OnOpenArchiveFolder, () => ArchivePath is not null);
        CopyArchivePathCommand = new RelayCommand(OnCopyArchivePath, () => ArchivePath is not null);
        ToggleShowAllRecentBuildsCommand = new RelayCommand(() => IsShowingAllRecentBuilds = !IsShowingAllRecentBuilds);
        Stages = Enum.GetValues<BuildStage>().Select(stage => new BuildStageViewModel(stage)).ToList();
        Log = new LogViewModel { AutoScroll = settingsStore.Load().AutoScrollLogs };
        recentBuilds = buildHistoryStore.GetRecent();
    }

    /// <summary>Gets every build profile the picker offers.</summary>
    public IReadOnlyList<BuildProfile> AvailableProfiles { get; } = Enum.GetValues<BuildProfile>();

    /// <summary>Gets or sets the build profile the next build targets.</summary>
    public BuildProfile SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (SetProperty(ref selectedProfile, value))
            {
                OnPropertyChanged(nameof(Profile));
                OnPropertyChanged(nameof(BuildSummaryText));
                OnPropertyChanged(nameof(AdapterSummaryText));
                OnPropertyChanged(nameof(HostSummaryText));
            }
        }
    }

    /// <summary>Gets <see cref="SelectedProfile"/>'s display name, for the build summary and recorded build history.</summary>
    public string Profile => SelectedProfile.ToString();

    /// <summary>Gets a short summary of the current profile and build options.</summary>
    public string BuildSummaryText => IsCleanBuild ? $"{Profile} · Clean build" : Profile;

    /// <summary>Gets the Adapter build summary row's text: <see cref="SelectedProfile"/>'s dotnet configuration and architecture.</summary>
    public string AdapterSummaryText => $"{SelectedProfile.ToDotnetConfiguration()} x64";

    /// <summary>Gets the Host build summary row's text: the fixed publish trait plus <see cref="SelectedProfile"/>'s dotnet configuration.</summary>
    public string HostSummaryText => $"self-contained win-x64 ({SelectedProfile.ToDotnetConfiguration()})";

    /// <summary>Gets the repository's product version, read fresh from its VERSION file; "unknown" when it cannot be read.</summary>
    public string RepositoryVersion => TryReadRepositoryVersion();

    /// <summary>Gets or sets an optional local note to attach to the next build that is started.</summary>
    public string? BuildNote
    {
        get => buildNote;
        set => SetProperty(ref buildNote, value);
    }

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
                NotifyShowIdleFormChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether cancellation has been requested and is being carried out; the Cancel button is
    /// disabled during this transient state, between Building and Cancelled (correction #6).
    /// </summary>
    public bool IsCancelling
    {
        get => isCancelling;
        private set
        {
            if (SetProperty(ref isCancelling, value))
            {
                NotifyCommandStateChanged();
                NotifyShowIdleFormChanged();
            }
        }
    }

    /// <summary>Gets the message shown while cancellation is in progress, or <see langword="null"/> otherwise.</summary>
    public string? CancellingMessage => IsCancelling ? "Stopping build… Terminating active build processes." : null;

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
                NotifyShowIdleFormChanged();
            }
        }
    }

    /// <summary>Gets whether a build has finished, and a result banner should show.</summary>
    public bool HasResult => LastOutcome is not null;

    /// <summary>
    /// Gets whether the idle build form (summary, clean-build toggle, note, Build button) should show,
    /// as opposed to the pipeline/console/outcome view: only before any build has run this session and
    /// while nothing is currently building or cancelling.
    /// </summary>
    public bool ShowIdleForm => !IsBuilding && !IsCancelling && !HasResult;

    /// <summary>Gets whether the pipeline/console/outcome view should show, as the complement of <see cref="ShowIdleForm"/>.</summary>
    public bool ShowActiveOrResult => !ShowIdleForm;

    /// <summary>Notifies the properties that switch between the idle form and the active/result view.</summary>
    private void NotifyShowIdleFormChanged()
    {
        OnPropertyChanged(nameof(ShowIdleForm));
        OnPropertyChanged(nameof(ShowActiveOrResult));
    }

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
        await environmentStore.RefreshAsync(cancellationToken);
        UpdateBuildBlockedReason();
    }

    /// <summary>
    /// Relays a change on the shared <see cref="gitStatusStore"/> to this page's own derived
    /// properties, since a refresh triggered from the Environment page's Recheck must also be
    /// reflected here (for example the sidebar's git-attention indicator).
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused change details; either property changing recomputes both.</param>
    private void OnGitStatusStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateBuildBlockedReason();
        NotifyGitStatusChanged();
    }

    /// <summary>
    /// Relays a change on the shared <see cref="environmentStore"/> to this page's own preflight
    /// results, since a refresh triggered from the Environment page's Recheck must also be reflected
    /// here.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused change details; every change recomputes the same derived state.</param>
    private void OnEnvironmentStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        PreflightResults = environmentStore.PreflightResults;
        UpdateBuildBlockedReason();
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
            : gitStatusStore.StatusError is not null
                ? $"Could not determine git status: {gitStatusStore.StatusError}"
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

    /// <summary>
    /// Requests cancellation of the currently running build and enters the Cancelling transient state;
    /// does nothing when no build is running or cancellation was already requested (correction #6).
    /// </summary>
    private void OnCancel()
    {
        if (!IsBuilding || IsCancelling)
        {
            return;
        }

        IsCancelling = true;
        buildCancellation?.Cancel();
    }

    /// <summary>
    /// Returns to the idle build form and refreshes preflight/git status in the background, doing
    /// nothing while a build is currently running. Guards <see cref="IsBuilding"/> itself rather than
    /// relying on <see cref="NewBuildCommand"/>'s own eligibility check, since <see
    /// cref="RelayCommand.Execute"/> does not re-verify it: without this guard, a direct call here
    /// while a build is in flight would reset the log/stages/archive state that build is still
    /// writing to.
    /// </summary>
    private void OnNewBuild()
    {
        if (IsBuilding)
        {
            return;
        }

        ResetBuildResultState();
        RunningBuildTask = InitializeAsync();
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

        if (GitNeedsAttention)
        {
            IsAwaitingConfirmation = true;
            return null;
        }

        return RunBuildAsync();
    }

    /// <summary>Runs the Adapter+Host build, recording its outcome.</summary>
    private async Task RunBuildAsync()
    {
        string? noteForThisBuild = string.IsNullOrWhiteSpace(BuildNote) ? null : BuildNote.Trim();
        BuildNote = null;
        IsBuilding = true;
        ResetBuildResultState();
        buildCancellation = new CancellationTokenSource();
        DateTimeOffset startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (IsCleanBuild)
            {
                Log.AppendLine("Clean build: clearing generated Adapter and Host build outputs...");
                await Task.Run(CleanBuildOutputs, buildCancellation.Token);
            }

            AdapterHostBuildResult result = await buildCoordinator.BuildAsync(
                new AdapterHostBuildRequest(repositoryContext.RepositoryRoot, SelectedProfile, settingsStore.Load().OutputPath),
                onOutput: Log.AppendLine,
                onStage: OnBuildStageEvent,
                buildCancellation.Token);
            // Computed before any property is set: if hashing the real archive fails, the build is
            // reported as Failed with no stale ArchivePath left over from a partially-applied success.
            string sha256 = ComputeSha256(result.ArchivePath);
            LastOutcome = BuildHistoryResult.Succeeded;
            ArchivePath = result.ArchivePath;
            ArchiveSha256 = sha256;
            ArchiveSizeText = FormatFileSize(new FileInfo(result.ArchivePath).Length);
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
            LastBuildDuration = stopwatch.Elapsed;
            RecordBuildHistory(startedAt, stopwatch.Elapsed, noteForThisBuild);
            IsBuilding = false;
            IsCancelling = false;
        }
    }

    /// <summary>Clears the previous build's outcome, archive, stages, and log back to a fresh, no-result state.</summary>
    private void ResetBuildResultState()
    {
        LastOutcome = null;
        LastOutcomeMessage = null;
        LastBuildDuration = null;
        ArchivePath = null;
        ArchiveSha256 = null;
        ArchiveSizeText = null;
        ArchiveEntries = [];
        IsShowingArchiveContents = false;
        ResetStages();
        Log.Clear();
    }

    /// <summary>Formats a byte count as a human-readable KB/MB size.</summary>
    /// <param name="bytes">The size in bytes.</param>
    private static string FormatFileSize(long bytes) =>
        bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / (1024.0 * 1024.0):0.#} MB";

    /// <summary>Records the just-finished build and refreshes <see cref="RecentBuilds"/> from the store.</summary>
    /// <param name="startedAt">When this build started.</param>
    /// <param name="duration">How long this build ran before reaching its final outcome.</param>
    /// <param name="note">The optional local note the user attached to this build.</param>
    private void RecordBuildHistory(DateTimeOffset startedAt, TimeSpan duration, string? note)
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
            note));

        RecentBuilds = buildHistoryStore.GetRecent();
    }

    /// <summary>Reads the repository's VERSION file, reporting "unknown" instead of throwing when it cannot be read.</summary>
    private string TryReadRepositoryVersion()
    {
        try
        {
            return File.ReadAllText(Path.Combine(repositoryContext.RepositoryRoot, "VERSION")).Trim();
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
        OnPropertyChanged(nameof(CancellingMessage));
        BuildCommand.RaiseCanExecuteChanged();
        ConfirmBuildCommand.RaiseCanExecuteChanged();
        CancelConfirmationCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        NewBuildCommand.RaiseCanExecuteChanged();
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
                OnPropertyChanged(nameof(ArchiveFileName));
                ViewArchiveContentsCommand.RaiseCanExecuteChanged();
                OpenArchiveFolderCommand.RaiseCanExecuteChanged();
                CopyArchivePathCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether <see cref="ArchivePath"/> currently has a value.</summary>
    public bool HasArchivePath => ArchivePath is not null;

    /// <summary>Gets the produced archive's file name on success; <see langword="null"/> otherwise.</summary>
    public string? ArchiveFileName => ArchivePath is null ? null : Path.GetFileName(ArchivePath);

    /// <summary>Gets the produced archive's human-readable file size on success; <see langword="null"/> otherwise.</summary>
    public string? ArchiveSizeText
    {
        get => archiveSizeText;
        private set => SetProperty(ref archiveSizeText, value);
    }

    /// <summary>Gets how long the most recently finished build ran; <see langword="null"/> before any build has finished.</summary>
    public TimeSpan? LastBuildDuration
    {
        get => lastBuildDuration;
        private set
        {
            if (SetProperty(ref lastBuildDuration, value))
            {
                OnPropertyChanged(nameof(LastBuildDurationText));
            }
        }
    }

    /// <summary>Gets a "Finished in Ns." summary of <see cref="LastBuildDuration"/>; <see langword="null"/> before any build has finished.</summary>
    public string? LastBuildDurationText => LastBuildDuration is { } duration ? $"Finished in {duration.TotalSeconds:0.0}s." : null;

    /// <summary>Gets the command that opens the produced archive's containing folder.</summary>
    public RelayCommand OpenArchiveFolderCommand { get; }

    /// <summary>Gets the command that copies the produced archive's path to the clipboard.</summary>
    public RelayCommand CopyArchivePathCommand { get; }

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
        private set
        {
            if (SetProperty(ref recentBuilds, value))
            {
                OnPropertyChanged(nameof(VisibleRecentBuilds));
                OnPropertyChanged(nameof(HasMoreRecentBuilds));
            }
        }
    }

    /// <summary>Gets whether "Show all" has been used to reveal every retained build, rather than just the most recent ones.</summary>
    public bool IsShowingAllRecentBuilds
    {
        get => isShowingAllRecentBuilds;
        private set
        {
            if (SetProperty(ref isShowingAllRecentBuilds, value))
            {
                OnPropertyChanged(nameof(VisibleRecentBuilds));
            }
        }
    }

    /// <summary>Gets the recent builds currently shown: the most recent few, or all of them once <see cref="IsShowingAllRecentBuilds"/> is set.</summary>
    public IReadOnlyList<BuildHistoryEntry> VisibleRecentBuilds =>
        IsShowingAllRecentBuilds ? RecentBuilds : RecentBuilds.Take(RecentBuildsPreviewCount).ToList();

    /// <summary>Gets whether there are more retained builds than <see cref="VisibleRecentBuilds"/> currently shows.</summary>
    public bool HasMoreRecentBuilds => RecentBuilds.Count > RecentBuildsPreviewCount;

    /// <summary>Gets the command that toggles between showing the most recent few builds and every retained build.</summary>
    public RelayCommand ToggleShowAllRecentBuildsCommand { get; }

    /// <summary>
    /// Gets the command that returns from a finished build's result view to the idle build form
    /// without starting a build. Refreshes preflight and git status in the background so the form's
    /// gate reflects current reality by the time the user presses Build themselves, rather than
    /// trusting a check that may be stale by however long the previous build took.
    /// </summary>
    public RelayCommand NewBuildCommand { get; }

    /// <summary>
    /// Opens the archive's containing folder when <see cref="BuilderSettings.OpenOutputFolderAfterSuccessfulBuild"/>
    /// is enabled, only ever on a successful build (correction #12 -- never on failure or cancellation).
    /// A failure here is a convenience-action failure, not a build failure, and never changes
    /// <see cref="LastOutcome"/>.
    /// </summary>
    /// <param name="archivePath">The successful build's produced archive path.</param>
    private void TryOpenOutputFolder(string archivePath)
    {
        if (settingsStore.Load().OpenOutputFolderAfterSuccessfulBuild)
        {
            OpenContainingFolderSafely(archivePath);
        }
    }

    /// <summary>Opens the produced archive's containing folder; does nothing when there is no archive.</summary>
    private void OnOpenArchiveFolder()
    {
        if (ArchivePath is not null)
        {
            OpenContainingFolderSafely(ArchivePath);
        }
    }

    /// <summary>
    /// Opens <paramref name="filePath"/>'s containing folder in the system file explorer. A failure
    /// here is a convenience-action failure, not a build failure, and never changes any reported state.
    /// </summary>
    /// <param name="filePath">The file whose containing folder should be opened.</param>
    private void OpenContainingFolderSafely(string filePath)
    {
        string? folderPath = Path.GetDirectoryName(filePath);
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
            // Opening the output folder is a convenience action; a failure here must not affect any reported state.
        }
    }

    /// <summary>Copies the produced archive's path to the clipboard; does nothing when there is no archive.</summary>
    private void OnCopyArchivePath()
    {
        if (ArchivePath is not null)
        {
            setClipboardText(ArchivePath);
        }
    }

    /// <summary>Gets the current branch name, shown only in the footer status strip.</summary>
    public string? GitBranch => gitStatusStore.Status?.Branch;

    /// <summary>Gets a short source-state label for the footer status strip: Pushed, Local changes, Not pushed, Committed (unverified), or Unverified while git status has not loaded.</summary>
    public string GitFooterStateText => gitStatusStore.Status switch
    {
        { WorkingTreeState: WorkingTreeState.Dirty } => "Local changes",
        { RemoteSyncState: RemoteSyncState.NotPushed } => "Not pushed",
        { RemoteSyncState: RemoteSyncState.CouldNotVerify } => "Committed, unverified",
        { RemoteSyncState: RemoteSyncState.Pushed } => "Pushed",
        _ => "Unverified",
    };

    /// <summary>
    /// Gets whether the current git status needs the maintainer's attention before building: a dirty
    /// working tree, or commits not yet pushed to the upstream remote. Never true while git status has
    /// not loaded or could not be determined -- that failure is already surfaced via
    /// <see cref="BuildBlockedReason"/>.
    /// </summary>
    public bool GitNeedsAttention => gitStatusStore.Status is { WorkingTreeState: WorkingTreeState.Dirty } or { RemoteSyncState: RemoteSyncState.NotPushed };

    /// <summary>
    /// Gets the footer status text reflecting the Build page's real current state (correction #7):
    /// Building while a build runs; Cancelling during the transient shutdown between Building and
    /// Cancelled; Failed/Cancelled/Complete for the most recent finished build; "Environment incomplete"
    /// while idle with a missing required check; Ready only while idle with everything passing. Never a
    /// static "Ready".
    /// </summary>
    public string FooterStatusText => (IsBuilding, IsCancelling, LastOutcome, HasBuildBlockedReason) switch
    {
        (true, true, _, _) => "Cancelling",
        (true, false, _, _) => "Building",
        (false, _, BuildHistoryResult.Failed, _) => "Failed",
        (false, _, BuildHistoryResult.Cancelled, _) => "Cancelled",
        (false, _, BuildHistoryResult.Succeeded, _) => "Complete",
        (false, _, null, true) => "Environment incomplete",
        (false, _, null, false) => "Ready",
        _ => "Ready",
    };

    /// <summary>Notifies bound properties that summarize the current git status.</summary>
    private void NotifyGitStatusChanged()
    {
        OnPropertyChanged(nameof(GitBranch));
        OnPropertyChanged(nameof(GitFooterStateText));
        OnPropertyChanged(nameof(GitNeedsAttention));
    }

    /// <summary>Gets the most recently loaded preflight results, in preflight order.</summary>
    public IReadOnlyList<ToolchainCheckResult> PreflightResults
    {
        get => preflightResults;
        private set
        {
            if (SetProperty(ref preflightResults, value))
            {
                OnPropertyChanged(nameof(EnvironmentSummaryText));
                OnPropertyChanged(nameof(HasMissingRequiredTool));
                OnPropertyChanged(nameof(DotNetVersionDetail));
            }
        }
    }

    /// <summary>Gets an honest "N of M checks passed" summary of <see cref="PreflightResults"/>.</summary>
    public string EnvironmentSummaryText =>
        $"{PreflightResults.Count(result => result.Availability == ToolchainAvailability.Found)} of {PreflightResults.Count} checks passed";

    /// <summary>Gets whether any required build tool is currently unavailable.</summary>
    public bool HasMissingRequiredTool => PreflightResults.Any(result => result.Availability != ToolchainAvailability.Found);

    /// <summary>Gets the resolved .NET SDK version detail for the footer status strip, or <see langword="null"/> before preflight has loaded.</summary>
    public string? DotNetVersionDetail => PreflightResults.FirstOrDefault(result => result.ToolName == ".NET SDK")?.Detail;

    /// <summary>Gets the command that copies a plain-text diagnostics report -- environment checks, git status, and the last build -- to the clipboard.</summary>
    public RelayCommand CopyDiagnosticsCommand { get; }

    /// <summary>Formats the current diagnostics report and writes it to the clipboard.</summary>
    private void OnCopyDiagnostics()
    {
        setClipboardText(DiagnosticsFormatter.Format(PreflightResults, gitStatusStore.Status, gitStatusStore.StatusError, LastOutcome, LastOutcomeMessage));
    }

    /// <summary>
    /// Clears generated Adapter and Host build outputs before building: only
    /// <c>adapter/build/{preset}/</c> and the effective output root's <c>{publish,package}</c>
    /// subfolders are deleted -- confirmed against the real repository layout when the output root is
    /// the default <c>tooling/out</c>, or the folder the user explicitly chose through
    /// <see cref="SettingsPageViewModel.BrowseOutputPathCommand"/> when an override is set -- never
    /// vcpkg's shared package cache (correction #10).
    /// </summary>
    private void CleanBuildOutputs()
    {
        string repositoryRoot = repositoryContext.RepositoryRoot;
        DeleteDirectoryIfExists(Path.Combine(repositoryRoot, "adapter", "build", SelectedProfile.ToCMakePreset()));
        string outputRoot = settingsStore.Load().OutputPath ?? SelectedProfile.ToOutputRoot(repositoryRoot);
        DeleteDirectoryIfExists(Path.Combine(outputRoot, "publish"));
        DeleteDirectoryIfExists(Path.Combine(outputRoot, "package"));
    }

    /// <summary>Deletes a directory and its contents, if it exists.</summary>
    /// <param name="path">The directory to delete.</param>
    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
