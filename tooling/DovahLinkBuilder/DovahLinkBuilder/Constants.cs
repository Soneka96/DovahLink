namespace DovahLink.DovahLinkBuilder;

// ---- Preflight ----

/// <summary>Cross-cutting constant values shared across the Builder.</summary>
public static class Constants
{
    /// <summary>
    /// The maximum time a version-probe preflight check (for example <c>dotnet --version</c>) waits
    /// before its availability is reported as <see cref="ToolchainAvailability.CouldNotCheck"/>.
    /// </summary>
    public static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(5);
}
