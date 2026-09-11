namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>hello_ack</c> message payload, per <c>protocol/schema/README.md</c>'s "<c>hello_ack</c>"
/// section. Host-originated only, sent once a <c>hello</c> is validated and authenticated.
/// </summary>
public sealed record HelloAckPayload
{
    /// <summary>
    /// <c>bridgeVersion</c> is a legacy wire-field name retained for protocol compatibility; it
    /// carries the DovahLink product release version, matching <c>adapter/vcpkg.json</c>'s
    /// <c>version-string</c>. The host does not evaluate a client-declared compatibility range
    /// itself.
    /// </summary>
    public required string BridgeVersion { get; init; }

    /// <summary>The trust kind the newly admitted session was assigned.</summary>
    public required ClientIdentityKind ClientIdentityKind { get; init; }
}
