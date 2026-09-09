using System.ComponentModel;
using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Git;
using DovahLink.DovahLinkBuilder.Preflight;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the one preflight-and-git-status refresh both the Build and Environment pages trigger, so
/// concurrent refresh requests -- for example both pages initializing at startup -- share one real
/// check instead of duplicating preflight probes and potentially running <c>git fetch</c> twice at
/// once.
/// </summary>
public interface IEnvironmentStore : INotifyPropertyChanged
{
    /// <summary>Gets the most recently loaded preflight results, in preflight order.</summary>
    IReadOnlyList<ToolchainCheckResult> PreflightResults { get; }

    /// <summary>Gets whether a refresh is currently in progress.</summary>
    bool IsRefreshing { get; }

    /// <summary>
    /// Refreshes preflight results and the shared git status for the repository currently active on
    /// the shared <see cref="IRepositoryContext"/>. A refresh already in progress is returned to every
    /// concurrent caller instead of starting a second one.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token used to cancel a newly started refresh; has no effect when a refresh already in
    /// progress is returned instead, since that refresh is already running under its own caller's token.
    /// </param>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IEnvironmentStore"/>
public sealed class EnvironmentStore : ObservableObject, IEnvironmentStore
{
    /// <summary>Checks the required build tools.</summary>
    private readonly IPreflightService preflightService;

    /// <summary>The shared git status refreshed alongside preflight, once per <see cref="RefreshAsync"/> call.</summary>
    private readonly IGitStatusStore gitStatusStore;

    /// <summary>The shared repository root each refresh checks.</summary>
    private readonly IRepositoryContext repositoryContext;

    /// <summary>The backing field for <see cref="PreflightResults"/>.</summary>
    private IReadOnlyList<ToolchainCheckResult> preflightResults = [];

    /// <summary>The backing field for <see cref="IsRefreshing"/>.</summary>
    private bool isRefreshing;

    /// <summary>
    /// The most recently started refresh, shared with every concurrent <see cref="RefreshAsync"/>
    /// caller while it is still running. Gated on <see cref="Task.IsCompleted"/> rather than cleared
    /// back to <see langword="null"/> when the refresh finishes: a refresh whose preflight and git
    /// checks both resolve synchronously (as test fakes typically do) can complete before the
    /// assignment below even runs, so clearing this field from inside that same refresh's own
    /// completion would race the assignment and could permanently wipe a task nothing has replaced yet.
    /// </summary>
    private Task? inFlightRefresh;

    /// <summary>Creates a store over the given preflight service, shared git status store, and shared repository context.</summary>
    /// <param name="preflightService">Checks the required build tools.</param>
    /// <param name="gitStatusStore">The shared git status refreshed alongside preflight, once per <see cref="RefreshAsync"/> call.</param>
    /// <param name="repositoryContext">The shared repository root each refresh checks.</param>
    public EnvironmentStore(IPreflightService preflightService, IGitStatusStore gitStatusStore, IRepositoryContext repositoryContext)
    {
        this.preflightService = preflightService;
        this.gitStatusStore = gitStatusStore;
        this.repositoryContext = repositoryContext;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolchainCheckResult> PreflightResults
    {
        get => preflightResults;
        private set => SetProperty(ref preflightResults, value);
    }

    /// <inheritdoc/>
    public bool IsRefreshing
    {
        get => isRefreshing;
        private set => SetProperty(ref isRefreshing, value);
    }

    /// <inheritdoc/>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (inFlightRefresh is { IsCompleted: false } runningRefresh)
        {
            return runningRefresh;
        }

        inFlightRefresh = RunRefreshAsync(cancellationToken);
        return inFlightRefresh;
    }

    /// <summary>Runs the actual preflight-and-git-status refresh, shared by every concurrent <see cref="RefreshAsync"/> caller.</summary>
    /// <param name="cancellationToken">The token used to cancel the refresh.</param>
    private async Task RunRefreshAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;
        try
        {
            PreflightResults = await preflightService.CheckAllAsync(repositoryContext.RepositoryRoot, cancellationToken);
            await gitStatusStore.RefreshAsync(cancellationToken);
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
