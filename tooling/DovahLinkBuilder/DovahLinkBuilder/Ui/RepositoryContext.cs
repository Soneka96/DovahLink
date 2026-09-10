using System.ComponentModel;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the one repository root every repository-dependent service reads, so a change made on the
/// Settings page is observed immediately by every consumer instead of each one independently
/// caching the value it was constructed with.
/// </summary>
public interface IRepositoryContext : INotifyPropertyChanged
{
    /// <summary>Gets the currently active repository root: a configured override, or the auto-detected repository.</summary>
    string RepositoryRoot { get; }

    /// <summary>Sets the currently active repository root, notifying every consumer of the change.</summary>
    /// <param name="repositoryRoot">The repository root now in effect.</param>
    void SetRepositoryRoot(string repositoryRoot);
}

/// <inheritdoc cref="IRepositoryContext"/>
public sealed class RepositoryContext : ObservableObject, IRepositoryContext
{
    /// <summary>The backing field for <see cref="RepositoryRoot"/>.</summary>
    private string repositoryRoot;

    /// <summary>Initializes the context with the repository root resolved at startup.</summary>
    /// <param name="repositoryRoot">The initially active repository root.</param>
    public RepositoryContext(string repositoryRoot)
    {
        this.repositoryRoot = repositoryRoot;
    }

    /// <inheritdoc/>
    public string RepositoryRoot
    {
        get => repositoryRoot;
        private set => SetProperty(ref repositoryRoot, value);
    }

    /// <inheritdoc/>
    public void SetRepositoryRoot(string repositoryRoot) => RepositoryRoot = repositoryRoot;
}
