using DovahLink.Host.Identity;

namespace DovahLink.Host.Readiness;

/// <summary>
/// One coherent answer from <see cref="IGameplayReadinessEvaluator"/>: the readiness value together
/// with the play-context and Adapter-connection identities it was evaluated against, taken from the
/// same validated read. A consumer that needs the value and the context it belongs to -- to stamp a
/// message or to detect that a context or connection changed across an await -- uses this instead of
/// reading <see cref="IGameplayReadinessEvaluator.IsReady"/> and a tracker separately, which could
/// pair an obsolete value with a newer identity.
/// </summary>
/// <param name="Ready">Whether gameplay was ready for exactly the identities below.</param>
/// <param name="PlayContext">The play context the value was evaluated for, or <see langword="null"/> when none was current or the read could not be stabilized.</param>
/// <param name="PlayContextTransitionGeneration">The play-context transition generation the value was evaluated at; zero when the read could not be stabilized.</param>
/// <param name="AdapterInstanceId">The most recently connected Adapter instance at that read (retained while the Adapter is unavailable, so <paramref name="Ready"/> is what says it is gone), or <see langword="null"/> when none has ever connected or the read could not be stabilized.</param>
/// <param name="ConnectionGeneration">The Adapter connection generation at that read (retained while the Adapter is unavailable); zero when none has connected or the read could not be stabilized.</param>
public sealed record GameplayReadinessSnapshot(
    bool Ready,
    PlayContextId? PlayContext,
    long PlayContextTransitionGeneration,
    AdapterInstanceId? AdapterInstanceId,
    long ConnectionGeneration);
