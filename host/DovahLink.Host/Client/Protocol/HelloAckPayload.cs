namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>hello_ack</c> message payload, per <c>protocol/schema/README.md</c>'s "<c>hello_ack</c>"
/// section. Host-originated only, sent once a <c>hello</c> is validated and authenticated.
/// </summary>
public sealed record HelloAckPayload
{
    /// <summary>
    /// <c>hostVersion</c> carries the Host's own release version, the compatibility authority per
    /// <c>ai/context/protocol/compatibility.md</c>. The host does not evaluate a client-declared
    /// compatibility range itself.
    /// </summary>
    public required string HostVersion { get; init; }

    /// <summary>The trust kind the newly admitted session was assigned.</summary>
    public required ClientIdentityKind ClientIdentityKind { get; init; }
}
