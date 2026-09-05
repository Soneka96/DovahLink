using DovahLink.Host.Identity;

namespace DovahLink.Host.Trust;

/// <summary>
/// A persisted record of one device's trust relationship with the host. Never stores the raw
/// pairing credential; only a verifier the host can check a presented credential against.
/// </summary>
/// <param name="ClientId">The device's persistent client identity.</param>
/// <param name="ShortId">The short, human-legible identifier shown alongside <paramref name="DisplayName"/>.</param>
/// <param name="DisplayName">The optional user-assigned display name for the device.</param>
/// <param name="State">The device's current trust state.</param>
/// <param name="CredentialVerifier">A hex-encoded, one-way verifier for the credential this device holds, never the credential itself.</param>
/// <param name="PairedAtUtc">The UTC time the trust relationship was first established.</param>
/// <param name="BlockedAtUtc">The time the device was blocked, or <see langword="null"/> when it is not blocked.</param>
public sealed record TrustRecord(
    ClientId ClientId,
    string ShortId,
    string? DisplayName,
    KnownDeviceState State,
    string CredentialVerifier,
    DateTimeOffset PairedAtUtc,
    DateTimeOffset? BlockedAtUtc = null)
{
    /// <summary>
    /// This specific Known Device incarnation's identity. Preserved unchanged across every ordinary
    /// state transition of the same Known Device (Trusted &#8594; Revoked, Trusted/Revoked &#8594;
    /// Blocked, Blocked &#8594; Unpaired, a rename, and a revoked or unpaired device re-pairing back to
    /// Trusted); replaced only when this exact record is destroyed (Forget, Factory Reset) and a later
    /// pairing for the same <see cref="ClientId"/> creates a new record. See
    /// <see cref="KnownDeviceIncarnationId"/>'s own remarks for why this must never be derived from
    /// <see cref="ShortId"/>.
    /// </summary>
    public KnownDeviceIncarnationId Incarnation { get; init; }
}
