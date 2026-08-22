import 'package:json_annotation/json_annotation.dart';

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
    required this.bridgeInstanceId,
    required this.playContextId,
    required this.clientId,
  });

  /// Decodes and validates one protocol envelope.
  factory Envelope.fromJson(JsonMap json) {
    try {
      final Envelope envelope = _$EnvelopeFromJson(json);
      if (envelope.messageId.isEmpty) {
        throw const ProtocolFormatException('messageId must not be empty.');
      }
      if (envelope.correlationId?.isEmpty ?? false) {
        throw const ProtocolFormatException(
          'correlationId must be null or non-empty.',
        );
      }
      if (envelope.messageType == ProtocolMessageType.helloAck &&
          envelope.correlationId == null) {
        throw const ProtocolFormatException(
          'correlationId must be present for hello_ack.',
        );
      }
      if (envelope.messageType == ProtocolMessageType.hello &&
          envelope.correlationId != null) {
        throw const ProtocolFormatException(
          'correlationId must be null for hello.',
        );
      }
      if (envelope.messageType == ProtocolMessageType.sessionInvalidated &&
          envelope.correlationId != null) {
        throw const ProtocolFormatException(
          'correlationId must be null for session_invalidated.',
        );
      }
      final bool correlationIsRequired = switch (envelope.messageType) {
        ProtocolMessageType.helloAck ||
        ProtocolMessageType.pairingStatus ||
        ProtocolMessageType.pairingOutcome ||
        ProtocolMessageType.renameOutcome ||
        ProtocolMessageType.subscriptionAck ||
        ProtocolMessageType.stateSnapshot ||
        ProtocolMessageType.pong => true,
        _ => false,
      };
      final bool correlationMustBeNull = switch (envelope.messageType) {
        ProtocolMessageType.hello ||
        ProtocolMessageType.capabilities ||
        ProtocolMessageType.stateEvent ||
        ProtocolMessageType.sessionInvalidated ||
        ProtocolMessageType.ping ||
        ProtocolMessageType.pairingRequest ||
        ProtocolMessageType.pairingConfirm ||
        ProtocolMessageType.pairingAck ||
        ProtocolMessageType.pairingRenotify ||
        ProtocolMessageType.pairingCancel ||
        ProtocolMessageType.renameRequest ||
        ProtocolMessageType.subscribe ||
        ProtocolMessageType.snapshotRequest => true,
        _ => false,
      };
      if (correlationMustBeNull && envelope.correlationId != null) {
        throw ProtocolFormatException(
          'correlationId must be null for ${envelope.messageType}.',
        );
      }
      if (correlationIsRequired && envelope.correlationId == null) {
        throw ProtocolFormatException(
          'correlationId must be present for ${envelope.messageType}.',
        );
      }
      final String? sessionId = envelope.sessionId;
      switch (envelope.messageType) {
        case ProtocolMessageType.hello:
          if (sessionId != null) {
            throw const ProtocolFormatException(
              'sessionId must be null for hello.',
            );
          }
        case ProtocolMessageType.error:
          if (sessionId?.isEmpty ?? false) {
            throw const ProtocolFormatException(
              'sessionId must be null or non-empty for error.',
            );
          }
        default:
          if (sessionId == null || sessionId.isEmpty) {
            throw ProtocolFormatException(
              'sessionId must be non-empty for ${envelope.messageType}.',
            );
          }
      }
      if (envelope.bridgeInstanceId?.isEmpty ?? false) {
        throw const ProtocolFormatException(
          'bridgeInstanceId must be null or non-empty.',
        );
      }
      if (envelope.playContextId?.isEmpty ?? false) {
        throw const ProtocolFormatException(
          'playContextId must be null or non-empty.',
        );
      }
      if (envelope.clientId?.isEmpty ?? false) {
        throw const ProtocolFormatException(
          'clientId must be null or non-empty.',
        );
      }
      final bool clientIdIsRequired = switch (envelope.messageType) {
        ProtocolMessageType.pairingRequest ||
        ProtocolMessageType.pairingConfirm ||
        ProtocolMessageType.pairingAck ||
        ProtocolMessageType.pairingRenotify ||
        ProtocolMessageType.pairingCancel ||
        ProtocolMessageType.renameRequest ||
        ProtocolMessageType.subscribe ||
        ProtocolMessageType.snapshotRequest ||
        ProtocolMessageType.ping => true,
        _ => false,
      };
      if (envelope.messageType == ProtocolMessageType.helloAck &&
          (envelope.clientId == null || envelope.clientId!.isEmpty)) {
        throw const ProtocolFormatException(
          'clientId must be non-empty for hello_ack.',
        );
      }
      if (clientIdIsRequired && envelope.clientId == null) {
        throw ProtocolFormatException(
          'clientId must be non-empty for ${envelope.messageType}.',
        );
      }
      if (envelope.messageType != ProtocolMessageType.capabilities &&
          !clientIdIsRequired &&
          envelope.messageType != ProtocolMessageType.helloAck &&
          envelope.clientId != null) {
        throw ProtocolFormatException(
          'clientId must be null for ${envelope.messageType}.',
        );
      }
      if (envelope.messageType == ProtocolMessageType.hello &&
          (envelope.bridgeInstanceId != null ||
              envelope.playContextId != null ||
              envelope.clientId != null)) {
        throw const ProtocolFormatException(
          'hello identity fields must be null.',
        );
      }
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
