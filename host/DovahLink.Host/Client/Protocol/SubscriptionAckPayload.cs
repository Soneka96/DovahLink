namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>subscription_ack</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>subscription_ack</c>" section. Host-originated reply to <c>subscribe</c>. No state area is
/// currently registered, so <see cref="AcceptedStateAreas"/> is always empty and every requested area
/// appears in <see cref="RejectedStateAreas"/>.
/// </summary>
public sealed record SubscriptionAckPayload
{
    /// <summary>The requested areas the host will publish snapshots and events for.</summary>
    public required IReadOnlyList<string> AcceptedStateAreas { get; init; }

    /// <summary>The requested areas the host rejected.</summary>
    public required IReadOnlyList<string> RejectedStateAreas { get; init; }
}
