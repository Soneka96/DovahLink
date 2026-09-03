using DovahLink.Host.Identity;

namespace DovahLink.Host.Pairing;

/// <summary>The single, globally active pairing challenge a device must confirm to pair.</summary>
/// <param name="Id">
/// This exact challenge instance's identity, distinct from its owning <see cref="ClientId"/>. Lets a
/// caller that reserved this challenge before an asynchronous adapter step -- initial display or
/// manual redisplay -- prove on return that the challenge it is about to commit or roll back is
/// still this same instance, never a later challenge for the same owner.
/// </param>
/// <param name="OwnerClientId">The client identity that owns this pairing operation.</param>
/// <param name="Code">The numeric code displayed in-game and entered by the pairing device.</param>
/// <param name="ExpiresAtUtc">The UTC time after which this challenge can no longer be confirmed.</param>
public sealed record PairingChallenge(ChallengeId Id, ClientId OwnerClientId, string Code, DateTimeOffset ExpiresAtUtc);
