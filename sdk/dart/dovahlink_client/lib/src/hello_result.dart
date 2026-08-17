import 'enums.dart';

/// The bootstrap information `DovahLinkClient.hello`/`DovahLinkClient.authenticate` returns on
/// success.
class HelloResult {
  /// Creates a hello result.
  const HelloResult({
    required this.bridgeVersion,
    required this.trustState,
    this.recoveredFromRejectedCredential,
  });

  /// The DovahLink Bridge/mod release version.
  final String bridgeVersion;

  /// The trust tier the session was admitted at.
  final DovahLinkTrustState trustState;

  /// Set when `DovahLinkClient.authenticate` recovered from a rejected `trusted_device_credential`
  /// hello by discarding the stale credential and retrying as `unpaired`; `null` on an ordinary
  /// hello with nothing to recover from.
  final CredentialRejectionReason? recoveredFromRejectedCredential;
}
