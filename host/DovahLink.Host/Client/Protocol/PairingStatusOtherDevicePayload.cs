namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>pairing_status</c> message payload for <see cref="PairingStatusWireState.OtherDevicePairing"/>
/// specifically, per <c>protocol/schema/README.md</c>'s "<c>pairing_status</c>" section: a different
/// client owns the active challenge or pending credential, and nothing else about it -- including a
/// remaining-seconds figure -- is disclosed. Declaring no <c>expiresInSeconds</c> member at all means
/// it is omitted from the encoded wire object entirely, distinct from <see cref="PairingStatusPayload"/>'s
/// explicit <see langword="null"/> for every other state that has no number to report.
/// </summary>
public sealed record PairingStatusOtherDevicePayload
{
    /// <summary>Always <see cref="PairingStatusWireState.OtherDevicePairing"/>.</summary>
    public required PairingStatusWireState State { get; init; }
}
