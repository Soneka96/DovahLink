using System.ComponentModel;
using DovahLink.DovahLinkBuilder.Build;

namespace DovahLink.DovahLinkBuilder.Ui;

/// <summary>Represents one segment of the build pipeline's honest, per-stage progress display.</summary>
public interface IBuildStageViewModel : INotifyPropertyChanged
{
    /// <summary>Gets the pipeline stage this segment represents.</summary>
    BuildStage Stage { get; }

    /// <summary>Gets the human-readable name shown for this stage.</summary>
    string DisplayName { get; }

    /// <summary>Gets this stage's current status.</summary>
    BuildStageStatus Status { get; }

    /// <summary>Gets how long this stage took, once it has succeeded or failed; otherwise <see langword="null"/>.</summary>
    TimeSpan? Duration { get; }

    /// <summary>Applies a reported status transition for this stage.</summary>
    /// <param name="stageEvent">The reported transition; its <see cref="BuildStageEvent.Stage"/> is assumed to already match <see cref="Stage"/>.</param>
    void Apply(BuildStageEvent stageEvent);

    /// <summary>Resets this stage back to Pending with no recorded duration, before a new build starts.</summary>
    void Reset();
}

/// <inheritdoc cref="IBuildStageViewModel"/>
public sealed class BuildStageViewModel : ObservableObject, IBuildStageViewModel
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

    /// <inheritdoc/>
    public BuildStage Stage { get; }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public BuildStageStatus Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    /// <inheritdoc/>
    public TimeSpan? Duration
    {
        get => duration;
        private set => SetProperty(ref duration, value);
    }

    /// <inheritdoc/>
    public void Apply(BuildStageEvent stageEvent)
    {
        Status = stageEvent.Status;
        Duration = stageEvent.Duration;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        Status = BuildStageStatus.Pending;
        Duration = null;
    }
}
