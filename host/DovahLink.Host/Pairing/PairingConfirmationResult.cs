using DovahLink.Host.Identity;

namespace DovahLink.Host.Pairing;

/// <summary>The outcome of confirming a pairing challenge's code.</summary>
/// <param name="Outcome">
/// The resulting state: <see cref="Host.PairingState.Trusted"/> on success, or
/// <see cref="Host.PairingState.Rejected"/> or <see cref="Host.PairingState.Expired"/> on failure.
/// </param>
/// <param name="ClientId">The newly trusted device's identity, present only when <paramref name="Outcome"/> is <see cref="Host.PairingState.Trusted"/>.</param>
/// <param name="Credential">The newly issued credential the device must persist, present only when <paramref name="Outcome"/> is <see cref="Host.PairingState.Trusted"/>.</param>
public sealed record PairingConfirmationResult(PairingState Outcome, ClientId? ClientId, string? Credential);
