namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>session_invalidated</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>session_invalidated</c>" section. Host-originated only, unsolicited, sent best-effort before
/// the owning socket is force-closed.
/// </summary>
public sealed record SessionInvalidatedPayload
{
    /// <summary>The authoritative reason this session was invalidated.</summary>
    public required SessionInvalidationReason Reason { get; init; }
}
