using System.ComponentModel;
using DovahLink.DovahLinkBuilder.Git;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the Environment page's state: the 8 required build tool checks and an informational git
/// remote status row, both loaded from the same shared store the Build page uses so every page
/// reflects one shared, real check result rather than an independently hardcoded copy.
/// </summary>
public interface IEnvironmentPageViewModel : INotifyPropertyChanged
{
    /// <summary>Gets the 8 required build tool checks, in preflight order.</summary>
    IReadOnlyList<EnvironmentCheckViewModel> Checks { get; }

    /// <summary>Gets an honest "N of M ready" summary of <see cref="Checks"/>.</summary>
    string SummaryText { get; }

    /// <summary>Gets the repository's git remote status, informational only; <see langword="null"/> when it could not be determined.</summary>
    GitSourceStatus? GitStatus { get; }

    /// <summary>Gets the git status failure message, or <see langword="null"/> when git status loaded successfully.</summary>
    string? GitStatusError { get; }

    /// <summary>Gets whether a check is currently in progress, reflecting the shared environment store's own refresh state.</summary>
    bool IsChecking { get; }

    /// <summary>Gets the command that manually re-runs every check.</summary>
    RelayCommand RecheckCommand { get; }

    /// <summary>Loads the preflight checks and git status.</summary>
    /// <param name="cancellationToken">The token used to cancel the outstanding checks.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IEnvironmentPageViewModel"/>
public sealed class EnvironmentPageViewModel : ObservableObject, IEnvironmentPageViewModel
{
    /// <summary>The shared preflight-and-git-status refresh both the Build and Environment pages trigger.</summary>
    private readonly IEnvironmentStore environmentStore;

    /// <summary>The shared git status both the Build and Environment pages read and refresh.</summary>
    private readonly IGitStatusStore gitStatusStore;

    /// <summary>The backing field for <see cref="Checks"/>.</summary>
    private IReadOnlyList<EnvironmentCheckViewModel> checks = [];

    /// <summary>Initializes the page over its collaborators, starting with no checks loaded.</summary>
    /// <param name="environmentStore">The shared preflight-and-git-status refresh both the Build and Environment pages trigger.</param>
    /// <param name="gitStatusStore">The shared git status both the Build and Environment pages read and refresh.</param>
    public EnvironmentPageViewModel(IEnvironmentStore environmentStore, IGitStatusStore gitStatusStore)
    {
        this.environmentStore = environmentStore;
        this.gitStatusStore = gitStatusStore;
        gitStatusStore.PropertyChanged += OnGitStatusStoreChanged;
        environmentStore.PropertyChanged += OnEnvironmentStoreChanged;
        RecheckCommand = new RelayCommand(OnRecheck, () => !IsChecking);
    }

    /// <inheritdoc/>
    public IReadOnlyList<EnvironmentCheckViewModel> Checks
    {
        get => checks;
        private set
        {
            if (SetProperty(ref checks, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    /// <inheritdoc/>
    public string SummaryText => $"{Checks.Count(check => check.IsAvailable)} of {Checks.Count} ready";

    /// <inheritdoc/>
    public GitSourceStatus? GitStatus => gitStatusStore.Status;

    /// <inheritdoc/>
    public string? GitStatusError => gitStatusStore.StatusError;

    /// <summary>
    /// Relays a change on the shared <see cref="gitStatusStore"/> to this page's own bound properties,
    /// since a refresh triggered from the Build page must also be reflected here.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused change details; either property changing recomputes both.</param>
    private void OnGitStatusStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(GitStatus));
        OnPropertyChanged(nameof(GitStatusError));
    }

    /// <inheritdoc/>
    public bool IsChecking => environmentStore.IsRefreshing;

    /// <inheritdoc/>
    public RelayCommand RecheckCommand { get; }

    /// <summary>Gets the currently running check's task, or <see langword="null"/> when idle. A test seam only.</summary>
    internal Task? RunningRecheckTask { get; private set; }

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => environmentStore.RefreshAsync(cancellationToken);

    /// <summary>
    /// Re-runs every check; does nothing while a check is already in progress. Shares an already
    /// in-flight refresh with the Build page's own trigger, rather than starting a duplicate.
    /// </summary>
    private void OnRecheck()
    {
        if (IsChecking)
        {
            return;
        }

        RunningRecheckTask = environmentStore.RefreshAsync();
    }

    /// <summary>
    /// Relays a change on the shared <see cref="environmentStore"/> to this page's own bound
    /// properties, since a refresh triggered from the Build page must also be reflected here.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused change details; every change recomputes the same derived state.</param>
    private void OnEnvironmentStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        Checks = environmentStore.PreflightResults.Select(result => new EnvironmentCheckViewModel(result)).ToList();
        OnPropertyChanged(nameof(IsChecking));
        RecheckCommand.RaiseCanExecuteChanged();
    }
}
