import 'package:json_annotation/json_annotation.dart';

import 'json_map.dart';
import 'protocol_format_exception.dart';

part 'session_invalidated_payload.g.dart';

/// Incoming `session_invalidated` payload (`protocol/schema/README.md`'s `session_invalidated`).
/// Decode-only: the client never sends `session_invalidated`.
@JsonSerializable(checked: true, createToJson: false)
class SessionInvalidatedPayload {
  /// Creates a session-invalidated payload.
  const SessionInvalidatedPayload({required this.reason});

  /// Decodes and validates one `session_invalidated` payload.
  factory SessionInvalidatedPayload.fromJson(JsonMap json) {
    try {
      return _$SessionInvalidatedPayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException(
        'Invalid session_invalidated payload: $error',
      );
    }
  }

  /// The raw wire value: `"revoked"`, `"blocked"`, `"trust_reset"`, or `"factory_reset"`.
  /// Interpreted into a typed value by the client rather than here, per
  /// `ai/context/flutter/architecture.md`'s "keep semantic validation outside generated code".
  @JsonKey(required: true)
  final String reason;
}
