namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>capabilities</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>capabilities</c>" section. Sent by both endpoints after <c>hello_ack</c>. No capability is
/// currently registered, so both directions of this exchange carry an empty list; a non-empty list
/// of structurally valid <see cref="CapabilityDescriptor"/> entries is rejected as
/// <see cref="PublicProtocolErrorCode.UnsupportedCapability"/>, while a structurally invalid entry
/// fails typed decoding and is rejected as <see cref="PublicProtocolErrorCode.MalformedMessage"/>
/// before that unsupported-capability classification is ever reached.
/// </summary>
public sealed record CapabilitiesPayload
{
    /// <summary>The advertised capability list.</summary>
    public required IReadOnlyList<CapabilityDescriptor> Capabilities { get; init; }
}
