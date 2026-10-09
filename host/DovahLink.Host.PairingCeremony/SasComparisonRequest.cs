namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// A SAS a human must now compare with the other device. The display is comparison data only: it
/// authenticates, approves, and trusts nothing. A decision must name <see cref="CeremonyIdentity"/>
/// exactly, so it can never apply to another ceremony.
/// </summary>
public sealed class SasComparisonRequest
{
    /// <summary>The 32-byte ceremony identity, owned by this instance.</summary>
    private readonly byte[] ceremonyIdentity;

    /// <summary>Creates a detached comparison request.</summary>
    /// <param name="attempt">The attempt this SAS belongs to.</param>
    /// <param name="ceremonyIdentity">The exact ceremony identity the decision must name.</param>
    /// <param name="decimalDisplay">The <c>NNNN NNNN NNNN</c> display to show.</param>
    internal SasComparisonRequest(CeremonyAttemptId attempt, ReadOnlySpan<byte> ceremonyIdentity, string decimalDisplay)
    {
        Attempt = attempt;
        this.ceremonyIdentity = ceremonyIdentity.ToArray();
        DecimalDisplay = decimalDisplay;
    }

    /// <summary>The attempt this SAS belongs to.</summary>
    public CeremonyAttemptId Attempt { get; }

    /// <summary>The exact ceremony identity a decision must name.</summary>
    public ReadOnlySpan<byte> CeremonyIdentity => ceremonyIdentity;

    /// <summary>The <c>NNNN NNNN NNNN</c> SAS display.</summary>
    public string DecimalDisplay { get; }
}
