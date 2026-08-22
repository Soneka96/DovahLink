import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

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
      final PairingOutcomePayload payload = _$PairingOutcomePayloadFromJson(
        json,
      );
      final Object? rawRetryAfterSeconds = json['retryAfterSeconds'];
      if (rawRetryAfterSeconds != null && rawRetryAfterSeconds is! int) {
        throw const ProtocolFormatException(
          'retryAfterSeconds must be an integer or null.',
        );
      }
      if (rawRetryAfterSeconds is int && rawRetryAfterSeconds < 0) {
        throw const ProtocolFormatException(
          'retryAfterSeconds must not be negative.',
        );
      }

      final bool carriesCredential = switch (payload.outcome) {
        PairingOutcome.credentialIssued ||
        PairingOutcome.trusted ||
        PairingOutcome.alreadyTrusted => true,
        _ => false,
      };
      final bool carriesShortId = switch (payload.outcome) {
        PairingOutcome.trusted || PairingOutcome.alreadyTrusted => true,
        _ => false,
      };
      final bool carriesRetryAfterSeconds = switch (payload.outcome) {
        PairingOutcome.pacingLimited || PairingOutcome.renotifyCooldown => true,
        _ => false,
      };
      if ((payload.credential != null) != carriesCredential) {
        throw ProtocolFormatException(
          'credential presence is invalid for ${payload.outcome}.',
        );
      }
      if (payload.credential != null && payload.credential!.isEmpty) {
        throw const ProtocolFormatException(
          'credential must not be empty when present.',
        );
      }
      if ((payload.shortId != null) != carriesShortId) {
        throw ProtocolFormatException(
          'shortId presence is invalid for ${payload.outcome}.',
        );
      }
      if (payload.shortId != null && payload.shortId!.isEmpty) {
        throw const ProtocolFormatException(
          'shortId must not be empty when present.',
        );
      }
      if (!carriesCredential && payload.displayName != null) {
        throw ProtocolFormatException(
          'displayName presence is invalid for ${payload.outcome}.',
        );
      }
      if ((payload.retryAfterSeconds != null) != carriesRetryAfterSeconds) {
        throw ProtocolFormatException(
          'retryAfterSeconds presence is invalid for ${payload.outcome}.',
        );
      }
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

  /// The remaining wait in seconds before the next attempt is accepted, present for
  /// `"pacing_limited"` (next `pairing_confirm`) and `"renotify_cooldown"` (next
  /// `pairing_renotify`); `null` otherwise.
  @JsonKey(required: true)
  final int? retryAfterSeconds;
}
