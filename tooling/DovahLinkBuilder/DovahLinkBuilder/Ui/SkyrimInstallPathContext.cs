using System.ComponentModel;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Owns the configured Skyrim / Creation Kit installation path every Papyrus consumer reads.</summary>
public interface ISkyrimInstallPathContext : INotifyPropertyChanged
{
    /// <summary>Gets the configured installation path, or <see langword="null"/> to use automatic discovery.</summary>
    string? SkyrimInstallPath { get; }

    /// <summary>Sets the configured installation path, notifying every Papyrus consumer of the change.</summary>
    /// <param name="skyrimInstallPath">The installation path now in effect, or <see langword="null"/> to use automatic discovery.</param>
    void SetSkyrimInstallPath(string? skyrimInstallPath);
}

/// <inheritdoc cref="ISkyrimInstallPathContext"/>
public sealed class SkyrimInstallPathContext : ObservableObject, ISkyrimInstallPathContext
{
    /// <summary>The backing field for <see cref="SkyrimInstallPath"/>.</summary>
    private string? skyrimInstallPath;

    /// <summary>Initializes the context with the configured installation path resolved at startup.</summary>
    /// <param name="skyrimInstallPath">The initially active installation path, or <see langword="null"/>.</param>
    public SkyrimInstallPathContext(string? skyrimInstallPath)
    {
        this.skyrimInstallPath = skyrimInstallPath;
    }

    /// <inheritdoc/>
    public string? SkyrimInstallPath
    {
        get => skyrimInstallPath;
        private set => SetProperty(ref skyrimInstallPath, value);
    }

    /// <inheritdoc/>
    public void SetSkyrimInstallPath(string? skyrimInstallPath) => SkyrimInstallPath = skyrimInstallPath;
}
