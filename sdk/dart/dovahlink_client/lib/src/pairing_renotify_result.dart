import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// The Host's response to [DovahLinkClient.requestPairingRenotify].
class PairingRenotifyResult {
  /// Creates a pairing renotify result.
  const PairingRenotifyResult({required this.status, this.retryAfterSeconds});

  /// Whether the code was redisplayed, rejected by the renotify cooldown, or nothing was owned.
  final PairingRenotifyStatus status;

  /// Host-reported cooldown seconds, newly started after [PairingRenotifyStatus.renotified] or
  /// remaining before another request is accepted for [PairingRenotifyStatus.cooldown]; `null` for
  /// [PairingRenotifyStatus.alreadyIdle].
  final int? retryAfterSeconds;
}
