namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>subscribe</c> message payload, per <c>protocol/schema/README.md</c>'s "<c>subscribe</c>"
/// section. Full-session only; no state area is currently registered, so every requested area is
/// rejected into <see cref="SubscriptionAckPayload.RejectedStateAreas"/>.
/// </summary>
public sealed record SubscribePayload
{
    /// <summary>The requested state areas.</summary>
    public required IReadOnlyList<string> StateAreas { get; init; }
}
