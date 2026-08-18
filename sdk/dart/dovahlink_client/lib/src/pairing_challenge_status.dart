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

  /// The active challenge's remaining code validity in seconds, present only for
  /// [PairingAvailability.available] and [PairingAvailability.inProgress].
  final int? expiresInSeconds;
}
