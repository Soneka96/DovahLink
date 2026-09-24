import 'package:dovahlink_client_sdk/dovahlink_client.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';

/// Represents the SDK authentication result at the Pairing data boundary.
class PairingHandshakeModel extends PairingHandshake {
  /// Creates a pairing handshake model from mapped data.
  const PairingHandshakeModel({
    /// Host release version reported by the SDK.
    required super.hostVersion,

    /// Resolved session trust standing after pairing recovery.
    required super.trusted,

    /// User-safe explanation for a recovered rejected credential, if any.
    super.credentialRejectedMessage,
  });

  /// Maps the SDK [hello] and resolved session [trusted] status to a model.
  factory PairingHandshakeModel.fromHelloResult({
    /// SDK authentication result for the session.
    required HelloResult hello,

    /// Trust status after checking for interrupted pairing recovery.
    required bool trusted,
  }) => PairingHandshakeModel(
    hostVersion: hello.hostVersion,
    trusted: trusted,
    credentialRejectedMessage: switch (hello.recoveredFromRejectedCredential) {
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
