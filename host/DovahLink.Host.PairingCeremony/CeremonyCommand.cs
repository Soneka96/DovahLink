namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// One explicit local decision queued for the owner thread: either an exposure authorization naming
/// an attempt, or a SAS decision naming an exact ceremony identity.
/// </summary>
internal sealed class CeremonyCommand
{
    /// <summary>Creates a command; use the factory methods.</summary>
    /// <param name="exposureAttempt">The attempt an exposure authorization names, or <see langword="null"/>.</param>
    /// <param name="ceremonyIdentity">The ceremony identity a SAS decision names, or <see langword="null"/>.</param>
    /// <param name="decision">The SAS decision, or <see langword="null"/>.</param>
    private CeremonyCommand(CeremonyAttemptId? exposureAttempt, byte[]? ceremonyIdentity, SasComparisonDecision? decision)
    {
        ExposureAttempt = exposureAttempt;
        CeremonyIdentity = ceremonyIdentity;
        Decision = decision;
    }

    /// <summary>The attempt an exposure authorization names, or <see langword="null"/> for a SAS decision.</summary>
    public CeremonyAttemptId? ExposureAttempt { get; }

    /// <summary>The exact ceremony identity a SAS decision names, or <see langword="null"/> for an exposure authorization.</summary>
    public byte[]? CeremonyIdentity { get; }

    /// <summary>The SAS decision, or <see langword="null"/> for an exposure authorization.</summary>
    public SasComparisonDecision? Decision { get; }

    /// <summary>Creates an exposure authorization.</summary>
    /// <param name="attempt">The attempt it names.</param>
    /// <returns>The command.</returns>
    public static CeremonyCommand AuthorizeExposure(CeremonyAttemptId attempt) => new(attempt, null, null);

    /// <summary>Creates a SAS decision.</summary>
    /// <param name="ceremonyIdentity">The exact ceremony identity it names; copied.</param>
    /// <param name="decision">The decision.</param>
    /// <returns>The command.</returns>
    public static CeremonyCommand SasDecision(ReadOnlySpan<byte> ceremonyIdentity, SasComparisonDecision decision) =>
        new(null, ceremonyIdentity.ToArray(), decision);
}
