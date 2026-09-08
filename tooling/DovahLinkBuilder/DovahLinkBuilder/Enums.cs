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
