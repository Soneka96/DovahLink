using System.Text.Json;

namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>capabilities</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>capabilities</c>" section. Sent by both endpoints after <c>hello_ack</c>. No capability is
/// currently registered, so both directions of this exchange carry an empty list; a non-empty list
/// is rejected as <see cref="PublicProtocolErrorCode.UnsupportedCapability"/> without needing to
/// interpret any individual entry's shape, which is why entries stay undecoded
/// <see cref="JsonElement"/> values here rather than a dedicated capability-descriptor type.
/// </summary>
public sealed record CapabilitiesPayload
{
    /// <summary>The advertised capability list.</summary>
    public required IReadOnlyList<JsonElement> Capabilities { get; init; }
}
