using DovahLink.DovahLinkBuilder;

namespace DovahLink.DovahLinkBuilder.Ui;

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
