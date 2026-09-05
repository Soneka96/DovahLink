namespace DovahLink.Host.Trust;

/// <summary>
/// Identifies exactly one <see cref="TrustResetService.ConfirmResetAsync"/> invocation's exclusive
/// claim on executing the active Factory Reset challenge. A fresh identity is issued only to the
/// single call that wins the race to claim a challenge, so a losing concurrent confirmation for the
/// same challenge, and a different invocation's own later release of its own claim, can never be
/// mistaken for this one.
/// </summary>
internal readonly record struct ResetClaimId
{
    /// <summary>The globally unique value backing this identity.</summary>
    private readonly Guid value;

    /// <summary>Creates a claim identity wrapping <paramref name="value"/>.</summary>
    /// <param name="value">The globally unique value to wrap.</param>
    private ResetClaimId(Guid value)
    {
        this.value = value;
    }

    /// <summary>Issues a fresh, globally unique claim identity.</summary>
    public static ResetClaimId New() => new(Guid.NewGuid());
}
