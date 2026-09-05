using DovahLink.Host.Identity;

namespace DovahLink.Host.Pairing;

/// <summary>The result of requesting a pairing-code redisplay.</summary>
/// <param name="Outcome">Whether the code may be displayed.</param>
/// <param name="RetryAfter">The remaining manual redisplay cooldown, when applicable.</param>
/// <param name="ChallengeId">
/// The exact challenge this outcome was evaluated against, populated only when
/// <see cref="PairingCoordinator.TryRenotify"/> reports <see cref="PairingRenotifyOutcome.Renotified"/>.
/// The caller must pass this back to <see cref="PairingCoordinator.CommitRenotify"/> so a stale
/// commit can never apply to a later, replacement challenge.
/// </param>
/// <param name="Code">
/// The still-active code to redisplay, populated alongside <paramref name="ChallengeId"/>.
/// </param>
public sealed record PairingRenotifyResult(
    PairingRenotifyOutcome Outcome,
    TimeSpan? RetryAfter = null,
    ChallengeId? ChallengeId = null,
    string? Code = null)
{
    /// <summary>
    /// The exact redisplay reservation this outcome grants, populated only alongside
    /// <see cref="PairingRenotifyOutcome.Renotified"/> from <see cref="PairingCoordinator.TryRenotify"/>.
    /// The caller must pass it back to <see cref="PairingCoordinator.CommitRenotify"/> on success or
    /// <see cref="PairingCoordinator.RollbackRenotify"/> otherwise. Exists only to route a caller's own
    /// redisplay attempt back to the coordinator call that resolves it; it never crosses the wire
    /// protocol itself.
    /// </summary>
    public RenotifyClaimId? ClaimId { get; init; }
}
