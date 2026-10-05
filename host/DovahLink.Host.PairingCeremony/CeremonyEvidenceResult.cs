namespace DovahLink.Host.PairingCeremony;

/// <summary>The outcome of validating one local result: either the evidence, or the one reason it was rejected.</summary>
public sealed class CeremonyEvidenceResult
{
    /// <summary>Creates an outcome whose evidence presence always matches its rejection.</summary>
    /// <param name="evidence">The evidence, or <see langword="null"/> when rejected.</param>
    /// <param name="rejection">The rejection, or <see langword="null"/> when accepted.</param>
    private CeremonyEvidenceResult(SasCeremonyCompletedLocally? evidence, CeremonyEvidenceRejection? rejection)
    {
        Evidence = evidence;
        Rejection = rejection;
    }

    /// <summary>The evidence when accepted; otherwise <see langword="null"/>.</summary>
    public SasCeremonyCompletedLocally? Evidence { get; }

    /// <summary>Why the result was rejected, or <see langword="null"/> when accepted.</summary>
    public CeremonyEvidenceRejection? Rejection { get; }

    /// <summary>Creates an accepted outcome.</summary>
    /// <param name="evidence">The evidence.</param>
    /// <returns>The outcome.</returns>
    internal static CeremonyEvidenceResult Accepted(SasCeremonyCompletedLocally evidence) => new(evidence, null);

    /// <summary>Creates a rejected outcome.</summary>
    /// <param name="rejection">Why it was rejected.</param>
    /// <returns>The outcome.</returns>
    internal static CeremonyEvidenceResult Rejected(CeremonyEvidenceRejection rejection) => new(null, rejection);
}
