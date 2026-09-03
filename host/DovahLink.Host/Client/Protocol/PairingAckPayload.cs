namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>pairing_ack</c> message payload, per <c>protocol/schema/README.md</c>'s "<c>pairing_ack</c>"
/// section. Restricted-session only: the client's final confirmation, echoing back the credential it
/// durably saved before this message was sent.
/// </summary>
public sealed record PairingAckPayload
{
    /// <summary>The hex-encoded credential the client received in a prior <c>credential_issued</c> outcome.</summary>
    public required string Credential { get; init; }
}
