// ignore_for_file: overridden_fields

import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client/features/connection/data/models/json_map.dart';
import 'package:dovahlink_client/features/connection/data/models/protocol_format_exception.dart';
import 'package:dovahlink_client/features/connection/domain/entities/protocol_envelope.entity.dart';

part 'protocol_envelope.model.g.dart';

/// A generated JSON adapter for [ProtocolEnvelopeEntity].
@JsonSerializable(checked: true)
class ProtocolEnvelopeModel extends ProtocolEnvelopeEntity {
  /// Creates a decoded protocol envelope.
  const ProtocolEnvelopeModel({
    required this.messageType,
    required this.messageId,
    required this.sessionId,
    required this.correlationId,
    required this.payload,
    required this.bridgeInstanceId,
    required this.playContextId,
    required this.clientId,
  }) : super(
         messageType: messageType,
         messageId: messageId,
         sessionId: sessionId,
         correlationId: correlationId,
         payload: payload,
         bridgeInstanceId: bridgeInstanceId,
         playContextId: playContextId,
         clientId: clientId,
       );

  /// Decodes and validates one protocol envelope.
  factory ProtocolEnvelopeModel.fromJson(JsonMap json) {
    try {
      return _$ProtocolEnvelopeModelFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid protocol envelope: $error');
    }
  }

  /// The canonical message type.
  @JsonKey(required: true)
  @override
  final String messageType;

  /// The unique message identifier for the session.
  @JsonKey(required: true)
  @override
  final String messageId;

  /// The server-issued session identifier, when available.
  @JsonKey(required: true)
  @override
  final String? sessionId;

  /// The identifier of the message being answered, when correlated.
  @JsonKey(required: true)
  @override
  final String? correlationId;

  /// The message-specific payload.
  @JsonKey(required: true)
  @override
  final JsonMap payload;

  /// The identity of the bridge instance that produced this message.
  /// Required key, nullable value: absent (not merely null) is rejected,
  /// matching protocol/schema/README.md's envelope table.
  @JsonKey(required: true)
  @override
  final String? bridgeInstanceId;

  /// The identity of the currently loaded play context, when one is active.
  /// See [bridgeInstanceId] for the same required-key, nullable-value shape.
  @JsonKey(required: true)
  @override
  final String? playContextId;

  /// The identity of the logical client, established at `hello`. See
  /// [bridgeInstanceId] for the same required-key, nullable-value shape.
  @JsonKey(required: true)
  @override
  final String? clientId;

  /// Encodes this envelope as a JSON object.
  JsonMap toJson() => _$ProtocolEnvelopeModelToJson(this);
}
