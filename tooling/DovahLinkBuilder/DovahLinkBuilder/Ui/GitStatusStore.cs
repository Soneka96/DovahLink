using System.ComponentModel;
using DovahLink.DovahLinkBuilder.Git;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the one git status both the Build and Environment pages read, so a refresh triggered from
/// either page updates both instead of each page loading and caching its own independent copy.
/// </summary>
public interface IGitStatusStore : INotifyPropertyChanged
{
    /// <summary>Gets the most recently loaded git status, or <see langword="null"/> before the first refresh or when it could not be determined.</summary>
    GitSourceStatus? Status { get; }

    /// <summary>Gets the most recent refresh failure message, or <see langword="null"/> when the last refresh succeeded.</summary>
    string? StatusError { get; }

    /// <summary>
    /// Refreshes the shared git status, reporting an <see cref="InvalidOperationException"/> through
    /// <see cref="StatusError"/> instead of throwing -- <see cref="IGitStatusService.GetStatusAsync"/>'s
    /// one documented failure mode; any other exception still propagates.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the outstanding check.</param>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGitStatusStore"/>
public sealed class GitStatusStore : ObservableObject, IGitStatusStore
{
    /// <summary>Reports the repository's branch, working tree, and remote sync state.</summary>
    private readonly IGitStatusService gitStatusService;

    /// <summary>The shared repository root every consumer checks.</summary>
    private readonly IRepositoryContext repositoryContext;

    /// <summary>The backing field for <see cref="Status"/>.</summary>
    private GitSourceStatus? status;

    /// <summary>The backing field for <see cref="StatusError"/>.</summary>
    private string? statusError;

    /// <summary>Creates a store over the given git status service, always checking the currently active repository root.</summary>
    /// <param name="gitStatusService">Reports the repository's branch, working tree, and remote sync state.</param>
    /// <param name="repositoryContext">The shared repository root every consumer checks.</param>
    public GitStatusStore(IGitStatusService gitStatusService, IRepositoryContext repositoryContext)
    {
        this.gitStatusService = gitStatusService;
        this.repositoryContext = repositoryContext;
    }

    /// <inheritdoc/>
    public GitSourceStatus? Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    /// <inheritdoc/>
    public string? StatusError
    {
        get => statusError;
        private set => SetProperty(ref statusError, value);
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Status = await gitStatusService.GetStatusAsync(repositoryContext.RepositoryRoot, cancellationToken);
            StatusError = null;
        }
        catch (InvalidOperationException exception)
        {
            Status = null;
            StatusError = exception.Message;
        }
    }
}
