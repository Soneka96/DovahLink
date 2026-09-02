namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The nested <c>auth</c> object within <see cref="HelloPayload"/>, per
/// <c>protocol/schema/README.md</c>'s "<c>hello</c>" section.
/// </summary>
public sealed record HelloAuthPayload
{
    /// <summary>
    /// The authentication method presented. <see cref="HelloAuthMethod.Unpaired"/> carries no
    /// <see cref="Token"/> at all; the other two methods require one.
    /// </summary>
    public required HelloAuthMethod Method { get; init; }

    /// <summary>
    /// The presented credential: the process-lifetime one-time token for
    /// <see cref="HelloAuthMethod.OneTimeLocalToken"/>, or the hex-encoded persisted pairing
    /// credential for <see cref="HelloAuthMethod.TrustedDeviceCredential"/>. Absent for
    /// <see cref="HelloAuthMethod.Unpaired"/>.
    /// </summary>
    public string? Token { get; init; }
}
