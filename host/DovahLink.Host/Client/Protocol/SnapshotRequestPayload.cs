namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>snapshot_request</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>snapshot_request</c>" section. Full-session only; no state area is currently registered, so
/// every request is rejected as <see cref="PublicProtocolErrorCode.UnsupportedCapability"/>.
/// </summary>
public sealed record SnapshotRequestPayload
{
    /// <summary>The state area a fresh baseline is requested for.</summary>
    public required string StateArea { get; init; }

    /// <summary>The client's last known revision for <see cref="StateArea"/>. Optional and advisory only.</summary>
    public long? KnownRevision { get; init; }
}
