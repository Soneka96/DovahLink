import 'package:json_annotation/json_annotation.dart';

import 'json_map.dart';
import 'protocol_format_exception.dart';

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
    required this.bridgeInstanceId,
    required this.playContextId,
    required this.clientId,
  });

  /// Decodes and validates one protocol envelope.
  factory Envelope.fromJson(JsonMap json) {
    try {
      return _$EnvelopeFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid protocol envelope: $error');
    }
  }

  /// The canonical message type.
  @JsonKey(required: true)
  final String messageType;

  /// The unique message identifier for the session.
  @JsonKey(required: true)
  final String messageId;

  /// The server-issued session identifier, when available.
  @JsonKey(required: true)
  final String? sessionId;

  /// The identifier of the message being answered, when correlated.
  @JsonKey(required: true)
  final String? correlationId;

  /// The message-specific payload.
  @JsonKey(required: true)
  final JsonMap payload;

  /// The identity of the bridge instance that produced this message. `null`
  /// on the client's own `hello` and on a narrow set of early
  /// connection-hygiene rejections; present otherwise.
  @JsonKey(required: true)
  final String? bridgeInstanceId;

  /// The identity of the currently loaded play context, when one is active.
  @JsonKey(required: true)
  final String? playContextId;

  /// The identity of the logical client, established at `hello`. `null`
  /// before `hello` completes and on every message the Bridge sends after
  /// `hello_ack`.
  @JsonKey(required: true)
  final String? clientId;

  /// Encodes this envelope as a JSON object.
  JsonMap toJson() => _$EnvelopeToJson(this);
}
