import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Validates the cross-field and raw-shape invariants of a decoded pairing-outcome payload.
class PairingOutcomePayloadValidator {
  /// Validates typed decoded fields against the original decoded [json] shape.
  static void validate({
    required PairingOutcome outcome,
    required String? credential,
    required String? shortId,
    required String? displayName,
    required int? retryAfterSeconds,
    required JsonMap json,
  }) {
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
      PairingOutcome.pacingLimited || PairingOutcome.renotifyCooldown => true,
      _ => false,
    };
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
