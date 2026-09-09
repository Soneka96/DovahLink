namespace DovahLink.DovahLinkBuilder;

// ---- Ui ----

/// <summary>Describes the current state of a DovahLink Builder operation.</summary>
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
