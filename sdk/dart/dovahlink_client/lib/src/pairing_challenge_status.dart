import 'shared/enums.dart';

/// The bridge's report of pairing availability, from `DovahLinkClient.requestPairing`.
class PairingChallengeStatus {
  /// Creates a pairing challenge status.
  const PairingChallengeStatus({
    required this.availability,
    this.expiresInSeconds,
  });

  /// Whether a challenge is available, already in progress, unavailable, or owned by another
  /// device.
  final PairingAvailability availability;

  /// The active challenge's remaining *code* validity in seconds -- not a general "how long until
  /// you lose this state" figure. Always non-null for [PairingAvailability.available]. For
  /// [PairingAvailability.inProgress] it is non-null only while the resumed challenge is still
  /// actively counting down; it is `null` when this installation's own resumed state is instead a
  /// pending credential awaiting acknowledgement (the code was already consumed on a successful
  /// confirm, so there is nothing left to count down even though pairing is still `inProgress`).
  /// Always `null` for [PairingAvailability.unavailable] and [PairingAvailability.otherDevicePairing].
  final int? expiresInSeconds;
}
