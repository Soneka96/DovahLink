namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>subscription_ack</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>subscription_ack</c>" section. Host-originated reply to <c>subscribe</c>. A requested area
/// appears in <see cref="AcceptedStateAreas"/> only when it is currently registered; every other
/// requested area appears in <see cref="RejectedStateAreas"/> instead.
/// </summary>
public sealed record SubscriptionAckPayload
{
    /// <summary>The requested areas the host will publish snapshots and events for.</summary>
    public required IReadOnlyList<string> AcceptedStateAreas { get; init; }

    /// <summary>The requested areas the host rejected.</summary>
    public required IReadOnlyList<string> RejectedStateAreas { get; init; }
}
