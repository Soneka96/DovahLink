using DovahLink.Host.Identity;

namespace DovahLink.Host.Pairing;

/// <summary>The outcome of evaluating or finalizing a pairing operation.</summary>
/// <param name="Outcome">
/// The operation result, such as credential issuance, expiry, invalid input, or a persistence
/// failure.
/// </param>
/// <param name="ClientId">The pairing client's identity, present when a credential belongs to it.</param>
/// <param name="Credential">The raw credential, present only when one was issued or trusted.</param>
/// <param name="ShortId">The administration short id, present after final trust.</param>
/// <param name="DisplayName">The presentation label associated with final trust.</param>
/// <param name="RetryAfter">The remaining pacing duration, when applicable.</param>
/// <param name="ShouldAutoRenotify">Whether the owner may be shown the code again after a wrong attempt.</param>
public sealed record PairingConfirmationResult(
    PairingConfirmOutcome Outcome,
    ClientId? ClientId,
    string? Credential,
    string? ShortId = null,
    string? DisplayName = null,
    TimeSpan? RetryAfter = null,
    bool ShouldAutoRenotify = false);
