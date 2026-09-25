import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_outcome_payload_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'pairing_outcome_payload.g.dart';

/// Incoming `pairing_outcome` payload (`protocol/schema/README.md`'s `pairing_outcome`).
/// Decode-only: the client never sends `pairing_outcome`.
@JsonSerializable(checked: true, createToJson: false)
class PairingOutcomePayload {
  /// Creates a pairing-outcome payload from decoded Host values.
  /// [outcome] is the Host-reported result.
  /// [credential], [shortId], and [displayName] carry trust metadata when applicable.
  /// [attemptsRemaining] and [retryAfterSeconds] carry Host runtime metadata when applicable.
  const PairingOutcomePayload({
    required this.outcome,
    required this.credential,
    required this.shortId,
    required this.displayName,
    required this.attemptsRemaining,
    this.retryAfterSeconds,
  });

  /// Decodes and validates one `pairing_outcome` payload from [json].
  /// Returns the typed Host reply.
  /// Throws [ProtocolFormatException] if its shape or metadata is malformed.
  factory PairingOutcomePayload.fromJson(JsonMap json) {
    try {
      final PairingOutcomePayload payload = _$PairingOutcomePayloadFromJson(
        json,
      );
      PairingOutcomePayloadValidator.validate(
        outcome: payload.outcome,
        credential: payload.credential,
        shortId: payload.shortId,
        displayName: payload.displayName,
        attemptsRemaining: payload.attemptsRemaining,
        retryAfterSeconds: payload.retryAfterSeconds,
        json: json,
      );
      return payload;
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

  /// The Host-calculated number of wrong-code attempts remaining after a counted `invalid` result;
  /// `null` when the attempt was not counted or the outcome is not `invalid`.
  @JsonKey(required: true)
  final int? attemptsRemaining;

  /// Host-authoritative whole seconds until retry is safe, present for `"pacing_limited"`,
  /// `"renotify_cooldown"`, and the newly committed cooldown on `"renotified"`; `null` otherwise.
  @JsonKey(required: true)
  final int? retryAfterSeconds;
}
