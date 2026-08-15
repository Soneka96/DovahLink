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
    required this.protocolVersion,
    required this.messageType,
    required this.messageId,
    required this.sessionId,
    required this.correlationId,
    required this.payload,
    this.bridgeInstanceId,
    this.playContextId,
    this.clientId,
  }) : super(
         protocolVersion: protocolVersion,
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
    final ProtocolEnvelopeModel model;
    try {
      model = _$ProtocolEnvelopeModelFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid protocol envelope: $error');
    }
    if (model.protocolVersion < 0) {
      throw ProtocolFormatException('protocolVersion must be non-negative');
    }
    return model;
  }

  /// The negotiated protocol version for this message.
  @JsonKey(required: true, fromJson: _readInteger)
  @override
  final int protocolVersion;

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
  /// Decoded tolerantly regardless of [protocolVersion]: an absent key and
  /// an explicit JSON `null` both decode to `null` here. Only [toJson]
  /// enforces which version may write this key.
  @JsonKey()
  @override
  final String? bridgeInstanceId;

  /// The identity of the currently loaded play context, when one is active.
  /// See [bridgeInstanceId] for the same absent-vs-null decode tolerance.
  @JsonKey()
  @override
  final String? playContextId;

  /// The identity of the logical client, established at `hello`. See
  /// [bridgeInstanceId] for the same absent-vs-null decode tolerance.
  @JsonKey()
  @override
  final String? clientId;

  /// Encodes this envelope as a JSON object.
  ///
  /// Version-gated per `protocol/schema/README.md`'s v2 encoding rule: below
  /// protocol version 2, [bridgeInstanceId], [playContextId], and [clientId]
  /// are omitted entirely rather than encoded as `null`, since
  /// `json_serializable`'s generated output cannot conditionally omit a key
  /// based on a sibling field.
  JsonMap toJson() {
    final JsonMap json = _$ProtocolEnvelopeModelToJson(this);
    if (protocolVersion < 2) {
      json
        ..remove('bridgeInstanceId')
        ..remove('playContextId')
        ..remove('clientId');
    }
    return json;
  }
}

/// Reads an integer while rejecting numeric values with a fractional part.
int _readInteger(Object? value) {
  if (value is int) return value;
  throw const FormatException('value must be an integer');
}
