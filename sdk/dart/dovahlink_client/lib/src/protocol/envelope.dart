import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/envelope_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'envelope.g.dart';

/// One decoded DovahLink protocol envelope -- the common wrapper every wire message shares
/// (`protocol/schema/README.md`'s "Common envelope"). Deliberately self-contained to this SDK
/// rather than shared with `app/lib/features/connection/data/models/protocol_envelope.model.dart`:
/// that feature is expected to depend on this SDK once it is wired in, not the reverse.
@JsonSerializable(checked: true)
class Envelope {
  /// Creates a decoded protocol envelope.
  const Envelope({
    required this.messageType,
    required this.messageId,
    required this.sessionId,
    required this.correlationId,
    required this.payload,
    required this.stateAuthorityId,
    required this.playContextId,
    required this.clientId,
  });

  /// Decodes and validates one protocol envelope.
  factory Envelope.fromJson(JsonMap json) {
    try {
      final Envelope envelope = _$EnvelopeFromJson(json);
      EnvelopeValidator.validate(
        messageType: envelope.messageType,
        messageId: envelope.messageId,
        sessionId: envelope.sessionId,
        correlationId: envelope.correlationId,
        stateAuthorityId: envelope.stateAuthorityId,
        playContextId: envelope.playContextId,
        clientId: envelope.clientId,
      );
      return envelope;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid protocol envelope: $error');
    }
  }

  /// The canonical message type.
  @JsonKey(required: true)
  final ProtocolMessageType messageType;

  /// The unique message identifier for the session.
  @JsonKey(required: true)
  final String messageId;

  /// The server-issued session identifier. It is `null` only for `hello` and pre-session `error`
  /// messages; every other message requires a non-empty value.
  @JsonKey(required: true)
  final String? sessionId;

  /// The identifier of the message being answered, when correlated.
  @JsonKey(required: true)
  final String? correlationId;

  /// The message-specific payload.
  @JsonKey(required: true)
  final JsonMap payload;

  /// Identifies the Host's current authoritative-state continuity epoch. Required and non-null on
  /// `hello_ack`, `state_snapshot`, and `state_event`; `null` (and, per `includeIfNull: false`,
  /// genuinely absent from this SDK's own outgoing envelopes) on every other message, including
  /// every client-originated one.
  @JsonKey(includeIfNull: false)
  final String? stateAuthorityId;

  /// The identity of the currently loaded play context, when one is active.
  @JsonKey(required: true)
  final String? playContextId;

  /// The identity of the logical client, established at `hello`. `null`
  /// before `hello` completes and on every message the Host sends after
  /// `hello_ack`.
  @JsonKey(required: true)
  final String? clientId;

  /// Encodes this envelope as a JSON object.
  JsonMap toJson() => _$EnvelopeToJson(this);
}
