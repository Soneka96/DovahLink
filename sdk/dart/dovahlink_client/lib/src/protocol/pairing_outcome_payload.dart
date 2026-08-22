import 'package:json_annotation/json_annotation.dart';

import '../shared/enums.dart';
import 'json_map.dart';
import 'protocol_format_exception.dart';

part 'pairing_outcome_payload.g.dart';

/// Incoming `pairing_outcome` payload (`protocol/schema/README.md`'s `pairing_outcome`).
/// Decode-only: the client never sends `pairing_outcome`.
@JsonSerializable(checked: true, createToJson: false)
class PairingOutcomePayload {
  /// Creates a pairing-outcome payload.
  const PairingOutcomePayload({
    required this.outcome,
    required this.credential,
    required this.shortId,
    required this.displayName,
    this.retryAfterSeconds,
  });

  /// Decodes and validates one `pairing_outcome` payload.
  factory PairingOutcomePayload.fromJson(JsonMap json) {
    try {
      return _$PairingOutcomePayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid pairing_outcome payload: $error');
    }
  }

  /// The wire vocabulary of `pairing_outcome.outcome`, spanning every exchange it can reply to.
  @JsonKey(required: true)
  final PairingOutcome outcome;

  /// Present only for `"credential_issued"`, `"trusted"`, and `"already_trusted"`.
  @JsonKey(required: true)
  final String? credential;

  /// The administration-only identifier, present only for `"trusted"` and `"already_trusted"`.
  @JsonKey(required: true)
  final String? shortId;

  /// Echoes the client-supplied label, present only alongside [credential]/[shortId] when the
  /// client supplied one.
  @JsonKey(required: true)
  final String? displayName;

  /// The remaining wait in seconds before the next attempt is accepted, present for
  /// `"pacing_limited"` (next `pairing_confirm`) and `"renotify_cooldown"` (next
  /// `pairing_renotify`); `null` otherwise.
  @JsonKey(required: true)
  final int? retryAfterSeconds;
}
