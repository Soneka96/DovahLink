using DovahLink.DovahLinkBuilder.Build;
using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies one build pipeline stage segment's status/duration tracking.</summary>
public sealed class BuildStageViewModelTests
{
    /// <summary>Starts Pending with no recorded duration.</summary>
    [Fact]
    public void StartsPendingWithNoDuration()
    {
        var stage = new BuildStageViewModel(BuildStage.BuildAdapter);

        Assert.Equal(BuildStageStatus.Pending, stage.Status);
        Assert.Null(stage.Duration);
    }

    /// <summary>Maps every <see cref="BuildStage"/> value to a distinct, non-empty display name.</summary>
    /// <param name="value">The stage to check.</param>
    [Theory]
    [InlineData(BuildStage.ValidateRepository)]
    [InlineData(BuildStage.ConfigureAdapter)]
    [InlineData(BuildStage.BuildAdapter)]
    [InlineData(BuildStage.CompilePapyrus)]
    [InlineData(BuildStage.PublishHost)]
    [InlineData(BuildStage.AssemblePackage)]
    [InlineData(BuildStage.ValidatePackage)]
    [InlineData(BuildStage.CreateZip)]
    public void DisplayNameIsNeverEmpty(BuildStage value)
    {
        var stage = new BuildStageViewModel(value);

        Assert.False(string.IsNullOrWhiteSpace(stage.DisplayName));
    }

    /// <summary>Applies a reported transition's status and duration.</summary>
    [Fact]
    public void ApplyUpdatesStatusAndDuration()
    {
        var stage = new BuildStageViewModel(BuildStage.CompilePapyrus);
        var stageEvent = new BuildStageEvent(BuildStage.CompilePapyrus, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(4.2));

        stage.Apply(stageEvent);

        Assert.Equal(BuildStageStatus.Succeeded, stage.Status);
        Assert.Equal(TimeSpan.FromSeconds(4.2), stage.Duration);
    }

    /// <summary>Applies a Failed transition, recording its duration same as a Succeeded one would.</summary>
    [Fact]
    public void ApplyUpdatesStatusAndDurationForAFailedTransition()
    {
        var stage = new BuildStageViewModel(BuildStage.BuildAdapter);

        stage.Apply(new BuildStageEvent(BuildStage.BuildAdapter, BuildStageStatus.Failed, TimeSpan.FromSeconds(3)));

        Assert.Equal(BuildStageStatus.Failed, stage.Status);
        Assert.Equal(TimeSpan.FromSeconds(3), stage.Duration);
    }

    /// <summary>Applying a Running transition carries no duration yet.</summary>
    [Fact]
    public void ApplyingARunningTransitionLeavesDurationNull()
    {
        var stage = new BuildStageViewModel(BuildStage.BuildAdapter);

        stage.Apply(new BuildStageEvent(BuildStage.BuildAdapter, BuildStageStatus.Running));

        Assert.Equal(BuildStageStatus.Running, stage.Status);
        Assert.Null(stage.Duration);
    }

    /// <summary>Resets a stage that already succeeded back to Pending with no duration.</summary>
    [Fact]
    public void ResetClearsStatusAndDuration()
    {
        var stage = new BuildStageViewModel(BuildStage.BuildAdapter);
        stage.Apply(new BuildStageEvent(BuildStage.BuildAdapter, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(1)));

        stage.Reset();

        Assert.Equal(BuildStageStatus.Pending, stage.Status);
        Assert.Null(stage.Duration);
    }

    /// <summary>Raises <c>PropertyChanged</c> for both <see cref="BuildStageViewModel.Status"/> and <see cref="BuildStageViewModel.Duration"/> when applying a transition.</summary>
    [Fact]
    public void ApplyRaisesPropertyChangedForStatusAndDuration()
    {
        var stage = new BuildStageViewModel(BuildStage.BuildAdapter);
        var raisedPropertyNames = new List<string?>();
        stage.PropertyChanged += (_, args) => raisedPropertyNames.Add(args.PropertyName);

        stage.Apply(new BuildStageEvent(BuildStage.BuildAdapter, BuildStageStatus.Succeeded, TimeSpan.FromSeconds(1)));

        Assert.Equal([nameof(BuildStageViewModel.Status), nameof(BuildStageViewModel.Duration)], raisedPropertyNames);
    }
}
