using DovahLink.Host.Identity;

namespace DovahLink.Host.Pairing;

/// <summary>
/// One coherent, single-lock snapshot of a client's pairing status, replacing any inference from a
/// nullable challenge alone. Read through <see cref="IPairingCoordinator.GetStatusSnapshot"/> so an
/// unacknowledged display reservation, a displayed challenge, a pending credential, another client's
/// active operation, and no operation at all stay structurally distinct instead of collapsing onto
/// the same nullable shape.
/// </summary>
/// <param name="Kind">Which of the structurally distinct pairing states the client currently owns.</param>
/// <param name="Challenge">
/// The owned, display-committed challenge. Present only when <paramref name="Kind"/> is
/// <see cref="PairingStatusKind.DisplayedChallenge"/>; <see langword="null"/> for every other kind.
/// </param>
public sealed record PairingStatusSnapshot(PairingStatusKind Kind, PairingChallenge? Challenge);
