import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// The bootstrap information [DovahLinkClient.hello]/[DovahLinkClient.authenticate] returns on
/// success.
class HelloResult {
  /// Creates a hello result.
  const HelloResult({
    required this.hostVersion,
    required this.trustState,
    this.recoveredFromRejectedCredential,
  });

  /// The Host's own release version, the compatibility authority.
  final String hostVersion;

  /// The trust tier the session was admitted at.
  final DovahLinkTrustState trustState;

  /// Set when [DovahLinkClient.authenticate] recovered from a rejected `trusted_device_credential`
  /// hello by discarding the stale credential and retrying as `unpaired`; `null` on an ordinary
  /// hello with nothing to recover from.
  final CredentialRejectionReason? recoveredFromRejectedCredential;
}
