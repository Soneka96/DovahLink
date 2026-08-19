import 'shared/enums.dart';

/// The bridge's response to `DovahLinkClient.requestPairingRenotify`.
class PairingRenotifyResult {
  /// Creates a pairing renotify result.
  const PairingRenotifyResult({required this.status, this.retryAfterSeconds});

  /// Whether the code was redisplayed, rejected by the renotify cooldown, or nothing was owned.
  final PairingRenotifyStatus status;

  /// The remaining cooldown in seconds before the next manual renotify is accepted, present only
  /// for [PairingRenotifyStatus.cooldown].
  final int? retryAfterSeconds;
}
