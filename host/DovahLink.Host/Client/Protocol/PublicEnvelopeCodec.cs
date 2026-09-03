using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// Decodes and encodes the public protocol's common envelope, per
/// <c>protocol/schema/README.md</c>'s "Common envelope" and <c>ai/context/protocol/security.md</c>'s
/// "Input limits". Enforces the approved bounded JSON limits -- maximum decoded nesting depth,
/// string length, array length, and object member count -- before any typed DTO is materialized, and
/// validates every required envelope field's presence and type. Stateless and safe to share across
/// every connection.
/// </summary>
public interface IPublicEnvelopeCodec
{
    /// <summary>
    /// Attempts to decode one complete inbound message into its common envelope. Enforces every
    /// approved JSON bound and every required envelope field's presence and type before returning;
    /// <paramref name="envelope"/>'s <see cref="PublicEnvelope.Payload"/> is a validated, bounded
    /// JSON object decoded no further -- a caller decodes it into a message-specific type through
    /// <see cref="TryDecodePayload{TPayload}"/> once it knows the message type.
    /// </summary>
    /// <param name="payload">The complete UTF-8 message bytes to decode.</param>
    /// <param name="envelope">The decoded envelope on success; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="payload"/> is well-formed, within every bound, and
    /// carries every required envelope field with the correct type and a recognized message type.
    /// </returns>
    bool TryDecode(ReadOnlyMemory<byte> payload, [NotNullWhen(true)] out PublicEnvelope? envelope);

    /// <summary>
    /// Attempts to decode an already-validated envelope's <see cref="PublicEnvelope.Payload"/> into a
    /// message-specific payload type. Only ever called after <see cref="TryDecode"/> has already
    /// enforced every bound on the complete document <paramref name="envelope"/> came from, so this
    /// performs no bound re-validation of its own -- only required-field presence and type, matching
    /// <typeparamref name="TPayload"/>'s own required members.
    /// </summary>
    /// <typeparam name="TPayload">The message-specific payload type to decode into.</typeparam>
    /// <param name="envelope">The already-decoded envelope whose payload to interpret.</param>
    /// <param name="payload">The decoded payload on success; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the payload matches <typeparamref name="TPayload"/>'s required shape.</returns>
    bool TryDecodePayload<TPayload>(PublicEnvelope envelope, [NotNullWhen(true)] out TPayload? payload) where TPayload : class;

    /// <summary>
    /// Encodes a host-originated message into its complete wire envelope.
    /// <see cref="PublicEnvelope.BridgeInstanceId"/> is always encoded as <see langword="null"/>, per
    /// the approved D1 transition-boundary limitation -- callers cannot override it.
    /// </summary>
    /// <typeparam name="TPayload">The message-specific payload type being encoded.</typeparam>
    /// <param name="messageType">The canonical message type.</param>
    /// <param name="messageId">A fresh, cryptographically random message identifier, unique within the socket session.</param>
    /// <param name="sessionId">The server-issued session identity, or <see langword="null"/> when none exists yet.</param>
    /// <param name="correlationId">The message ID this message answers, or <see langword="null"/> when there is no correlation.</param>
    /// <param name="playContextId">The current play context identity, or <see langword="null"/> outside an active play context.</param>
    /// <param name="clientId">The authenticated client identity, present only where the schema explicitly requires it on a host-originated message.</param>
    /// <param name="payload">The message-specific payload to encode.</param>
    /// <returns>The complete UTF-8 encoded message bytes.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="payload"/> did not serialize to a JSON object, violating the canonical
    /// schema's <c>payload: object</c> requirement that <see cref="TryDecode"/> enforces on decode.
    /// </exception>
    byte[] Encode<TPayload>(
        PublicMessageType messageType,
        string messageId,
        string? sessionId,
        string? correlationId,
        string? playContextId,
        string? clientId,
        TPayload payload);
}

/// <inheritdoc cref="IPublicEnvelopeCodec"/>
public sealed class PublicEnvelopeCodec : IPublicEnvelopeCodec
{
    /// <summary>The bounded document-parse options every decode enforces before materializing any typed value.</summary>
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = Constants.PublicProtocolMaxJsonDepth,
    };

    /// <summary>
    /// The serializer options used for message-specific payload (de)serialization: camelCase field
    /// names matching the canonical schema, snake_case wire values for every protocol enum, and strict
    /// wire validation of every required field. <see cref="JsonSerializerOptions.RespectNullableAnnotations"/>
    /// makes a schema-required reference/collection member's C# nullable-reference annotation (every
    /// payload type in this project enables nullable reference types) into real wire validation: the
    /// C# <c>required</c> keyword alone only checks that a property was present in the document, not
    /// that its value was not JSON <c>null</c>, so <c>{"auth": null}</c> would otherwise satisfy
    /// <see cref="HelloPayload.Auth"/>'s presence check while still producing a null reference a
    /// handler later dereferences. With this option, deserializing an explicit <c>null</c> into any
    /// non-nullable member throws <see cref="JsonException"/>, which <see cref="TryDecodePayload{TPayload}"/>
    /// already turns into a decode failure.
    /// <see cref="JsonSerializerOptions.UnmappedMemberHandling"/> rejects an unrecognized nested
    /// property the same way: <c>protocol/schema/README.md</c> documents forward-compatible extension
    /// only for the common envelope's own top-level fields (handled leniently by <see cref="TryDecode"/>'s
    /// own <c>JsonElement.TryGetProperty</c>-based reads below, which this option does not affect), not
    /// for any nested payload object -- so every message-specific payload type sharing these options is
    /// strict about its own members.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <inheritdoc/>
    public bool TryDecode(ReadOnlyMemory<byte> payload, [NotNullWhen(true)] out PublicEnvelope? envelope)
    {
        envelope = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload, DocumentOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !IsWithinBounds(root))
            {
                return false;
            }

            if (!root.TryGetProperty("messageType", out JsonElement messageTypeElement) ||
                messageTypeElement.ValueKind != JsonValueKind.String ||
                !TryParseMessageType(messageTypeElement.GetString(), out PublicMessageType messageType))
            {
                return false;
            }

            if (!TryGetRequiredString(root, "messageId", out string? messageId) ||
                !TryGetOptionalString(root, "sessionId", out string? sessionId) ||
                !TryGetOptionalString(root, "correlationId", out string? correlationId) ||
                !TryGetOptionalString(root, "bridgeInstanceId", out string? bridgeInstanceId) ||
                !TryGetOptionalString(root, "playContextId", out string? playContextId) ||
                !TryGetOptionalString(root, "clientId", out string? clientId))
            {
                return false;
            }

            if (!root.TryGetProperty("payload", out JsonElement payloadElement) || payloadElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            envelope = new PublicEnvelope(
                messageType, messageId!, sessionId, correlationId, payloadElement.Clone(), bridgeInstanceId, playContextId, clientId);
            return true;
        }
    }

    /// <inheritdoc/>
    public bool TryDecodePayload<TPayload>(PublicEnvelope envelope, [NotNullWhen(true)] out TPayload? payload) where TPayload : class
    {
        try
        {
            payload = envelope.Payload.Deserialize<TPayload>(PayloadSerializerOptions);
            return payload is not null;
        }
        catch (JsonException)
        {
            payload = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public byte[] Encode<TPayload>(
        PublicMessageType messageType,
        string messageId,
        string? sessionId,
        string? correlationId,
        string? playContextId,
        string? clientId,
        TPayload payload)
    {
        JsonNode? payloadNode = JsonSerializer.SerializeToNode(payload, PayloadSerializerOptions);
        if (payloadNode is not JsonObject payloadObject)
        {
            // The canonical schema requires payload: object, and TryDecode rejects anything else on
            // decode; failing here instead keeps the host from ever emitting a wire message its own
            // decoder -- or a conforming client -- would reject.
            throw new ArgumentException(
                $"{typeof(TPayload)} must serialize to a JSON object; it serialized to {(payloadNode is null ? "null" : payloadNode.GetType().Name)}.",
                nameof(payload));
        }

        var envelope = new JsonObject
        {
            ["messageType"] = FormatMessageType(messageType),
            ["messageId"] = messageId,
            ["sessionId"] = sessionId,
            ["correlationId"] = correlationId,
            ["payload"] = payloadObject,
            ["bridgeInstanceId"] = null,
            ["playContextId"] = playContextId,
            ["clientId"] = clientId,
        };

        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

    /// <summary>
    /// Recursively enforces the approved array-length, object-member-count, and string-length bounds,
    /// and rejects a duplicate property name within any one object, over an already depth-bounded
    /// parsed document. Nesting depth itself needs no separate check here: <see cref="DocumentOptions"/>
    /// already rejected anything deeper than <see cref="Constants.PublicProtocolMaxJsonDepth"/> during
    /// parsing, so this walk can never recurse past that same bound. Duplicate-name rejection applies
    /// uniformly to every object in the document -- the common envelope's own top-level object and every
    /// nested payload object alike -- rather than special-casing only security-sensitive fields: <see
    /// cref="JsonElement.EnumerateObject"/> yields every occurrence in document order, including
    /// duplicates, since <see cref="System.Text.Json.JsonDocument"/> itself does not deduplicate them
    /// during parsing; leaving a security-sensitive field's effective value dependent on which
    /// occurrence a later consumer happens to read back is rejected outright instead.
    /// </summary>
    private static bool IsWithinBounds(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                int memberCount = 0;
                HashSet<string> seenPropertyNames = [];
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    memberCount++;
                    if (memberCount > Constants.PublicProtocolMaxJsonObjectMembers ||
                        !IsStringWithinBounds(property.Name) || !seenPropertyNames.Add(property.Name) ||
                        !IsWithinBounds(property.Value))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.Array:
                int elementCount = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    elementCount++;
                    if (elementCount > Constants.PublicProtocolMaxJsonArrayLength || !IsWithinBounds(item))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.String:
                return IsStringWithinBounds(element.GetString()!);

            default:
                return true;
        }
    }

    /// <summary>Reports whether a decoded string's UTF-8 byte length stays within the approved bound.</summary>
    private static bool IsStringWithinBounds(string value) =>
        Encoding.UTF8.GetByteCount(value) <= Constants.PublicProtocolMaxJsonStringLengthBytes;

    /// <summary>Reads a required, non-null string property.</summary>
    private static bool TryGetRequiredString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value is not null;
    }

    /// <summary>Reads a required property whose value is a string or explicit JSON <c>null</c>.</summary>
    private static bool TryGetOptionalString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return true;
    }

    /// <summary>Maps a wire <c>messageType</c> string to its canonical enum value.</summary>
    private static bool TryParseMessageType(string? wireValue, out PublicMessageType messageType)
    {
        switch (wireValue)
        {
            case "hello": messageType = PublicMessageType.Hello; return true;
            case "hello_ack": messageType = PublicMessageType.HelloAck; return true;
            case "pairing_request": messageType = PublicMessageType.PairingRequest; return true;
            case "pairing_status": messageType = PublicMessageType.PairingStatus; return true;
            case "pairing_confirm": messageType = PublicMessageType.PairingConfirm; return true;
            case "pairing_ack": messageType = PublicMessageType.PairingAck; return true;
            case "pairing_renotify": messageType = PublicMessageType.PairingRenotify; return true;
            case "pairing_cancel": messageType = PublicMessageType.PairingCancel; return true;
            case "pairing_outcome": messageType = PublicMessageType.PairingOutcome; return true;
            case "rename_request": messageType = PublicMessageType.RenameRequest; return true;
            case "rename_outcome": messageType = PublicMessageType.RenameOutcome; return true;
            case "capabilities": messageType = PublicMessageType.Capabilities; return true;
            case "subscribe": messageType = PublicMessageType.Subscribe; return true;
            case "subscription_ack": messageType = PublicMessageType.SubscriptionAck; return true;
            case "snapshot_request": messageType = PublicMessageType.SnapshotRequest; return true;
            case "state_snapshot": messageType = PublicMessageType.StateSnapshot; return true;
            case "state_event": messageType = PublicMessageType.StateEvent; return true;
            case "error": messageType = PublicMessageType.Error; return true;
            case "session_invalidated": messageType = PublicMessageType.SessionInvalidated; return true;
            case "ping": messageType = PublicMessageType.Ping; return true;
            case "pong": messageType = PublicMessageType.Pong; return true;
            default: messageType = default; return false;
        }
    }

    /// <summary>Maps a canonical message type to its wire <c>messageType</c> string.</summary>
    private static string FormatMessageType(PublicMessageType messageType) => messageType switch
    {
        PublicMessageType.Hello => "hello",
        PublicMessageType.HelloAck => "hello_ack",
        PublicMessageType.PairingRequest => "pairing_request",
        PublicMessageType.PairingStatus => "pairing_status",
        PublicMessageType.PairingConfirm => "pairing_confirm",
        PublicMessageType.PairingAck => "pairing_ack",
        PublicMessageType.PairingRenotify => "pairing_renotify",
        PublicMessageType.PairingCancel => "pairing_cancel",
        PublicMessageType.PairingOutcome => "pairing_outcome",
        PublicMessageType.RenameRequest => "rename_request",
        PublicMessageType.RenameOutcome => "rename_outcome",
        PublicMessageType.Capabilities => "capabilities",
        PublicMessageType.Subscribe => "subscribe",
        PublicMessageType.SubscriptionAck => "subscription_ack",
        PublicMessageType.SnapshotRequest => "snapshot_request",
        PublicMessageType.StateSnapshot => "state_snapshot",
        PublicMessageType.StateEvent => "state_event",
        PublicMessageType.Error => "error",
        PublicMessageType.SessionInvalidated => "session_invalidated",
        PublicMessageType.Ping => "ping",
        PublicMessageType.Pong => "pong",
        _ => throw new ArgumentOutOfRangeException(nameof(messageType)),
    };
}
