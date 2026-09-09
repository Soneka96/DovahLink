namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>Reports a status transition for one <see cref="Build.BuildStage"/> of the build pipeline.</summary>
/// <param name="Stage">The stage the transition applies to.</param>
/// <param name="Status">The stage's status after the transition.</param>
/// <param name="Duration">
/// The elapsed time since the stage started, when <paramref name="Status"/> is
/// <see cref="BuildStageStatus.Succeeded"/> or <see cref="BuildStageStatus.Failed"/>; otherwise
/// <see langword="null"/>.
/// </param>
public sealed record BuildStageEvent(
    BuildStage Stage,
    BuildStageStatus Status,
    TimeSpan? Duration = null);
