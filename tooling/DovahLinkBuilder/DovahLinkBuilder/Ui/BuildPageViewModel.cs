using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
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

    /// <summary>The repository root this page checks and builds.</summary>
    private readonly string repositoryRoot;

    /// <summary>The most recently loaded git status, or <see langword="null"/> when it could not be determined.</summary>
    private GitSourceStatus? gitStatus;

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

    /// <summary>Initializes the page over its collaborators, starting in the "checking environment" state.</summary>
    /// <param name="preflightService">Checks the required build tools before a build is allowed to start.</param>
    /// <param name="gitStatusService">Reports the repository's branch, working tree, and remote sync state.</param>
    /// <param name="buildCoordinator">Builds and packages the production Adapter and Host.</param>
    /// <param name="repositoryRoot">The repository root this page checks and builds.</param>
    public BuildPageViewModel(
        IPreflightService preflightService,
        IGitStatusService gitStatusService,
        IAdapterHostBuildCoordinator buildCoordinator,
        string repositoryRoot)
    {
        this.preflightService = preflightService;
        this.gitStatusService = gitStatusService;
        this.buildCoordinator = buildCoordinator;
        this.repositoryRoot = repositoryRoot;
        BuildCommand = new RelayCommand(OnBuild, () => CanBuild);
        ConfirmBuildCommand = new RelayCommand(OnConfirmBuild, () => IsAwaitingConfirmation);
        CancelConfirmationCommand = new RelayCommand(OnCancelConfirmation, () => IsAwaitingConfirmation);
        CancelCommand = new RelayCommand(OnCancel, () => IsBuilding);
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
        private set => SetProperty(ref lastOutcome, value);
    }

    /// <summary>
    /// Gets the archive path on success, or the failure message on failure, for the most recently
    /// finished build; <see langword="null"/> before any build has finished or after a cancellation.
    /// </summary>
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
        IReadOnlyList<ToolchainCheckResult> preflightResults =
            await preflightService.CheckAllAsync(repositoryRoot, cancellationToken);
        string? gitStatusError = await RefreshGitStatusAsync(cancellationToken);
        UpdateBuildBlockedReason(preflightResults, gitStatusError);
    }

    /// <summary>Loads the current git status, reporting a failure message instead of throwing.</summary>
    /// <param name="cancellationToken">The token used to cancel the outstanding check.</param>
    /// <returns>A failure message when the status could not be determined; otherwise <see langword="null"/>.</returns>
    private async Task<string?> RefreshGitStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            gitStatus = await gitStatusService.GetStatusAsync(repositoryRoot, cancellationToken);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            gitStatus = null;
            return exception.Message;
        }
    }

    /// <summary>Recomputes <see cref="BuildBlockedReason"/> from the latest preflight results and git status.</summary>
    /// <param name="preflightResults">The latest preflight results.</param>
    /// <param name="gitStatusError">The git status failure message, or <see langword="null"/> when git status loaded successfully.</param>
    private void UpdateBuildBlockedReason(IReadOnlyList<ToolchainCheckResult> preflightResults, string? gitStatusError)
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
        if (!CanBuild)
        {
            return;
        }

        if (gitStatus is { WorkingTreeState: WorkingTreeState.Dirty } or { RemoteSyncState: RemoteSyncState.NotPushed })
        {
            IsAwaitingConfirmation = true;
            return;
        }

        RunningBuildTask = RunBuildAsync();
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

    /// <summary>Runs the Adapter+Host build, recording its outcome.</summary>
    private async Task RunBuildAsync()
    {
        IsBuilding = true;
        LastOutcome = null;
        LastOutcomeMessage = null;
        buildCancellation = new CancellationTokenSource();
        try
        {
            AdapterHostBuildResult result = await buildCoordinator.BuildAsync(
                new AdapterHostBuildRequest(repositoryRoot),
                onOutput: null,
                onStage: null,
                buildCancellation.Token);
            LastOutcome = BuildHistoryResult.Succeeded;
            LastOutcomeMessage = result.ArchivePath;
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
            IsBuilding = false;
        }
    }

    /// <summary>Notifies bound commands and computed properties that depend on <see cref="CanBuild"/>'s inputs.</summary>
    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(HasBuildBlockedReason));
        OnPropertyChanged(nameof(CanShowBuildActions));
        BuildCommand.RaiseCanExecuteChanged();
        ConfirmBuildCommand.RaiseCanExecuteChanged();
        CancelConfirmationCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }
}
