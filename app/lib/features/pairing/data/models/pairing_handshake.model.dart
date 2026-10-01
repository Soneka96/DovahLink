import 'package:dovahlink_client_sdk/dovahlink_client.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// Represents the SDK authentication result at the Pairing data boundary.
class PairingHandshakeModel extends PairingHandshake {
  /// Creates a pairing handshake model from mapped data.
  const PairingHandshakeModel({
    /// Host release version reported by the SDK.
    required super.hostVersion,

    /// Resolved session trust standing after pairing recovery.
    required super.trusted,

    /// Typed Host rejection reason for a previously stored credential, if any.
    super.credentialRejectionReason,

    /// User-safe explanation for a recovered rejected credential, if any.
    super.credentialRejectedMessage,
  });

  /// Maps [handshake] after SDK-owned pending pairing recovery.
  factory PairingHandshakeModel.fromPairingHandshake(
    DovahLinkPairingHandshake handshake,
  ) => PairingHandshakeModel._fromValues(
    hostVersion: handshake.hello.hostVersion,
    trusted: handshake.trustState == DovahLinkTrustState.trusted,
    recoveredFromRejectedCredential:
        handshake.hello.recoveredFromRejectedCredential,
  );

  factory PairingHandshakeModel._fromValues({
    required String hostVersion,
    required bool trusted,
    required CredentialRejectionReason? recoveredFromRejectedCredential,
  }) => PairingHandshakeModel(
    hostVersion: hostVersion,
    trusted: trusted,
    credentialRejectionReason: switch (recoveredFromRejectedCredential) {
      CredentialRejectionReason.revoked =>
        PairingCredentialRejectionReason.revoked,
      CredentialRejectionReason.unrecognized =>
        PairingCredentialRejectionReason.unrecognized,
      CredentialRejectionReason.blocked =>
        PairingCredentialRejectionReason.blocked,
      null => null,
    },
    credentialRejectedMessage: switch (recoveredFromRejectedCredential) {
      CredentialRejectionReason.revoked => "This device's trust was revoked.",
      CredentialRejectionReason.unrecognized =>
        "This device isn't recognized by this host.",
      CredentialRejectionReason.blocked =>
        'This device is blocked by the host and cannot be paired again until an '
            'administrator unblocks it.',
      null => null,
    },
  );
}
