using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
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
public interface IBuildPageViewModel : INotifyPropertyChanged
{
    /// <summary>Gets every build profile the picker offers.</summary>
    IReadOnlyList<BuildProfile> AvailableProfiles { get; }

    /// <summary>Gets or sets the build profile the next build targets.</summary>
    BuildProfile SelectedProfile { get; set; }

    /// <summary>Gets <see cref="SelectedProfile"/>'s display name, for the build summary and recorded build history.</summary>
    string Profile { get; }

    /// <summary>Gets a short summary of the current profile and build options.</summary>
    string BuildSummaryText { get; }

    /// <summary>Gets the Adapter build summary row's text: <see cref="SelectedProfile"/>'s dotnet configuration and architecture.</summary>
    string AdapterSummaryText { get; }

    /// <summary>Gets the Host build summary row's text: the fixed publish trait plus <see cref="SelectedProfile"/>'s dotnet configuration.</summary>
    string HostSummaryText { get; }

    /// <summary>Gets the repository's product version, read fresh from its VERSION file; "unknown" when it cannot be read.</summary>
    string RepositoryVersion { get; }

    /// <summary>Gets or sets an optional local note to attach to the next build that is started.</summary>
    string? BuildNote { get; set; }

    /// <summary>Gets or sets whether generated Adapter and Host build outputs are cleared before building.</summary>
    bool IsCleanBuild { get; set; }

    /// <summary>Gets whether a build is currently running.</summary>
    bool IsBuilding { get; }

    /// <summary>
    /// Gets whether cancellation has been requested and is being carried out; the Cancel button is
    /// disabled during this transient state, between Building and Cancelled.
    /// </summary>
    bool IsCancelling { get; }

    /// <summary>Gets the message shown while cancellation is in progress, or <see langword="null"/> otherwise.</summary>
    string? CancellingMessage { get; }

    /// <summary>Gets whether the page is showing the uncommitted/unpushed build acknowledgement prompt.</summary>
    bool IsAwaitingConfirmation { get; }

    /// <summary>
    /// Gets the reason a build cannot currently start, or <see langword="null"/> when building is
    /// allowed. Reflects the first required check that is not <see cref="ToolchainAvailability.Found"/>,
    /// or a git status error, in that order.
    /// </summary>
    string? BuildBlockedReason { get; }

    /// <summary>Gets whether <see cref="BuildBlockedReason"/> currently has a value.</summary>
    bool HasBuildBlockedReason { get; }

    /// <summary>Gets whether the ordinary Build/Cancel controls should show, as opposed to the acknowledgement prompt.</summary>
    bool CanShowBuildActions { get; }

    /// <summary>Gets whether the Build command can currently start a build.</summary>
    bool CanBuild { get; }

    /// <summary>Gets the outcome of the most recently finished build, or <see langword="null"/> before any build has finished.</summary>
    BuildHistoryResult? LastOutcome { get; }

    /// <summary>Gets whether a build has finished, and a result banner should show.</summary>
    bool HasResult { get; }

    /// <summary>
    /// Gets whether the idle build form (summary, clean-build toggle, note, Build button) should show,
    /// as opposed to the pipeline/console/outcome view: only before any build has run this session and
    /// while nothing is currently building or cancelling.
    /// </summary>
    bool ShowIdleForm { get; }

    /// <summary>Gets whether the pipeline/console/outcome view should show, as the complement of <see cref="ShowIdleForm"/>.</summary>
    bool ShowActiveOrResult { get; }

    /// <summary>Gets whether the most recently finished build failed, for showing "Copy diagnostics" on the failure banner.</summary>
    bool IsFailed { get; }

    /// <summary>Gets the result banner text for <see cref="LastOutcome"/>, or <see langword="null"/> before any build has finished.</summary>
    string? ResultBannerText { get; }

    /// <summary>Gets the failure message for the most recently finished build; <see langword="null"/> unless it failed.</summary>
    string? LastOutcomeMessage { get; }

    /// <summary>Gets the command that starts a build, or opens the acknowledgement prompt for a dirty/unpushed source.</summary>
    RelayCommand BuildCommand { get; }

    /// <summary>Gets the command that starts a build after the acknowledgement prompt is accepted.</summary>
    RelayCommand ConfirmBuildCommand { get; }

    /// <summary>Gets the command that dismisses the acknowledgement prompt without starting a build.</summary>
    RelayCommand CancelConfirmationCommand { get; }

    /// <summary>Gets the command that cancels the currently running build.</summary>
    RelayCommand CancelCommand { get; }

    /// <summary>
    /// Gets the currently running build's task, or <see langword="null"/> when idle. Lets the main
    /// window's close handler await a build's own cancellation-driven shutdown before actually
    /// closing, and gives tests a seam to await the same task.
    /// </summary>
    Task? RunningBuildTask { get; }

    /// <summary>Loads the current preflight and git status, updating <see cref="BuildBlockedReason"/>.</summary>
    /// <param name="cancellationToken">The token used to cancel the outstanding checks.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the pipeline's stages in order, one segment per <see cref="BuildStage"/> value.</summary>
    IReadOnlyList<IBuildStageViewModel> Stages { get; }

    /// <summary>Gets the number of stages that have actually succeeded in the current or most recent build.</summary>
    int CompletedStageCount { get; }

    /// <summary>Gets an honest "Stage N of 8" summary reflecting <see cref="CompletedStageCount"/> exactly -- never an invented percentage.</summary>
    string StageProgressText { get; }

    /// <summary>Gets the Build page's log panel, kept alive for the application's lifetime.</summary>
    ILogViewModel Log { get; }

    /// <summary>Gets the produced archive's path on success; <see langword="null"/> otherwise.</summary>
    string? ArchivePath { get; }

    /// <summary>Gets whether <see cref="ArchivePath"/> currently has a value.</summary>
    bool HasArchivePath { get; }

    /// <summary>Gets the produced archive's file name on success; <see langword="null"/> otherwise.</summary>
    string? ArchiveFileName { get; }

    /// <summary>Gets the produced archive's human-readable file size on success; <see langword="null"/> otherwise.</summary>
    string? ArchiveSizeText { get; }

    /// <summary>Gets how long the most recently finished build ran; <see langword="null"/> before any build has finished.</summary>
    TimeSpan? LastBuildDuration { get; }

    /// <summary>Gets a "Finished in Ns." summary of <see cref="LastBuildDuration"/>; <see langword="null"/> before any build has finished.</summary>
    string? LastBuildDurationText { get; }

    /// <summary>Gets the command that opens the produced archive's containing folder.</summary>
    RelayCommand OpenArchiveFolderCommand { get; }

    /// <summary>Gets the command that copies the produced archive's path to the clipboard.</summary>
    RelayCommand CopyArchivePathCommand { get; }

    /// <summary>Gets the produced archive's SHA-256 hash, as lowercase hex, on success; <see langword="null"/> otherwise.</summary>
    string? ArchiveSha256 { get; }

    /// <summary>Gets whether the archive's real contents are currently shown.</summary>
    bool IsShowingArchiveContents { get; }

    /// <summary>Gets the produced archive's real entry names, read directly from the ZIP; empty until <see cref="ViewArchiveContentsCommand"/> runs.</summary>
    IReadOnlyList<string> ArchiveEntries { get; }

    /// <summary>Gets the command that toggles showing the produced archive's real contents.</summary>
    RelayCommand ViewArchiveContentsCommand { get; }

    /// <summary>Gets the retained build history, most recent first.</summary>
    IReadOnlyList<BuildHistoryEntry> RecentBuilds { get; }

    /// <summary>Gets whether "Show all" has been used to reveal every retained build, rather than just the most recent ones.</summary>
    bool IsShowingAllRecentBuilds { get; }

    /// <summary>Gets the recent builds currently shown: the most recent few, or all of them once <see cref="IsShowingAllRecentBuilds"/> is set.</summary>
    IReadOnlyList<BuildHistoryEntry> VisibleRecentBuilds { get; }

    /// <summary>Gets whether there are more retained builds than <see cref="VisibleRecentBuilds"/> currently shows.</summary>
    bool HasMoreRecentBuilds { get; }

    /// <summary>Gets the command that toggles between showing the most recent few builds and every retained build.</summary>
    RelayCommand ToggleShowAllRecentBuildsCommand { get; }

    /// <summary>
    /// Gets the command that returns from a finished build's result view to the idle build form
    /// without starting a build. Refreshes preflight and git status in the background so the form's
    /// gate reflects current reality by the time the user presses Build themselves, rather than
    /// trusting a check that may be stale by however long the previous build took.
    /// </summary>
    RelayCommand NewBuildCommand { get; }

    /// <summary>Gets the current branch name, shown only in the footer status strip.</summary>
    string? GitBranch { get; }

    /// <summary>Gets a short source-state label for the footer status strip: Pushed, Local changes, Not pushed, Committed (unverified), or Unverified while git status has not loaded.</summary>
    string GitFooterStateText { get; }

    /// <summary>
    /// Gets whether the current git status needs the maintainer's attention before building: a dirty
    /// working tree, or commits not yet pushed to the upstream remote. Never true while git status has
    /// not loaded or could not be determined -- that failure is already surfaced via
    /// <see cref="BuildBlockedReason"/>.
    /// </summary>
    bool GitNeedsAttention { get; }

    /// <summary>
    /// Gets the footer status text reflecting the Build page's real current state: Building while a
    /// build runs; Cancelling during the transient shutdown between Building and Cancelled;
    /// Failed/Cancelled/Complete for the most recent finished build; "Environment incomplete" while
    /// idle with a missing required check; Ready only while idle with everything passing. Never a
    /// static "Ready".
    /// </summary>
    string FooterStatusText { get; }

    /// <summary>Gets the most recently loaded preflight results, in preflight order.</summary>
    IReadOnlyList<ToolchainCheckResult> PreflightResults { get; }

    /// <summary>Gets an honest "N of M checks passed" summary of <see cref="PreflightResults"/>.</summary>
    string EnvironmentSummaryText { get; }

    /// <summary>Gets whether any required build tool is currently unavailable.</summary>
    bool HasMissingRequiredTool { get; }

    /// <summary>Gets the resolved .NET SDK version detail for the footer status strip, or <see langword="null"/> before preflight has loaded.</summary>
    string? DotNetVersionDetail { get; }

    /// <summary>Gets the command that copies a plain-text diagnostics report -- environment checks, git status, and the last build -- to the clipboard.</summary>
    RelayCommand CopyDiagnosticsCommand { get; }
}

/// <inheritdoc cref="IBuildPageViewModel"/>
public sealed class BuildPageViewModel : ObservableObject, IBuildPageViewModel
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

    /// <summary>The shared build output path override this page checks and builds.</summary>
    private readonly IOutputPathContext outputPathContext;

    /// <summary>Verifies the resolved output root is safe for a build to destructively manage before <see cref="CleanBuildOutputs"/> or the coordinator ever touches it.</summary>
    private readonly IBuildOutputOwnershipGuard outputOwnershipGuard;

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
    /// <param name="outputPathContext">The shared build output path override this page checks and builds.</param>
    /// <param name="outputOwnershipGuard">Verifies the resolved output root is safe for a build to destructively manage.</param>
    /// <param name="log">The Build page's log panel, kept alive for the application's lifetime.</param>
    /// <param name="buildStageViewModelFactory">Constructs one stage segment for a given pipeline stage, for <see cref="Stages"/>.</param>
    public BuildPageViewModel(
        IEnvironmentStore environmentStore,
        IGitStatusStore gitStatusStore,
        IAdapterHostBuildCoordinator buildCoordinator,
        IBuildHistoryStore buildHistoryStore,
        ISettingsStore settingsStore,
        Action<string> openOutputFolder,
        Action<string> setClipboardText,
        IRepositoryContext repositoryContext,
        IOutputPathContext outputPathContext,
        IBuildOutputOwnershipGuard outputOwnershipGuard,
        ILogViewModel log,
        Func<BuildStage, IBuildStageViewModel> buildStageViewModelFactory)
    {
        this.environmentStore = environmentStore;
        this.gitStatusStore = gitStatusStore;
        this.buildCoordinator = buildCoordinator;
        this.buildHistoryStore = buildHistoryStore;
        this.settingsStore = settingsStore;
        this.openOutputFolder = openOutputFolder;
        this.setClipboardText = setClipboardText;
        this.repositoryContext = repositoryContext;
        this.outputPathContext = outputPathContext;
        this.outputOwnershipGuard = outputOwnershipGuard;
        Log = log;
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
        Stages = Enum.GetValues<BuildStage>().Select(buildStageViewModelFactory).ToList();
        Log.AutoScroll = settingsStore.Load().AutoScrollLogs;
        recentBuilds = buildHistoryStore.GetRecent();
    }

    /// <inheritdoc/>
    public IReadOnlyList<BuildProfile> AvailableProfiles { get; } = Enum.GetValues<BuildProfile>();

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public string Profile => SelectedProfile.ToString();

    /// <inheritdoc/>
    public string BuildSummaryText => IsCleanBuild ? $"{Profile} · Clean build" : Profile;

    /// <inheritdoc/>
    public string AdapterSummaryText => $"{SelectedProfile.ToDotnetConfiguration()} x64";

    /// <inheritdoc/>
    public string HostSummaryText => $"self-contained win-x64 ({SelectedProfile.ToDotnetConfiguration()})";

    /// <inheritdoc/>
    public string RepositoryVersion => TryReadRepositoryVersion(repositoryContext.RepositoryRoot);

    /// <inheritdoc/>
    public string? BuildNote
    {
        get => buildNote;
        set => SetProperty(ref buildNote, value);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public string? CancellingMessage => IsCancelling ? "Stopping build… Terminating active build processes." : null;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool HasBuildBlockedReason => BuildBlockedReason is not null;

    /// <inheritdoc/>
    public bool CanShowBuildActions => !IsAwaitingConfirmation;

    /// <inheritdoc/>
    public bool CanBuild => !IsBuilding && !IsAwaitingConfirmation && BuildBlockedReason is null;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool HasResult => LastOutcome is not null;

    /// <inheritdoc/>
    public bool ShowIdleForm => !IsBuilding && !IsCancelling && !HasResult;

    /// <inheritdoc/>
    public bool ShowActiveOrResult => !ShowIdleForm;

    /// <summary>Notifies the properties that switch between the idle form and the active/result view.</summary>
    private void NotifyShowIdleFormChanged()
    {
        OnPropertyChanged(nameof(ShowIdleForm));
        OnPropertyChanged(nameof(ShowActiveOrResult));
    }

    /// <inheritdoc/>
    public bool IsFailed => LastOutcome == BuildHistoryResult.Failed;

    /// <inheritdoc/>
    public string? ResultBannerText => LastOutcome switch
    {
        BuildHistoryResult.Succeeded => "Build succeeded.",
        BuildHistoryResult.Failed => "Build failed.",
        BuildHistoryResult.Cancelled => "Build cancelled.",
        _ => null,
    };

    /// <inheritdoc/>
    public string? LastOutcomeMessage
    {
        get => lastOutcomeMessage;
        private set => SetProperty(ref lastOutcomeMessage, value);
    }

    /// <inheritdoc/>
    public RelayCommand BuildCommand { get; }

    /// <inheritdoc/>
    public RelayCommand ConfirmBuildCommand { get; }

    /// <inheritdoc/>
    public RelayCommand CancelConfirmationCommand { get; }

    /// <inheritdoc/>
    public RelayCommand CancelCommand { get; }

    /// <inheritdoc/>
    public Task? RunningBuildTask { get; private set; }

    /// <inheritdoc/>
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
    /// here. Also re-reads <see cref="RepositoryVersion"/>, since a refresh this page did not itself
    /// request can mean the active repository just changed underneath it (<see cref="repositoryContext"/>),
    /// and that property has no backing field of its own to otherwise signal it changed.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused change details; every change recomputes the same derived state.</param>
    private void OnEnvironmentStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        PreflightResults = environmentStore.PreflightResults;
        OnPropertyChanged(nameof(RepositoryVersion));
        UpdateBuildBlockedReason();
    }

    /// <summary>
    /// Recomputes <see cref="BuildBlockedReason"/> from the latest <see cref="PreflightResults"/> and
    /// git status. While <see cref="environmentStore"/> is mid-refresh -- for example because the
    /// active repository just changed -- this reports the same "checking" reason construction starts
    /// in, regardless of what the last-known results say: those results may still describe the
    /// repository that was active before the refresh started, so a build must not read them as if they
    /// were already valid for the current one. A failed refresh leaves <see cref="PreflightResults"/>
    /// empty rather than describing any tool as unavailable, so that case is checked explicitly --
    /// otherwise it would fall through to the checks below as if the environment were simply unchecked.
    /// </summary>
    private void UpdateBuildBlockedReason()
    {
        if (environmentStore.IsRefreshing)
        {
            BuildBlockedReason = CheckingEnvironmentReason;
            return;
        }

        if (environmentStore.RefreshError is { } refreshError)
        {
            BuildBlockedReason = $"Could not check the environment: {refreshError}";
            return;
        }

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
    /// prompt instead of starting.
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
    /// does nothing when no build is running or cancellation was already requested.
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

    /// <summary>
    /// Immutable identity of one build run, captured synchronously at the very start of
    /// <see cref="RunBuildAsync"/> before its first <c>await</c>. Everything that describes this run --
    /// cleaning previous outputs, the coordinator request, and the recorded history entry -- reads only
    /// from this snapshot rather than the live <see cref="repositoryContext"/>, <see cref="SelectedProfile"/>,
    /// or <see cref="outputPathContext"/>: once a build has started, a Settings or repository change made
    /// while it is still running must affect only the next build, never the one already in flight.
    /// </summary>
    /// <param name="RepositoryRoot">The repository root this build targets.</param>
    /// <param name="RepositoryVersion">The repository's product version at the moment this build started.</param>
    /// <param name="Profile">The build profile this build targets.</param>
    /// <param name="OutputPath">The build output path override in effect for this build, or <see langword="null"/> to use <see cref="Profile"/>'s default.</param>
    /// <param name="IsCleanBuild">Whether this build clears generated Adapter and Host build outputs before building.</param>
    /// <param name="Note">The optional local note attached to this build.</param>
    private sealed record BuildRunSnapshot(
        string RepositoryRoot,
        string RepositoryVersion,
        BuildProfile Profile,
        string? OutputPath,
        bool IsCleanBuild,
        string? Note);

    /// <summary>Runs the Adapter+Host build, recording its outcome.</summary>
    private async Task RunBuildAsync()
    {
        var snapshot = new BuildRunSnapshot(
            RepositoryRoot: repositoryContext.RepositoryRoot,
            RepositoryVersion: TryReadRepositoryVersion(repositoryContext.RepositoryRoot),
            Profile: SelectedProfile,
            OutputPath: outputPathContext.OutputPath,
            IsCleanBuild: IsCleanBuild,
            Note: string.IsNullOrWhiteSpace(BuildNote) ? null : BuildNote.Trim());
        BuildNote = null;
        IsBuilding = true;
        ResetBuildResultState();
        buildCancellation = new CancellationTokenSource();
        DateTimeOffset startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Verified before any destructive work this build could do -- CleanBuildOutputs below,
            // or the coordinator's own packaging -- ever touches the resolved output root: a custom
            // output path that turns out to be an arbitrary, unrelated, non-empty folder must fail
            // the whole build here rather than partway through either path. Not given buildCancellation's
            // own token: this is a single fast, synchronous filesystem check with no internal
            // cancellation point of its own, and a Task.Run scheduled with an already-cancelled token
            // can be cancelled before its delegate ever starts running -- an immediate Cancel right
            // after Build must not race this check out from under it and skip straight past the
            // coordinator with nothing ever verified.
            await Task.Run(() => outputOwnershipGuard.EnsureOwned(
                snapshot.OutputPath ?? snapshot.Profile.ToOutputRoot(snapshot.RepositoryRoot), snapshot.RepositoryRoot));

            if (snapshot.IsCleanBuild)
            {
                Log.AppendLine("Clean build: clearing generated Adapter and Host build outputs...");
                await Task.Run(() => CleanBuildOutputs(snapshot), buildCancellation.Token);
            }

            AdapterHostBuildResult result = await buildCoordinator.BuildAsync(
                new AdapterHostBuildRequest(snapshot.RepositoryRoot, snapshot.Profile, snapshot.OutputPath),
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
            // Teardown of the build's own lifecycle state completes unconditionally, before the
            // optional, best-effort local history write below -- a build must never appear stuck
            // "in progress" just because history persistence failed.
            IsBuilding = false;
            IsCancelling = false;
            TryRecordBuildHistory(snapshot, startedAt, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Clears the previous build's outcome, archive, stages, and log back to a fresh, no-result state.
    /// Also re-seeds <see cref="LogViewModel.AutoScroll"/> from the current setting, so a change made
    /// on the Settings page takes effect at the next build rather than only when the application was
    /// started, without overriding the user's own live toggle of the same checkbox mid-session.
    /// </summary>
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
        Log.AutoScroll = settingsStore.Load().AutoScrollLogs;
    }

    /// <summary>Formats a byte count as a human-readable KB/MB size.</summary>
    /// <param name="bytes">The size in bytes.</param>
    private static string FormatFileSize(long bytes) =>
        bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / (1024.0 * 1024.0):0.#} MB";

    /// <summary>
    /// Records the just-finished build, swallowing a local persistence failure: history is optional
    /// bookkeeping, and a failure to write or reload it must never change the build's own already-
    /// finished outcome or propagate out of <see cref="RunBuildAsync"/>'s teardown.
    /// </summary>
    /// <param name="snapshot">The just-finished build's immutable identity, captured at its start.</param>
    /// <param name="startedAt">When this build started.</param>
    /// <param name="duration">How long this build ran before reaching its final outcome.</param>
    private void TryRecordBuildHistory(BuildRunSnapshot snapshot, DateTimeOffset startedAt, TimeSpan duration)
    {
        try
        {
            RecordBuildHistory(snapshot, startedAt, duration);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Recording build history is optional local bookkeeping; a failure here must not affect
            // the build's own already-reported outcome. RecentBuilds is simply left at its previous,
            // now slightly stale value rather than partially updated.
        }
    }

    /// <summary>Records the just-finished build and refreshes <see cref="RecentBuilds"/> from the store.</summary>
    /// <param name="snapshot">The just-finished build's immutable identity, captured at its start.</param>
    /// <param name="startedAt">When this build started.</param>
    /// <param name="duration">How long this build ran before reaching its final outcome.</param>
    private void RecordBuildHistory(BuildRunSnapshot snapshot, DateTimeOffset startedAt, TimeSpan duration)
    {
        string? failedStage = LastOutcome == BuildHistoryResult.Failed
            ? Stages.FirstOrDefault(stage => stage.Status == BuildStageStatus.Failed)?.DisplayName
            : null;

        buildHistoryStore.Add(new BuildHistoryEntry(
            startedAt,
            LastOutcome!.Value,
            snapshot.RepositoryVersion,
            snapshot.Profile.ToString(),
            duration,
            ArchivePath,
            failedStage,
            ArchiveSha256,
            snapshot.Note));

        RecentBuilds = buildHistoryStore.GetRecent();
    }

    /// <summary>Reads the repository's VERSION file, reporting "unknown" instead of throwing when it cannot be read.</summary>
    /// <param name="repositoryRoot">The repository root to read the VERSION file from.</param>
    private static string TryReadRepositoryVersion(string repositoryRoot)
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
        OnPropertyChanged(nameof(CancellingMessage));
        BuildCommand.RaiseCanExecuteChanged();
        ConfirmBuildCommand.RaiseCanExecuteChanged();
        CancelConfirmationCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        NewBuildCommand.RaiseCanExecuteChanged();
    }

    /// <inheritdoc/>
    public IReadOnlyList<IBuildStageViewModel> Stages { get; }

    /// <inheritdoc/>
    public int CompletedStageCount => Stages.Count(stage => stage.Status == BuildStageStatus.Succeeded);

    /// <inheritdoc/>
    public string StageProgressText => $"Stage {CompletedStageCount} of {Stages.Count}";

    /// <summary>Resets every stage segment back to Pending before a new build starts.</summary>
    private void ResetStages()
    {
        foreach (IBuildStageViewModel stage in Stages)
        {
            stage.Reset();
        }

        NotifyStageProgressChanged();
    }

    /// <summary>Applies one reported stage transition to its matching segment.</summary>
    /// <param name="stageEvent">The reported transition.</param>
    private void OnBuildStageEvent(BuildStageEvent stageEvent)
    {
        IBuildStageViewModel? stage = Stages.FirstOrDefault(candidate => candidate.Stage == stageEvent.Stage);
        stage?.Apply(stageEvent);
        NotifyStageProgressChanged();
    }

    /// <summary>Notifies bound properties that summarize the stage segments as a whole.</summary>
    private void NotifyStageProgressChanged()
    {
        OnPropertyChanged(nameof(CompletedStageCount));
        OnPropertyChanged(nameof(StageProgressText));
    }

    /// <inheritdoc/>
    public ILogViewModel Log { get; }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool HasArchivePath => ArchivePath is not null;

    /// <inheritdoc/>
    public string? ArchiveFileName => ArchivePath is null ? null : Path.GetFileName(ArchivePath);

    /// <inheritdoc/>
    public string? ArchiveSizeText
    {
        get => archiveSizeText;
        private set => SetProperty(ref archiveSizeText, value);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public string? LastBuildDurationText => LastBuildDuration is { } duration ? $"Finished in {duration.TotalSeconds:0.0}s." : null;

    /// <inheritdoc/>
    public RelayCommand OpenArchiveFolderCommand { get; }

    /// <inheritdoc/>
    public RelayCommand CopyArchivePathCommand { get; }

    /// <inheritdoc/>
    public string? ArchiveSha256
    {
        get => archiveSha256;
        private set => SetProperty(ref archiveSha256, value);
    }

    /// <inheritdoc/>
    public bool IsShowingArchiveContents
    {
        get => isShowingArchiveContents;
        private set => SetProperty(ref isShowingArchiveContents, value);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ArchiveEntries
    {
        get => archiveEntries;
        private set => SetProperty(ref archiveEntries, value);
    }

    /// <inheritdoc/>
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
    /// hardcoded package-layout list) the first time it is shown. Does nothing, rather than crashing
    /// the application, if the archive has since been moved, deleted, or is locked by another process --
    /// a real possibility given how long it can sit on disk before this is clicked.
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

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(ArchivePath);
            ArchiveEntries = archive.Entries.Select(entry => entry.FullName).ToList();
            IsShowingArchiveContents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Reading the archive's own contents for display is a convenience action; a failure here
            // must not crash the application or change any other reported state.
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public IReadOnlyList<BuildHistoryEntry> VisibleRecentBuilds =>
        IsShowingAllRecentBuilds ? RecentBuilds : RecentBuilds.Take(RecentBuildsPreviewCount).ToList();

    /// <inheritdoc/>
    public bool HasMoreRecentBuilds => RecentBuilds.Count > RecentBuildsPreviewCount;

    /// <inheritdoc/>
    public RelayCommand ToggleShowAllRecentBuildsCommand { get; }

    /// <inheritdoc/>
    public RelayCommand NewBuildCommand { get; }

    /// <summary>
    /// Opens the archive's containing folder when <see cref="BuilderSettings.OpenOutputFolderAfterSuccessfulBuild"/>
    /// is enabled, only ever on a successful build -- never on failure or cancellation.
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

    /// <summary>
    /// Copies the produced archive's path to the clipboard; does nothing when there is no archive. A
    /// failure here (for example another process briefly holding clipboard access, a real and fairly
    /// common Windows condition) is a convenience-action failure and never changes any reported state.
    /// </summary>
    private void OnCopyArchivePath()
    {
        if (ArchivePath is not null)
        {
            TrySetClipboardText(ArchivePath);
        }
    }

    /// <summary>Writes <paramref name="text"/> to the clipboard, swallowing a failure to acquire it rather than crashing the application.</summary>
    /// <param name="text">The text to write.</param>
    private void TrySetClipboardText(string text)
    {
        try
        {
            setClipboardText(text);
        }
        catch (ExternalException)
        {
            // Another process briefly holding clipboard access is a normal, transient Windows
            // condition; a failure here must not crash the application or change any reported state.
        }
    }

    /// <inheritdoc/>
    public string? GitBranch => gitStatusStore.Status?.Branch;

    /// <inheritdoc/>
    public string GitFooterStateText => gitStatusStore.Status switch
    {
        { WorkingTreeState: WorkingTreeState.Dirty } => "Local changes",
        { RemoteSyncState: RemoteSyncState.NotPushed } => "Not pushed",
        { RemoteSyncState: RemoteSyncState.CouldNotVerify } => "Committed, unverified",
        { RemoteSyncState: RemoteSyncState.Pushed } => "Pushed",
        _ => "Unverified",
    };

    /// <inheritdoc/>
    public bool GitNeedsAttention => gitStatusStore.Status is { WorkingTreeState: WorkingTreeState.Dirty } or { RemoteSyncState: RemoteSyncState.NotPushed };

    /// <inheritdoc/>
    public string FooterStatusText => (IsBuilding, IsCancelling, LastOutcome, HasBuildBlockedReason) switch
    {
        (true, true, _, _) => "Cancelling",
        (true, false, _, _) => "Building",
        (false, _, BuildHistoryResult.Failed, _) => "Failed",
        (false, _, BuildHistoryResult.Cancelled, _) => "Cancelled",
        (false, _, BuildHistoryResult.Succeeded, _) => "Complete",
        // Distinguished from the case below: environmentStore refreshing means nothing has actually
        // been determined incomplete yet -- reporting it as such here would assert a negative result
        // before the check that would produce one has even finished (for example immediately after a
        // repository change, or at startup).
        (false, _, null, true) when environmentStore.IsRefreshing => "Checking",
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public string EnvironmentSummaryText =>
        $"{PreflightResults.Count(result => result.Availability == ToolchainAvailability.Found)} of {PreflightResults.Count} checks passed";

    /// <inheritdoc/>
    public bool HasMissingRequiredTool => PreflightResults.Any(result => result.Availability != ToolchainAvailability.Found);

    /// <inheritdoc/>
    public string? DotNetVersionDetail => PreflightResults.FirstOrDefault(result => result.ToolName == ".NET SDK")?.Detail;

    /// <inheritdoc/>
    public RelayCommand CopyDiagnosticsCommand { get; }

    /// <summary>Formats the current diagnostics report and writes it to the clipboard.</summary>
    private void OnCopyDiagnostics()
    {
        TrySetClipboardText(DiagnosticsFormatter.Format(PreflightResults, gitStatusStore.Status, gitStatusStore.StatusError, LastOutcome, LastOutcomeMessage));
    }

    /// <summary>
    /// Clears generated Adapter and Host build outputs before building: only
    /// <c>adapter/build/{preset}/</c> and the effective output root's <c>{publish,package}</c>
    /// subfolders are deleted -- confirmed against the real repository layout when the output root is
    /// the default <c>tooling/out</c>, or the folder the user explicitly chose through
    /// <see cref="SettingsPageViewModel.BrowseOutputPathCommand"/> when an override is set -- never
    /// vcpkg's shared package cache. Takes the build's own immutable <paramref name="snapshot"/> rather
    /// than reading <see cref="repositoryContext"/>/<see cref="SelectedProfile"/>/<see cref="outputPathContext"/>
    /// directly, so a repository or Settings change made while this runs on a background thread cannot
    /// clean a different repository than the one this same build then compiles.
    /// </summary>
    /// <param name="snapshot">The running build's immutable identity, captured at its start.</param>
    private static void CleanBuildOutputs(BuildRunSnapshot snapshot)
    {
        DeleteDirectoryIfExists(Path.Combine(snapshot.RepositoryRoot, "adapter", "build", snapshot.Profile.ToCMakePreset()));
        string outputRoot = snapshot.OutputPath ?? snapshot.Profile.ToOutputRoot(snapshot.RepositoryRoot);
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
