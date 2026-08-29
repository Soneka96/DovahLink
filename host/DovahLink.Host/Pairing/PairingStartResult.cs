namespace DovahLink.Host.Pairing;

/// <summary>The result of beginning or resuming a pairing operation.</summary>
/// <param name="Outcome">The ownership result for the requesting client.</param>
/// <param name="Challenge">The new challenge when one was started.</param>
public sealed record PairingStartResult(PairingStartOutcome Outcome, PairingChallenge? Challenge);
