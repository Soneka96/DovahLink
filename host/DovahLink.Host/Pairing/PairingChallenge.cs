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
/// <param name="SecurityFenceGeneration">
/// The trust store's <see cref="Trust.ITrustStore.SecurityFenceGeneration"/> observed the instant
/// this challenge was created. Confirming this challenge is only ever allowed to mint a pending
/// credential while the trust store's current generation still matches this exact value -- an
/// administrative mutation that commits after this challenge began but before it is confirmed must
/// invalidate it, even though the challenge's own code, ownership, and expiry all still check out.
/// </param>
public sealed record PairingChallenge(ChallengeId Id, ClientId OwnerClientId, string Code, DateTimeOffset ExpiresAtUtc, long SecurityFenceGeneration);
