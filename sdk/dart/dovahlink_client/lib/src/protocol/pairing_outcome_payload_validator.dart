import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Validates the cross-field and raw-shape invariants of a decoded pairing-outcome payload.
class PairingOutcomePayloadValidator {
  /// Validates decoded fields against the original wire shape [json].
  /// [outcome] determines which metadata is permitted.
  /// [credential], [shortId], and [displayName] carry trust metadata.
  /// [attemptsRemaining] carries a counted wrong-code remainder for `invalid`.
  /// [retryAfterSeconds] carries the Host retry interval when defined.
  /// Throws [ProtocolFormatException] when a value or field combination is malformed.
  static void validate({
    required PairingOutcome outcome,
    required String? credential,
    required String? shortId,
    required String? displayName,
    required int? attemptsRemaining,
    required int? retryAfterSeconds,
    required JsonMap json,
  }) {
    final Object? rawAttemptsRemaining = json['attemptsRemaining'];
    if (rawAttemptsRemaining != null && rawAttemptsRemaining is! int) {
      throw const ProtocolFormatException(
        'attemptsRemaining must be an integer or null.',
      );
    }
    if (rawAttemptsRemaining is int && rawAttemptsRemaining < 0) {
      throw const ProtocolFormatException(
        'attemptsRemaining must not be negative.',
      );
    }

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

    final bool carriesCredential = switch (outcome) {
      PairingOutcome.credentialIssued ||
      PairingOutcome.trusted ||
      PairingOutcome.alreadyTrusted => true,
      _ => false,
    };
    final bool carriesShortId = switch (outcome) {
      PairingOutcome.trusted || PairingOutcome.alreadyTrusted => true,
      _ => false,
    };
    final bool carriesRetryAfterSeconds = switch (outcome) {
      PairingOutcome.pacingLimited ||
      PairingOutcome.renotifyCooldown ||
      PairingOutcome.renotified => true,
      _ => false,
    };
    if (attemptsRemaining != null &&
        (outcome != PairingOutcome.invalid || attemptsRemaining == 0)) {
      throw ProtocolFormatException(
        'attemptsRemaining presence is invalid for $outcome.',
      );
    }
    if ((credential != null) != carriesCredential) {
      throw ProtocolFormatException(
        'credential presence is invalid for $outcome.',
      );
    }
    if (credential != null && credential.isEmpty) {
      throw const ProtocolFormatException(
        'credential must not be empty when present.',
      );
    }
    if ((shortId != null) != carriesShortId) {
      throw ProtocolFormatException(
        'shortId presence is invalid for $outcome.',
      );
    }
    if (shortId != null && shortId.isEmpty) {
      throw const ProtocolFormatException(
        'shortId must not be empty when present.',
      );
    }
    if (!carriesCredential && displayName != null) {
      throw ProtocolFormatException(
        'displayName presence is invalid for $outcome.',
      );
    }
    if ((retryAfterSeconds != null) != carriesRetryAfterSeconds) {
      throw ProtocolFormatException(
        'retryAfterSeconds presence is invalid for $outcome.',
      );
    }
  }
}
