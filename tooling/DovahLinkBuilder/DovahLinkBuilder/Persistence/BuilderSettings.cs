namespace DovahLink.DovahLinkBuilder.Persistence;

/// <summary>The Builder's persisted local preferences.</summary>
/// <param name="RepositoryPath">The repository path override, or <see langword="null"/> to use the auto-detected repository.</param>
/// <param name="SkyrimInstallPath">The Skyrim / Creation Kit install path, set only through a folder picker, or <see langword="null"/> when not configured. No part of the Builder detects this path automatically; nothing currently reads this value.</param>
/// <param name="OutputPath">The build output path override, or <see langword="null"/> to use the repository's default <c>tooling/out</c>.</param>
/// <param name="OpenOutputFolderAfterSuccessfulBuild">Whether to open the output folder after a build succeeds. Never applies to a failed or cancelled build.</param>
/// <param name="AutoScrollLogs">Whether the log panel scrolls to the newest line automatically.</param>
/// <param name="VerboseCommandOutput">Whether build and packaging commands report their full raw output rather than a condensed summary.</param>
/// <param name="NotifyWhenBuildCompletes">Whether to show a desktop notification when a build finishes.</param>
/// <param name="WindowLeft">The main window's last saved left position, or <see langword="null"/> when never saved.</param>
/// <param name="WindowTop">The main window's last saved top position, or <see langword="null"/> when never saved.</param>
/// <param name="WindowWidth">The main window's last saved width, or <see langword="null"/> when never saved.</param>
/// <param name="WindowHeight">The main window's last saved height, or <see langword="null"/> when never saved.</param>
/// <param name="WindowIsMaximized">Whether the main window was maximized when it was last closed.</param>
public sealed record BuilderSettings(
    string? RepositoryPath = null,
    string? SkyrimInstallPath = null,
    string? OutputPath = null,
    bool OpenOutputFolderAfterSuccessfulBuild = true,
    bool AutoScrollLogs = true,
    bool VerboseCommandOutput = false,
    bool NotifyWhenBuildCompletes = false,
    double? WindowLeft = null,
    double? WindowTop = null,
    double? WindowWidth = null,
    double? WindowHeight = null,
    bool WindowIsMaximized = false);
