namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// One entry of a <c>capabilities</c> advertisement's list, per <c>protocol/schema/README.md</c>'s
/// "<c>capabilities</c>" section: "Each capability requires <c>id</c> and <c>version</c>." Decoding a
/// capability entry into this typed shape -- rather than an undecoded <see cref="System.Text.Json.JsonElement"/> --
/// lets the host distinguish a structurally invalid descriptor (<see cref="PublicProtocolErrorCode.MalformedMessage"/>)
/// from a structurally valid one the host does not currently support (<see cref="PublicProtocolErrorCode.UnsupportedCapability"/>),
/// even though no capability ID or version is currently registered.
/// </summary>
public sealed record CapabilityDescriptor
{
    /// <summary>The canonical capability identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The capability's version, independent of the Bridge/Host release version.</summary>
    public required string Version { get; init; }
}
