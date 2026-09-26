import 'package:dovahlink_client_sdk/dovahlink_client.dart';

/// The connected Host identity, compatibility version, and trust outcome returned by
/// [DovahLinkClient.hello]/[DovahLinkClient.authenticate].
class HelloResult {
  /// Creates a hello result.
  /// @param hostId The stable DovahLink identity of the connected Host installation.
  /// @param hostName The current OS computer name reported by the Host.
  /// @param hostVersion The Host release version used for compatibility checks.
  /// @param trustState The trust tier the session was admitted at.
  /// @param recoveredFromRejectedCredential The credential rejection recovered during authentication, if any.
  const HelloResult({
    required this.hostId,
    required this.hostName,
    required this.hostVersion,
    required this.trustState,
    this.recoveredFromRejectedCredential,
  });

  /// The stable DovahLink-generated UUID of the connected Host installation.
  final String hostId;

  /// The current operating-system computer name reported by the connected Host.
  final String hostName;

  /// The Host's own release version, the compatibility authority.
  final String hostVersion;

  /// The trust tier the session was admitted at.
  final DovahLinkTrustState trustState;

  /// Set when [DovahLinkClient.authenticate] recovered from a rejected `trusted_device_credential`
  /// hello by discarding the stale credential and retrying as `unpaired`; `null` on an ordinary
  /// hello with nothing to recover from.
  final CredentialRejectionReason? recoveredFromRejectedCredential;
}
