using DovahLink.DovahLinkBuilder;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Maintains the mutable state transitions shown by the builder UI.</summary>
public sealed class BuildViewModel
{
    /// <summary>The current builder state.</summary>
    public BuildUiState State { get; private set; } = new(BuildUiStatus.Ready, "Ready", null);

    /// <summary>
    /// Attempts to start an Adapter+Host build.
    /// </summary>
    /// <returns><c>true</c> if the build starts, <c>false</c> if a build is already running.</returns>
    public bool TryBeginBuild()
    {
        if (!State.CanBuild)
        {
            return false;
        }

        State = new BuildUiState(BuildUiStatus.Building, "Building the Adapter and Host...", null);
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
