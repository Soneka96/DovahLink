namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// Turns one detached local result into evidence only if it is the exact ceremony this Responder Host
/// expected: an Initiator peer, the frozen protocol profile, this Host's own shared context, and a
/// whole authenticated peer frame equal byte for byte to a frame built beforehand from candidate values.
/// It never parses the peer frame; candidate values become evidence only through that equality.
/// </summary>
public interface ICeremonyResultValidator
{
    /// <summary>Validates one local result.</summary>
    /// <param name="result">The detached local result.</param>
    /// <param name="localBootstrap">This Host's own Bootstrap, whose shared context is the only one accepted.</param>
    /// <param name="expectedPeerBootstrap">The canonical peer frame built from candidate values held before the ceremony.</param>
    /// <returns>The evidence, or the first reason it was rejected.</returns>
    CeremonyEvidenceResult Validate(
        CeremonyResultSnapshot result, CeremonyBootstrapFields localBootstrap, ReadOnlySpan<byte> expectedPeerBootstrap);
}

/// <inheritdoc cref="ICeremonyResultValidator"/>
public sealed class CeremonyResultValidator : ICeremonyResultValidator
{
    /// <inheritdoc/>
    public CeremonyEvidenceResult Validate(
        CeremonyResultSnapshot result, CeremonyBootstrapFields localBootstrap, ReadOnlySpan<byte> expectedPeerBootstrap)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(localBootstrap);

        if (result.PeerRole != CeremonyPeerRole.Initiator)
        {
            return CeremonyEvidenceResult.Rejected(CeremonyEvidenceRejection.UnexpectedPeerRole);
        }

        if (result.ProfileVersion != Constants.ExpectedProfileVersion ||
            !result.ProfileIdentifier.SequenceEqual(Constants.ExpectedProfileIdentifier))
        {
            return CeremonyEvidenceResult.Rejected(CeremonyEvidenceRejection.UnexpectedProfile);
        }

        if (!result.AuthenticatedSharedContext.SequenceEqual(localBootstrap.SharedContext))
        {
            return CeremonyEvidenceResult.Rejected(CeremonyEvidenceRejection.SharedContextMismatch);
        }

        if (expectedPeerBootstrap.IsEmpty || !result.AuthenticatedPeerBootstrap.SequenceEqual(expectedPeerBootstrap))
        {
            return CeremonyEvidenceResult.Rejected(CeremonyEvidenceRejection.PeerBootstrapMismatch);
        }

        return CeremonyEvidenceResult.Accepted(new SasCeremonyCompletedLocally(result.CeremonyIdentity, result.AuthenticatedPeerBootstrap));
    }
}
