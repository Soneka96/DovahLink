using DovahLink.Host.Identity;

namespace DovahLink.Host.Pairing;

/// <summary>The result of finalizing a pending pairing credential.</summary>
/// <param name="Outcome">The finalization result.</param>
/// <param name="ClientId">The paired client's identity when trust was established.</param>
/// <param name="Credential">The credential associated with the trusted record.</param>
/// <param name="ShortId">The administration short id when trust was established.</param>
/// <param name="DisplayName">The trusted device's presentation label.</param>
public sealed record PairingCommitResult(
    PairingCommitOutcome Outcome,
    ClientId? ClientId = null,
    string? Credential = null,
    string? ShortId = null,
    string? DisplayName = null);
