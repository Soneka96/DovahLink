namespace DovahLink.BridgeBuilder.Ui;

/// <summary>Describes the current state of a Bridge Builder operation.</summary>
public enum BuildUiStatus
{
    /// <summary>No build is currently running.</summary>
    Ready,

    /// <summary>A build is currently running.</summary>
    Building,

    /// <summary>The most recent build completed successfully.</summary>
    Succeeded,

    /// <summary>The most recent build failed.</summary>
    Failed,
}

/// <summary>Contains the status and user-facing message for the builder UI.</summary>
/// <param name="Status">The current build status.</param>
/// <param name="Message">The user-facing status message.</param>
/// <param name="ArchivePath">The completed archive path, when available.</param>
public sealed record BuildUiState(
    BuildUiStatus Status,
    string Message,
    string? ArchivePath)
{
    /// <summary>Gets whether a build is currently running.</summary>
    public bool IsBuilding => Status == BuildUiStatus.Building;

    /// <summary>Gets whether another build may be started.</summary>
    public bool CanBuild => !IsBuilding;
}

/// <summary>Maintains the mutable state transitions shown by the builder UI.</summary>
public sealed class BuildViewModel
{
    /// <summary>The current builder state.</summary>
    public BuildUiState State { get; private set; } = new(BuildUiStatus.Ready, "Ready", null);

    /// <summary>
    /// Attempts to start a bridge build.
    /// </summary>
    /// <returns><c>true</c> if the build starts, <c>false</c> if a build is already running.</returns>
    public bool TryBeginBuild()
    {
        if (!State.CanBuild)
        {
            return false;
        }

        State = new BuildUiState(BuildUiStatus.Building, "Building the bridge...", null);
        return true;
    }

    /// <summary>
    /// Marks the build as completed successfully and stores its archive path.
    /// </summary>
    /// <param name="archivePath">The path to the completed build archive.</param>
    public void Complete(string archivePath)
    {
        State = new BuildUiState(BuildUiStatus.Succeeded, "Build complete", archivePath);
    }

    /// <summary>
    /// Marks the build as failed with the specified message.
    /// </summary>
    /// <param name="message">The user-facing failure message.</param>
    public void Fail(string message)
    {
        State = new BuildUiState(BuildUiStatus.Failed, message, null);
    }
}
