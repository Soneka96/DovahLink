namespace DovahLink.Host.Pairing;

/// <summary>The result of requesting a pairing-code redisplay.</summary>
/// <param name="Outcome">Whether the code may be displayed.</param>
/// <param name="RetryAfter">The remaining manual redisplay cooldown, when applicable.</param>
public sealed record PairingRenotifyResult(PairingRenotifyOutcome Outcome, TimeSpan? RetryAfter = null);
