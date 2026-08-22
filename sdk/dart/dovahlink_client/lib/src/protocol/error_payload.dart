import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'error_payload.g.dart';

/// Incoming `error` payload (`protocol/schema/README.md`'s `error`). Decode-only: the client
/// never sends `error`.
@JsonSerializable(checked: true, createToJson: false)
class ErrorPayload {
  /// Creates an error payload.
  const ErrorPayload({
    required this.code,
    required this.message,
    required this.retryable,
    this.details,
  });

  /// Decodes and validates one `error` payload.
  factory ErrorPayload.fromJson(JsonMap json) {
    try {
      return _$ErrorPayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid error payload: $error');
    }
  }

  /// The canonical machine-readable failure code. For branching; never [message].
  @JsonKey(required: true)
  final ProtocolErrorCode code;

  /// Diagnostic text. Never used for branching.
  @JsonKey(required: true)
  final String message;

  /// Whether a fresh connection may retry.
  @JsonKey(required: true)
  final bool retryable;

  /// Safe diagnostic details, when any exist. Unlike the other fields, this key may be absent
  /// entirely, not only `null` -- per the schema's "optional when no safe diagnostic details
  /// exist."
  final JsonMap? details;
}
