using System.Text.Json;

namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The common envelope every public protocol message carries, per
/// <c>protocol/schema/README.md</c>'s "Common envelope". <see cref="Payload"/> stays an undecoded
/// <see cref="JsonElement"/>: its shape depends on <see cref="MessageType"/>, so a caller decodes it
/// further through <see cref="IPublicEnvelopeCodec.TryDecodePayload{TPayload}"/> once it knows which
/// message type it is handling.
/// </summary>
/// <param name="MessageType">The canonical registered message identifier.</param>
/// <param name="MessageId">Cryptographically random and unique within the connection session.</param>
/// <param name="SessionId">The server-issued session identity, or <see langword="null"/> for a pre-authentication <c>hello</c> or a pre-session <c>error</c>.</param>
/// <param name="CorrelationId">The message ID this message answers, or <see langword="null"/> when there is no correlation.</param>
/// <param name="Payload">The message-specific data, decoded no further than a well-formed, bounded JSON object.</param>
/// <param name="BridgeInstanceId">Identifies the running host process. Explicitly unavailable (always <see langword="null"/>) for this transition boundary; see D1.</param>
/// <param name="PlayContextId">Identifies the currently loaded play context, or <see langword="null"/> outside an active play context.</param>
/// <param name="ClientId">The logical client identity, or <see langword="null"/> on the client's own <c>hello</c> or on any host-originated message after <c>hello_ack</c>.</param>
public sealed record PublicEnvelope(
    PublicMessageType MessageType,
    string MessageId,
    string? SessionId,
    string? CorrelationId,
    JsonElement Payload,
    string? BridgeInstanceId,
    string? PlayContextId,
    string? ClientId);
