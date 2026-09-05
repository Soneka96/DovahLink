namespace DovahLink.Host.Trust;

/// <summary>
/// One coherent, single-lock read of a client's current trust record together with the store's
/// current <see cref="ITrustStore.SecurityFenceGeneration"/>, so a caller that must decide both
/// eligibility and the generation to stamp on new state (for example a pairing challenge) can never
/// combine a pre-mutation eligibility decision with a post-mutation generation, or vice versa.
/// </summary>
/// <param name="Record">The client's current trust record, or <see langword="null"/> if it is not known.</param>
/// <param name="SecurityFenceGeneration">The store's security fence generation at the same instant <paramref name="Record"/> was read.</param>
public sealed record TrustSecuritySnapshot(TrustRecord? Record, long SecurityFenceGeneration);
