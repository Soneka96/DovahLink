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

    /// <summary>Gets the most recent refresh failure message, or <see langword="null"/> when the last refresh succeeded.</summary>
    string? RefreshError { get; }

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

    /// <summary>The shared build output path override each refresh checks, and whose own change alone triggers a narrower recheck.</summary>
    private readonly IOutputPathContext outputPathContext;

    /// <summary>The backing field for <see cref="PreflightResults"/>.</summary>
    private IReadOnlyList<ToolchainCheckResult> preflightResults = [];

    /// <summary>The backing field for <see cref="IsRefreshing"/>.</summary>
    private bool isRefreshing;

    /// <summary>The backing field for <see cref="RefreshError"/>.</summary>
    private string? refreshError;

    /// <summary>
    /// The most recently started refresh, shared with every concurrent <see cref="RefreshAsync"/>
    /// caller while it is still running. Gated on <see cref="Task.IsCompleted"/> rather than cleared
    /// back to <see langword="null"/> when the refresh finishes: a refresh whose preflight and git
    /// checks both resolve synchronously (as test fakes typically do) can complete before the
    /// assignment below even runs, so clearing this field from inside that same refresh's own
    /// completion would race the assignment and could permanently wipe a task nothing has replaced yet.
    /// </summary>
    private Task? inFlightRefresh;

    /// <summary>Creates a store over the given preflight service, shared git status store, shared repository context, and shared output path context.</summary>
    /// <param name="preflightService">Checks the required build tools.</param>
    /// <param name="gitStatusStore">The shared git status refreshed alongside preflight, once per <see cref="RefreshAsync"/> call.</param>
    /// <param name="repositoryContext">The shared repository root each refresh checks.</param>
    /// <param name="outputPathContext">The shared build output path override each refresh checks, and whose own change alone triggers a narrower recheck.</param>
    public EnvironmentStore(IPreflightService preflightService, IGitStatusStore gitStatusStore, IRepositoryContext repositoryContext, IOutputPathContext outputPathContext)
    {
        this.preflightService = preflightService;
        this.gitStatusStore = gitStatusStore;
        this.repositoryContext = repositoryContext;
        this.outputPathContext = outputPathContext;
        repositoryContext.PropertyChanged += OnRepositoryContextChanged;
        outputPathContext.PropertyChanged += OnOutputPathContextChanged;
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
    public string? RefreshError
    {
        get => refreshError;
        private set => SetProperty(ref refreshError, value);
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

    /// <summary>
    /// Runs the actual preflight-and-git-status refresh, shared by every concurrent
    /// <see cref="RefreshAsync"/> caller. Loops until a full check completes for whichever repository
    /// root and output path override were current when that check started: either can change again
    /// while a check is already running, and a concurrent <see cref="RefreshAsync"/> call or an
    /// <see cref="OnOutputPathContextChanged"/> patch made after that change coalesces onto this same
    /// running refresh rather than starting its own -- without this loop, a rapid second change could
    /// leave the store permanently reporting stale results for whichever one changed, since nothing else
    /// would ever check the newer value once this refresh's own result overwrites it. A failure other
    /// than cancellation is recorded in <see cref="RefreshError"/> and clears <see cref="PreflightResults"/>
    /// rather than propagating, so a build never gates on stale results left over from before the failure.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the refresh.</param>
    private async Task RunRefreshAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;
        try
        {
            string rootCheckedThisPass;
            string? outputPathCheckedThisPass;
            do
            {
                rootCheckedThisPass = repositoryContext.RepositoryRoot;
                outputPathCheckedThisPass = outputPathContext.OutputPath;
                PreflightResults = await preflightService.CheckAllAsync(rootCheckedThisPass, outputPathCheckedThisPass, cancellationToken);
                await gitStatusStore.RefreshAsync(cancellationToken);
            }
            while (rootCheckedThisPass != repositoryContext.RepositoryRoot || outputPathCheckedThisPass != outputPathContext.OutputPath);
            RefreshError = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            PreflightResults = [];
            RefreshError = exception.Message;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Starts a fresh refresh whenever the active repository changes, so preflight and git status
    /// become valid for the new repository instead of continuing to reflect the one that was active
    /// when they were last checked. Discarded the same way the application's own startup refresh is:
    /// every failure mode this refresh can reach is already handled inside <see cref="RunRefreshAsync"/>,
    /// so nothing here would be silently lost.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused change details; the context reports only one property.</param>
    private void OnRepositoryContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = RefreshAsync();
    }

    /// <summary>
    /// Recomputes just the Output Folder check whenever the active output path override changes,
    /// leaving every other already-loaded check untouched: unlike a repository change, an output path
    /// change cannot affect any other required tool's availability, so re-running the full battery
    /// (several external version-probe processes) would be wasted work.
    /// </summary>
    /// <param name="sender">The unused event source.</param>
    /// <param name="e">The unused change details; the context reports only one property.</param>
    private void OnOutputPathContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        PreflightResults = preflightService.RefreshOutputFolderCheck(PreflightResults, repositoryContext.RepositoryRoot, outputPathContext.OutputPath);
    }
}
