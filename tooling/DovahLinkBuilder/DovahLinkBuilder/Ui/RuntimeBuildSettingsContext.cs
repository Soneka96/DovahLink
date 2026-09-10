using System.ComponentModel;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>
/// Owns the current session's authoritative value of the behavior settings other already-running
/// application state reads live -- <see cref="OpenOutputFolderAfterSuccessfulBuild"/> and
/// <see cref="AutoScrollLogs"/> -- so every consumer observes the same in-memory value the Settings
/// page displays, whether or not best-effort persistence for it has actually succeeded. Mirrors
/// <see cref="IOutputPathContext"/>'s shape and rationale: persisted storage answers what should be
/// restored at next launch, never what the current session already treats as true.
/// </summary>
public interface IRuntimeBuildSettingsContext : INotifyPropertyChanged
{
    /// <summary>Gets whether to open the output folder after a build succeeds. Never applies to a failed or cancelled build.</summary>
    bool OpenOutputFolderAfterSuccessfulBuild { get; }

    /// <summary>Gets whether the log panel scrolls to the newest line automatically, for a build that starts from here on.</summary>
    bool AutoScrollLogs { get; }

    /// <summary>Sets the current session's authoritative value for <see cref="OpenOutputFolderAfterSuccessfulBuild"/>, notifying every consumer of the change.</summary>
    /// <param name="value">The new value.</param>
    void SetOpenOutputFolderAfterSuccessfulBuild(bool value);

    /// <summary>Sets the current session's authoritative value for <see cref="AutoScrollLogs"/>, notifying every consumer of the change.</summary>
    /// <param name="value">The new value.</param>
    void SetAutoScrollLogs(bool value);
}

/// <inheritdoc cref="IRuntimeBuildSettingsContext"/>
public sealed class RuntimeBuildSettingsContext : ObservableObject, IRuntimeBuildSettingsContext
{
    /// <summary>The backing field for <see cref="OpenOutputFolderAfterSuccessfulBuild"/>.</summary>
    private bool openOutputFolderAfterSuccessfulBuild;

    /// <summary>The backing field for <see cref="AutoScrollLogs"/>.</summary>
    private bool autoScrollLogs;

    /// <summary>Initializes the context with the values resolved at startup.</summary>
    /// <param name="openOutputFolderAfterSuccessfulBuild">The initially active value for <see cref="OpenOutputFolderAfterSuccessfulBuild"/>.</param>
    /// <param name="autoScrollLogs">The initially active value for <see cref="AutoScrollLogs"/>.</param>
    public RuntimeBuildSettingsContext(bool openOutputFolderAfterSuccessfulBuild, bool autoScrollLogs)
    {
        this.openOutputFolderAfterSuccessfulBuild = openOutputFolderAfterSuccessfulBuild;
        this.autoScrollLogs = autoScrollLogs;
    }

    /// <inheritdoc/>
    public bool OpenOutputFolderAfterSuccessfulBuild
    {
        get => openOutputFolderAfterSuccessfulBuild;
        private set => SetProperty(ref openOutputFolderAfterSuccessfulBuild, value);
    }

    /// <inheritdoc/>
    public bool AutoScrollLogs
    {
        get => autoScrollLogs;
        private set => SetProperty(ref autoScrollLogs, value);
    }

    /// <inheritdoc/>
    public void SetOpenOutputFolderAfterSuccessfulBuild(bool value) => OpenOutputFolderAfterSuccessfulBuild = value;

    /// <inheritdoc/>
    public void SetAutoScrollLogs(bool value) => AutoScrollLogs = value;
}
