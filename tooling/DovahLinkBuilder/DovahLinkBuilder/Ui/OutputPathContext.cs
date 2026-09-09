using System.ComponentModel;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the one build output path override every output-path-dependent service reads, so a change
/// made on the Settings page is observed immediately by every consumer instead of each one
/// independently caching the value it was constructed with. Mirrors <see cref="IRepositoryContext"/>'s
/// shape; unlike that context, <see langword="null"/> is a real, always-valid value here (no override
/// configured, so the caller should fall back to the profile's own default output root) rather than a
/// state this context resolves away.
/// </summary>
public interface IOutputPathContext : INotifyPropertyChanged
{
    /// <summary>Gets the currently active build output path override, or <see langword="null"/> to use the active profile's default.</summary>
    string? OutputPath { get; }

    /// <summary>Sets the currently active build output path override, notifying every consumer of the change.</summary>
    /// <param name="outputPath">The output path override now in effect, or <see langword="null"/> to use the active profile's default.</param>
    void SetOutputPath(string? outputPath);
}

/// <inheritdoc cref="IOutputPathContext"/>
public sealed class OutputPathContext : ObservableObject, IOutputPathContext
{
    /// <summary>The backing field for <see cref="OutputPath"/>.</summary>
    private string? outputPath;

    /// <summary>Initializes the context with the output path override resolved at startup.</summary>
    /// <param name="outputPath">The initially active output path override, or <see langword="null"/>.</param>
    public OutputPathContext(string? outputPath)
    {
        this.outputPath = outputPath;
    }

    /// <inheritdoc/>
    public string? OutputPath
    {
        get => outputPath;
        private set => SetProperty(ref outputPath, value);
    }

    /// <inheritdoc/>
    public void SetOutputPath(string? outputPath) => OutputPath = outputPath;
}
