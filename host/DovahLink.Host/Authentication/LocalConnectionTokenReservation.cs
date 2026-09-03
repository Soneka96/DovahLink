namespace DovahLink.Host.Authentication;

/// <summary>
/// Opaque proof that a caller's <see cref="ILocalConnectionTokenAuthenticator.TryValidate"/> call
/// reserved a specific token issuance. Carries no secret material -- only an internal generation
/// number identifying which issuance it was stamped against -- so a caller can hold and pass it
/// around without ever seeing the token itself. Presenting a reservation from a superseded issuance
/// to <see cref="ILocalConnectionTokenAuthenticator.CommitConsumption"/> or
/// <see cref="ILocalConnectionTokenAuthenticator.RollbackReservation"/> is a safe no-op: a stale
/// reservation can never commit, roll back, or otherwise affect a newer one.
/// </summary>
public readonly record struct LocalConnectionTokenReservation
{
    /// <summary>
    /// The token issuance this reservation was stamped against. <c>0</c> (the type's default value)
    /// can never match a real issuance, since <see cref="LocalConnectionTokenAuthenticator"/> starts
    /// generation numbering at <c>1</c>.
    /// </summary>
    internal long Generation { get; }

    /// <summary>Creates a reservation stamped with a specific token issuance.</summary>
    /// <param name="generation">The token issuance this reservation was stamped against.</param>
    internal LocalConnectionTokenReservation(long generation)
    {
        Generation = generation;
    }
}
