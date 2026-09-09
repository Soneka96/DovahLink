namespace DovahLink.DovahLinkBuilder.Persistence;

/// <summary>One recorded build, retained locally as build history.</summary>
/// <param name="Timestamp">When the build started.</param>
/// <param name="Result">The build's outcome.</param>
/// <param name="Version">The product version this build targeted.</param>
/// <param name="Profile">The build profile used, for example <c>Release</c>.</param>
/// <param name="Duration">How long the build ran before reaching its final outcome.</param>
/// <param name="ArtifactPath">The produced archive's path, when <paramref name="Result"/> is <see cref="BuildHistoryResult.Succeeded"/>; otherwise <see langword="null"/>.</param>
/// <param name="FailedStage">The stage that was running when the build failed, when <paramref name="Result"/> is <see cref="BuildHistoryResult.Failed"/>; otherwise <see langword="null"/>.</param>
/// <param name="Sha256">The produced archive's SHA-256 hash, when <paramref name="Result"/> is <see cref="BuildHistoryResult.Succeeded"/>; otherwise <see langword="null"/>.</param>
/// <param name="Note">An optional local note the user attached to this build.</param>
public sealed record BuildHistoryEntry(
    DateTimeOffset Timestamp,
    BuildHistoryResult Result,
    string Version,
    string Profile,
    TimeSpan Duration,
    string? ArtifactPath,
    string? FailedStage,
    string? Sha256,
    string? Note);
