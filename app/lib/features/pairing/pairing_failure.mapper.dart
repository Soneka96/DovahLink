import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show DovahLinkPairingException, PairingOutcome;

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Maps SDK pairing exceptions into app-owned typed pairing failures.
abstract final class PairingFailureMapper {
  /// Maps [error] when its outcome represents a known pairing failure.
  /// @param error The SDK outcome and Host-reported metadata.
  /// @param fallbackMessage Safe fallback copy carried by the failure boundary.
  /// @param keepCodeEntry Whether the current code-entry challenge remains active.
  /// @return A typed failure, or `null` when the SDK outcome is not a failure.
  static PairingFailure? fromSdkException(
    DovahLinkPairingException error, {
    required String fallbackMessage,
    bool keepCodeEntry = false,
  }) {
    final PairingFailureOutcome? outcome = switch (error.outcome) {
      PairingOutcome.expired => PairingFailureOutcome.expired,
      PairingOutcome.invalid => PairingFailureOutcome.invalid,
      PairingOutcome.pacingLimited => PairingFailureOutcome.pacingLimited,
      PairingOutcome.hardLimitReached => PairingFailureOutcome.hardLimitReached,
      PairingOutcome.pendingNotFound => PairingFailureOutcome.pendingNotFound,
      PairingOutcome.pairingInvalidated =>
        PairingFailureOutcome.pairingInvalidated,
      _ => null,
    };
    if (outcome == null) {
      return null;
    }
    if (keepCodeEntry &&
        (outcome == PairingFailureOutcome.invalid ||
            outcome == PairingFailureOutcome.pacingLimited)) {
      return PairingRetriableFailure(
        fallbackMessage,
        outcome: outcome,
        attemptsRemaining: error.attemptsRemaining,
      );
    }
    return PairingFailure(
      fallbackMessage,
      outcome: outcome,
      attemptsRemaining: error.attemptsRemaining,
    );
  }
}
