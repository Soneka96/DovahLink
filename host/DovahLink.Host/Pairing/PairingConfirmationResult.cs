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
/// <param name="AutoRenotifyCode">
/// The exact code of the challenge this wrong attempt was evaluated against, present only alongside
/// <paramref name="ShouldAutoRenotify"/>. Lets a caller request the adapter redisplay this exact code
/// without a separate, later status read that could race a challenge replacement and redisplay a
/// different challenge's code under this attempt's "wrong code" presentation.
/// </param>
public sealed record PairingConfirmationResult(
    PairingConfirmOutcome Outcome,
    ClientId? ClientId,
    string? Credential,
    string? ShortId = null,
    string? DisplayName = null,
    TimeSpan? RetryAfter = null,
    bool ShouldAutoRenotify = false,
    string? AutoRenotifyCode = null);
