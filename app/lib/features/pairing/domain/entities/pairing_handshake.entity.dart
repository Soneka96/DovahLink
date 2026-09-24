import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';

/// The result of authenticating a host session for the pairing flow.
class PairingHandshake extends Equatable {
  /// The Host's own release version reported by `hello_ack`.
  final String hostVersion;

  /// Whether this session already holds a trusted credential -- either from
  /// `hello`'s own trust tier or from recovering an interrupted pairing
  /// confirmation.
  final bool trusted;

  /// The typed Host reason for rejecting a stored credential, or `null` when none was rejected.
  final PairingCredentialRejectionReason? credentialRejectionReason;

  /// A user-safe explanation, or `null` when not applicable. Set only when this handshake
  /// recovered from a rejected `trusted_device_credential` hello by discarding the stale
  /// credential and re-authenticating as unpaired, so the user can understand why pairing is
  /// required or unavailable.
  final String? credentialRejectedMessage;

  /// Creates a pairing handshake result.
  const PairingHandshake({
    required this.hostVersion,
    required this.trusted,

    /// Typed Host reason for rejecting a previously stored credential, or `null` when absent.
    this.credentialRejectionReason,
    this.credentialRejectedMessage,
  });

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    hostVersion,
    trusted,
    credentialRejectionReason,
    credentialRejectedMessage,
  ];
}
