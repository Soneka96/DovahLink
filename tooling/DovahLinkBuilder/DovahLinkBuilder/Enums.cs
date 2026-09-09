namespace DovahLink.DovahLinkBuilder;

// ---- Build ----

/// <summary>Identifies one stage of the Adapter+Host build and packaging pipeline.</summary>
public enum BuildStage
{
    /// <summary>Validates repository inputs and the Visual Studio and Papyrus toolchains.</summary>
    ValidateRepository,

    /// <summary>Imports the Visual Studio build environment and configures the Adapter CMake project.</summary>
    ConfigureAdapter,

    /// <summary>Builds the Adapter Release binary.</summary>
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
