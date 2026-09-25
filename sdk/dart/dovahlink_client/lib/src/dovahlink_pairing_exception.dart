import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Thrown when a pairing operation reports a non-success outcome.
class DovahLinkPairingException implements Exception {
  /// Creates a pairing exception from the Host's [outcome] and runtime retry metadata.
  /// [outcome] identifies the failed pairing operation.
  /// [retryAfterSeconds] carries the Host's safe retry interval when available.
  /// [attemptsRemaining] carries the Host's remaining wrong-code count when available.
  const DovahLinkPairingException(
    this.outcome, {
    this.retryAfterSeconds,
    this.attemptsRemaining,
  });

  /// The Host's reported outcome: [PairingOutcome.expired], [PairingOutcome.invalid],
  /// [PairingOutcome.pacingLimited], or [PairingOutcome.hardLimitReached] (from
  /// `pairing_confirm`), [PairingOutcome.pendingNotFound] or
  /// [PairingOutcome.pairingInvalidated] (from `pairing_ack`).
  final PairingOutcome outcome;

  /// Host-reported whole seconds until retry is safe, when the outcome defines a retry delay.
  final int? retryAfterSeconds;

  /// Host-reported wrong-code attempts remaining after a counted `invalid` result.
  final int? attemptsRemaining;

  /// Implements [Object.toString].
  @override
  String toString() => 'DovahLinkPairingException: $outcome';
}
