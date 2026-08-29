using DovahLink.Host.Identity;

namespace DovahLink.Host.Pairing;

/// <summary>The single, globally active pairing challenge a device must confirm to pair.</summary>
/// <param name="OwnerClientId">The client identity that owns this pairing operation.</param>
/// <param name="Code">The numeric code displayed in-game and entered by the pairing device.</param>
/// <param name="ExpiresAtUtc">The UTC time after which this challenge can no longer be confirmed.</param>
public sealed record PairingChallenge(ClientId OwnerClientId, string Code, DateTimeOffset ExpiresAtUtc);
