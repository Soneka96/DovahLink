using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Preflight;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the Environment page's state: the 8 required build tool checks and an informational git
/// remote status row, both loaded from the same services the Build page uses so every page reflects
/// one shared, real check result rather than an independently hardcoded copy (correction #2).
/// </summary>
public sealed class EnvironmentPageViewModel : ObservableObject
{
    /// <summary>Checks the required build tools.</summary>
    private readonly IPreflightService preflightService;

    /// <summary>Reports the repository's branch, working tree, and remote sync state.</summary>
    private readonly IGitStatusService gitStatusService;

    /// <summary>The repository root this page checks.</summary>
    private readonly string repositoryRoot;

    /// <summary>The backing field for <see cref="Checks"/>.</summary>
    private IReadOnlyList<EnvironmentCheckViewModel> checks = [];

    /// <summary>The backing field for <see cref="GitStatus"/>.</summary>
    private GitSourceStatus? gitStatus;

    /// <summary>The backing field for <see cref="GitStatusError"/>.</summary>
    private string? gitStatusError;

    /// <summary>The backing field for <see cref="IsChecking"/>.</summary>
    private bool isChecking;

    /// <summary>Initializes the page over its collaborators, starting with no checks loaded.</summary>
    /// <param name="preflightService">Checks the required build tools.</param>
    /// <param name="gitStatusService">Reports the repository's branch, working tree, and remote sync state.</param>
    /// <param name="repositoryRoot">The repository root this page checks.</param>
    public EnvironmentPageViewModel(IPreflightService preflightService, IGitStatusService gitStatusService, string repositoryRoot)
    {
        this.preflightService = preflightService;
        this.gitStatusService = gitStatusService;
        this.repositoryRoot = repositoryRoot;
        RecheckCommand = new RelayCommand(OnRecheck, () => !IsChecking);
    }

    /// <summary>Gets the 8 required build tool checks, in preflight order.</summary>
    public IReadOnlyList<EnvironmentCheckViewModel> Checks
    {
        get => checks;
        private set => SetProperty(ref checks, value);
    }

    /// <summary>Gets the repository's git remote status, informational only; <see langword="null"/> when it could not be determined.</summary>
    public GitSourceStatus? GitStatus
    {
        get => gitStatus;
        private set => SetProperty(ref gitStatus, value);
    }

    /// <summary>Gets the git status failure message, or <see langword="null"/> when git status loaded successfully.</summary>
    public string? GitStatusError
    {
        get => gitStatusError;
        private set => SetProperty(ref gitStatusError, value);
    }

    /// <summary>Gets whether a check is currently in progress.</summary>
    public bool IsChecking
    {
        get => isChecking;
        private set
        {
            if (SetProperty(ref isChecking, value))
            {
                RecheckCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the command that manually re-runs every check.</summary>
    public RelayCommand RecheckCommand { get; }

    /// <summary>Gets the currently running check's task, or <see langword="null"/> when idle. A test seam only.</summary>
    internal Task? RunningRecheckTask { get; private set; }

    /// <summary>Loads the preflight checks and git status.</summary>
    /// <param name="cancellationToken">The token used to cancel the outstanding checks.</param>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => RunChecksAsync(cancellationToken);

    /// <summary>Re-runs every check; does nothing while a check is already in progress.</summary>
    private void OnRecheck()
    {
        if (IsChecking)
        {
            return;
        }

        RunningRecheckTask = RunChecksAsync();
    }

    /// <summary>Runs the preflight checks and git status check, reporting a failure message instead of throwing for git status.</summary>
    /// <param name="cancellationToken">The token used to cancel the outstanding checks.</param>
    private async Task RunChecksAsync(CancellationToken cancellationToken = default)
    {
        IsChecking = true;
        try
        {
            IReadOnlyList<ToolchainCheckResult> results = await preflightService.CheckAllAsync(repositoryRoot, cancellationToken);
            Checks = results.Select(result => new EnvironmentCheckViewModel(result)).ToList();

            try
            {
                GitStatus = await gitStatusService.GetStatusAsync(repositoryRoot, cancellationToken);
                GitStatusError = null;
            }
            catch (InvalidOperationException exception)
            {
                GitStatus = null;
                GitStatusError = exception.Message;
            }
        }
        finally
        {
            IsChecking = false;
        }
    }
}
