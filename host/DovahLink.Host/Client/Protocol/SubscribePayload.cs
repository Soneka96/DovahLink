namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>subscribe</c> message payload, per <c>protocol/schema/README.md</c>'s "<c>subscribe</c>"
/// section. Full-session only; a requested area is accepted only when it is currently registered,
/// otherwise it is rejected into <see cref="SubscriptionAckPayload.RejectedStateAreas"/>.
/// </summary>
public sealed record SubscribePayload
{
    /// <summary>The requested state areas.</summary>
    public required IReadOnlyList<string> StateAreas { get; init; }
}
