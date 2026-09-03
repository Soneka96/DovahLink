namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>pairing_outcome</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>pairing_outcome</c>" section. Host-originated shared reply to <c>pairing_confirm</c>,
/// <c>pairing_ack</c>, <c>pairing_renotify</c>, and <c>pairing_cancel</c>.
/// </summary>
public sealed record PairingOutcomePayload
{
    /// <summary>The specific result, whose meaning depends on which message this replies to.</summary>
    public required PairingOutcomeWireValue Outcome { get; init; }

    /// <summary>The issued or trusted credential, present only for <c>credential_issued</c>, <c>trusted</c>, and <c>already_trusted</c>.</summary>
    public string? Credential { get; init; }

    /// <summary>The administration-only short identifier, present only for <c>trusted</c> and <c>already_trusted</c>.</summary>
    public string? ShortId { get; init; }

    /// <summary>The client-supplied presentation label, present only alongside <see cref="Credential"/>/<see cref="ShortId"/> when the client supplied one.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The minimum safe number of whole seconds to wait before retrying, rounded up. Present only for
    /// <c>pacing_limited</c> and <c>renotify_cooldown</c>.
    /// </summary>
    public int? RetryAfterSeconds { get; init; }
}
