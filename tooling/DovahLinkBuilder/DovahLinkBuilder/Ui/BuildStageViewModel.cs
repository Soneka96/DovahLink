using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Represents one segment of the build pipeline's honest, per-stage progress display.</summary>
public sealed class BuildStageViewModel : ObservableObject
{
    /// <summary>The backing field for <see cref="Status"/>.</summary>
    private BuildStageStatus status = BuildStageStatus.Pending;

    /// <summary>The backing field for <see cref="Duration"/>.</summary>
    private TimeSpan? duration;

    /// <summary>Initializes a stage segment for the given pipeline stage, starting Pending.</summary>
    /// <param name="stage">The pipeline stage this segment represents.</param>
    public BuildStageViewModel(BuildStage stage)
    {
        Stage = stage;
    }

    /// <summary>Gets the pipeline stage this segment represents.</summary>
    public BuildStage Stage { get; }

    /// <summary>Gets the human-readable name shown for this stage.</summary>
    public string DisplayName => Stage switch
    {
        BuildStage.ValidateRepository => "Validate repository",
        BuildStage.ConfigureAdapter => "Configure Adapter",
        BuildStage.BuildAdapter => "Build Adapter",
        BuildStage.CompilePapyrus => "Compile Papyrus",
        BuildStage.PublishHost => "Publish Host",
        BuildStage.AssemblePackage => "Assemble package",
        BuildStage.ValidatePackage => "Validate package",
        BuildStage.CreateZip => "Create ZIP",
        _ => Stage.ToString(),
    };

    /// <summary>Gets this stage's current status.</summary>
    public BuildStageStatus Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    /// <summary>Gets how long this stage took, once it has succeeded or failed; otherwise <see langword="null"/>.</summary>
    public TimeSpan? Duration
    {
        get => duration;
        private set => SetProperty(ref duration, value);
    }

    /// <summary>Applies a reported status transition for this stage.</summary>
    /// <param name="stageEvent">The reported transition; its <see cref="BuildStageEvent.Stage"/> is assumed to already match <see cref="Stage"/>.</param>
    internal void Apply(BuildStageEvent stageEvent)
    {
        Status = stageEvent.Status;
        Duration = stageEvent.Duration;
    }

    /// <summary>Resets this stage back to Pending with no recorded duration, before a new build starts.</summary>
    internal void Reset()
    {
        Status = BuildStageStatus.Pending;
        Duration = null;
    }
}
