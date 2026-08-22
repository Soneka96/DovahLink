import 'package:json_annotation/json_annotation.dart';

import '../shared/enums.dart';
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

  /// The wire vocabulary of `session_invalidated.reason`.
  @JsonKey(required: true)
  final AdministrativeInvalidationReason reason;
}
