import 'package:json_annotation/json_annotation.dart';

import '../../domain/entities/protocol_envelope.entity.dart';
import 'json_map.dart';
import 'protocol_format_exception.dart';

part 'protocol_envelope.model.g.dart';

@JsonSerializable(checked: true)
/// A generated JSON adapter for [ProtocolEnvelopeEntity].
class ProtocolEnvelopeModel extends ProtocolEnvelopeEntity {
  /// Creates a decoded protocol envelope.
  const ProtocolEnvelopeModel({
    required this.protocolVersion,
    required this.messageType,
    required this.messageId,
    required this.sessionId,
    required this.correlationId,
    required this.payload,
  });

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

  /// Encodes this envelope as a JSON object.
  JsonMap toJson() => _$ProtocolEnvelopeModelToJson(this);
}

/// Reads an integer while rejecting numeric values with a fractional part.
int _readInteger(Object? value) {
  if (value is int) return value;
  throw const FormatException('value must be an integer');
}
