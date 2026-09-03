namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The empty <c>{}</c> payload shared by every message that carries no payload fields:
/// <c>pairing_request</c>, <c>pairing_renotify</c>, <c>pairing_cancel</c>, <c>ping</c>, and
/// <c>pong</c>. Declaring no members means <see cref="IPublicEnvelopeCodec.TryDecodePayload{TPayload}"/>'s
/// strict <c>UnmappedMemberHandling.Disallow</c> rejects any unexpected field as malformed, matching
/// each of these messages' own "no payload fields" contract.
/// </summary>
public sealed record EmptyPayload;
