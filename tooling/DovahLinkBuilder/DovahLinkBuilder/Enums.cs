using System.IO;

namespace DovahLink.DovahLinkBuilder;

// ---- Build ----

/// <summary>Identifies one stage of the Adapter+Host build and packaging pipeline.</summary>
public enum BuildStage
{
    /// <summary>Validates repository inputs and the Visual Studio and Papyrus toolchains.</summary>
    ValidateRepository,

    /// <summary>Imports the Visual Studio build environment and configures the Adapter CMake project.</summary>
    ConfigureAdapter,

    /// <summary>Builds the Adapter binary in the selected <see cref="BuildProfile"/>'s configuration.</summary>
    BuildAdapter,

    /// <summary>Compiles the console-admin Papyrus script.</summary>
    CompilePapyrus,

    /// <summary>Publishes the Host executable.</summary>
    PublishHost,

    /// <summary>Assembles the Vortex-installable Adapter+Host package layout.</summary>
    AssemblePackage,

    /// <summary>Validates the assembled package layout.</summary>
    ValidatePackage,

    /// <summary>Creates the Vortex-installable ZIP archive.</summary>
    CreateZip,
}

/// <summary>Describes the current status of one <see cref="BuildStage"/>.</summary>
public enum BuildStageStatus
{
    /// <summary>The stage has not started.</summary>
    Pending,

    /// <summary>The stage is currently running.</summary>
    Running,

    /// <summary>The stage completed successfully.</summary>
    Succeeded,

    /// <summary>The stage failed.</summary>
    Failed,

    /// <summary>The stage was cancelled before it could finish.</summary>
    Cancelled,
}

/// <summary>Describes whether a required build tool is available, for non-throwing preflight checks.</summary>
public enum ToolchainAvailability
{
    /// <summary>The tool was located and validated.</summary>
    Found,

    /// <summary>The tool could not be located.</summary>
    Missing,

    /// <summary>The tool was located but failed validation.</summary>
    Invalid,

    /// <summary>Availability could not be determined, for example because a search path could not be evaluated.</summary>
    CouldNotCheck,
}

/// <summary>Identifies which build profile the Builder targets: what configuration the Adapter and Host are built in.</summary>
public enum BuildProfile
{
    /// <summary>Builds the Adapter and Host in their Debug configuration, for local development and troubleshooting.</summary>
    Debug,

    /// <summary>Builds the Adapter and Host in their Release configuration, packaged to a separate output location than <see cref="Release"/> for pre-release testing.</summary>
    Beta,

    /// <summary>Builds the Adapter and Host in their Release configuration for distribution.</summary>
    Release,
}

/// <summary>Maps <see cref="BuildProfile"/> to the concrete build configuration values it drives.</summary>
public static class BuildProfileExtensions
{
    /// <summary>Gets the CMake preset name (and matching Adapter build output directory name) for <paramref name="profile"/>.</summary>
    /// <param name="profile">The profile to build.</param>
    /// <remarks><see cref="BuildProfile.Beta"/> shares <see cref="BuildProfile.Release"/>'s Adapter binaries; only the Host publish configuration and package output location differ.</remarks>
    public static string ToCMakePreset(this BuildProfile profile) =>
        profile == BuildProfile.Debug ? "windows-x64-debug" : "windows-x64-release";

    /// <summary>Gets the <c>dotnet publish --configuration</c> value for <paramref name="profile"/>.</summary>
    /// <param name="profile">The profile to build.</param>
    public static string ToDotnetConfiguration(this BuildProfile profile) =>
        profile == BuildProfile.Debug ? "Debug" : "Release";

    /// <summary>Gets the lowercase output-path segment used to keep each profile's package output from colliding with another's.</summary>
    /// <param name="profile">The profile to build.</param>
    public static string ToOutputSegment(this BuildProfile profile) => profile.ToString().ToLowerInvariant();

    /// <summary>Gets the output root a build of <paramref name="profile"/> writes its published Host, assembled package, and ZIP under.</summary>
    /// <param name="profile">The profile to build.</param>
    /// <param name="repositoryRoot">The repository root the Builder is targeting.</param>
    /// <remarks><see cref="BuildProfile.Release"/> keeps the Builder's original, unqualified <c>tooling/out</c> location unchanged.</remarks>
    public static string ToOutputRoot(this BuildProfile profile, string repositoryRoot) => profile == BuildProfile.Release
        ? Path.Combine(repositoryRoot, "tooling", "out")
        : Path.Combine(repositoryRoot, "tooling", "out", profile.ToOutputSegment());
}

/// <summary>Describes the outcome of one recorded build, for <see cref="Persistence.BuildHistoryEntry"/>.</summary>
public enum BuildHistoryResult
{
    /// <summary>The build completed successfully and produced a validated package.</summary>
    Succeeded,

    /// <summary>The build failed before producing a package.</summary>
    Failed,

    /// <summary>The build was cancelled before producing a package.</summary>
    Cancelled,
}

// ---- Git ----

/// <summary>Describes whether the repository working tree has uncommitted changes.</summary>
public enum WorkingTreeState
{
    /// <summary>The working tree has no uncommitted changes.</summary>
    Clean,

    /// <summary>The working tree has uncommitted changes.</summary>
    Dirty,
}

/// <summary>Describes whether the current branch's commits are pushed to its upstream remote.</summary>
public enum RemoteSyncState
{
    /// <summary>Every local commit on the current branch exists on its upstream remote.</summary>
    Pushed,

    /// <summary>The current branch has local commits not yet on its upstream remote.</summary>
    NotPushed,

    /// <summary>
    /// Remote sync status could not be verified, for example because fetching the upstream remote
    /// failed or no upstream is configured. Never inferred from stale local tracking refs.
    /// </summary>
    CouldNotVerify,
}
