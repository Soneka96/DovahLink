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

    // ---- Git ----

    /// <summary>
    /// The maximum time <c>git fetch</c> waits during a git status check before remote sync is
    /// reported as <see cref="RemoteSyncState.CouldNotVerify"/>.
    /// </summary>
    public static readonly TimeSpan GitFetchTimeout = TimeSpan.FromSeconds(10);

    // ---- Process ----

    /// <summary>
    /// The maximum time <c>ProcessCommandRunner.RunAsync</c> waits, after a cancelled command's
    /// process tree has been signaled to terminate, for every process in that tree to actually
    /// finish exiting -- not only the root process itself, which
    /// <see cref="System.Diagnostics.Process.WaitForExitAsync(System.Threading.CancellationToken)"/>
    /// already covers.
    /// </summary>
    public static readonly TimeSpan ProcessTreeTerminationTimeout = TimeSpan.FromSeconds(10);
}
