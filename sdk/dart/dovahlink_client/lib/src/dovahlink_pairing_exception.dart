import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Thrown when a pairing operation reports a non-success outcome.
class DovahLinkPairingException implements Exception {
  /// Creates a pairing exception from the bridge's reported [outcome] and optional retry delay.
  const DovahLinkPairingException(this.outcome, {this.retryAfterSeconds});

  /// The bridge's reported outcome: [PairingOutcome.expired], [PairingOutcome.invalid],
  /// [PairingOutcome.pacingLimited], or [PairingOutcome.hardLimitReached] (from
  /// `pairing_confirm`), [PairingOutcome.pendingNotFound] (from `pairing_ack`).
  final PairingOutcome outcome;

  /// The minimum whole-second wait before retrying, present for `pacing_limited`.
  final int? retryAfterSeconds;

  /// Implements [Object.toString].
  @override
  String toString() => 'DovahLinkPairingException: $outcome';
}
