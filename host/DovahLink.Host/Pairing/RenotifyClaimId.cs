namespace DovahLink.Host.Pairing;

/// <summary>
/// Identifies exactly one <see cref="PairingCoordinator.TryRenotify"/> invocation's exclusive
/// reservation of the active challenge's redisplay. A fresh identity is issued only to the single
/// call that wins the race to claim the reservation, so a losing concurrent
/// <see cref="PairingCoordinator.TryRenotify"/> call, and a stale
/// <see cref="PairingCoordinator.CommitRenotify"/> or <see cref="PairingCoordinator.RollbackRenotify"/>
/// call for a reservation this exact challenge has since superseded, can never be mistaken for this
/// one. Public, unlike <see cref="CommitClaimId"/>, because it must flow through
/// <see cref="IPairingCoordinator"/>'s own public surface from <see cref="PairingCoordinator.TryRenotify"/>
/// to whichever of <see cref="PairingCoordinator.CommitRenotify"/> or
/// <see cref="PairingCoordinator.RollbackRenotify"/> resolves it -- it never crosses the wire
/// protocol itself.
/// </summary>
public readonly record struct RenotifyClaimId
{
    /// <summary>The globally unique value backing this identity.</summary>
    private readonly Guid value;

    /// <summary>Creates a claim identity wrapping <paramref name="value"/>.</summary>
    /// <param name="value">The globally unique value to wrap.</param>
    private RenotifyClaimId(Guid value)
    {
        this.value = value;
    }

    /// <summary>Issues a fresh, globally unique claim identity.</summary>
    public static RenotifyClaimId New() => new(Guid.NewGuid());
}
