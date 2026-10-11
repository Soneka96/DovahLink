import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'gameplay_readiness_payload.g.dart';

/// Incoming `gameplay_readiness` payload (`protocol/schema/README.md`'s `gameplay_readiness`).
/// Decode-only: the client never sends `gameplay_readiness`. Unlike other payloads, the schema
/// declares any additional field malformed, so an unrecognized key is rejected rather than ignored.
@JsonSerializable(
  checked: true,
  createToJson: false,
  disallowUnrecognizedKeys: true,
)
class GameplayReadinessPayload {
  /// Whether the Host reports gameplay as ready.
  @JsonKey(required: true)
  final bool ready;

  /// The per-session, strictly increasing publication sequence; at least `1`.
  @JsonKey(required: true)
  final int sequence;

  /// Creates a gameplay-readiness payload.
  const GameplayReadinessPayload({required this.ready, required this.sequence});

  /// Decodes and validates one `gameplay_readiness` payload. [sequence] must be a JSON integer
  /// of at least `1`; the generated decoder alone would silently truncate a fractional number.
  factory GameplayReadinessPayload.fromJson(JsonMap json) {
    try {
      final Object? rawSequence = json['sequence'];
      if (rawSequence is! int || rawSequence < 1) {
        throw const ProtocolFormatException(
          'sequence must be an integer of at least 1.',
        );
      }
      return _$GameplayReadinessPayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException(
        'Invalid gameplay_readiness payload: $error',
      );
    }
  }
}
