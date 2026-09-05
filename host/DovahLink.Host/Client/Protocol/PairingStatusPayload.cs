namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>pairing_status</c> message payload for every <see cref="PairingStatusWireState"/> value
/// except <see cref="PairingStatusWireState.OtherDevicePairing"/>, per
/// <c>protocol/schema/README.md</c>'s "<c>pairing_status</c>" section. Host-originated reply to
/// <c>pairing_request</c>. <see cref="PairingStatusWireState.OtherDevicePairing"/> uses
/// <see cref="PairingStatusOtherDevicePayload"/> instead, since it omits
/// <see cref="ExpiresInSeconds"/> entirely rather than carrying it as <see langword="null"/>.
/// </summary>
public sealed record PairingStatusPayload
{
    /// <summary>The reported pairing availability.</summary>
    public required PairingStatusWireState State { get; init; }

    /// <summary>
    /// The active challenge's remaining code validity in whole seconds, rounded up; a number for
    /// <see cref="PairingStatusWireState.Available"/> and for <see cref="PairingStatusWireState.InProgress"/>
    /// while the requesting client's own code is still counting down, or <see langword="null"/> for
    /// <see cref="PairingStatusWireState.Unavailable"/> and for <see cref="PairingStatusWireState.InProgress"/>
    /// while the requesting client owns only a pending credential with no code left to redisplay.
    /// </summary>
    public required int? ExpiresInSeconds { get; init; }
}
