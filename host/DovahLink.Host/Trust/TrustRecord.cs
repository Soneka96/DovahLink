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
public sealed record TrustRecord(
    ClientId ClientId,
    string ShortId,
    string? DisplayName,
    KnownDeviceState State,
    string CredentialVerifier,
    DateTimeOffset PairedAtUtc);
