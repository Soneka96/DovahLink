import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// The bridge's response to [DovahLinkClient.cancelPairing].
class PairingCancelOutcome {
  /// Creates a pairing cancel outcome.
  const PairingCancelOutcome({required this.status});

  /// Whether an owned active challenge or pending credential was cleared, or nothing was owned.
  final PairingCancelStatus status;
}
