namespace DovahLink.Host.Pairing;

/// <summary>
/// Identifies exactly one <see cref="PairingCoordinator.CommitPendingAsync"/> invocation's exclusive
/// claim on finalizing a pending credential. A fresh identity is issued only to the single call that
/// wins the race to claim a reservation, so a losing concurrent call for the same reservation, and a
/// different invocation's own later release of its own claim, can never be mistaken for this one.
/// </summary>
internal readonly record struct CommitClaimId
{
    /// <summary>The globally unique value backing this identity.</summary>
    private readonly Guid value;

    /// <summary>Creates a claim identity wrapping <paramref name="value"/>.</summary>
    /// <param name="value">The globally unique value to wrap.</param>
    private CommitClaimId(Guid value)
    {
        this.value = value;
    }

    /// <summary>Issues a fresh, globally unique claim identity.</summary>
    public static CommitClaimId New() => new(Guid.NewGuid());
}
