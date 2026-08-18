import 'package:equatable/equatable.dart';

/// The result of authenticating a bridge session for the pairing flow.
class PairingHandshakeEntity extends Equatable {
  /// Creates a pairing handshake result.
  const PairingHandshakeEntity({
    required this.bridgeVersion,
    required this.trusted,
    this.credentialRejectedMessage,
  });

  /// The DovahLink Bridge/mod release version reported by `hello_ack`.
  final String bridgeVersion;

  /// Whether this session already holds a trusted credential -- either from
  /// `hello`'s own trust tier or from recovering an interrupted pairing
  /// confirmation.
  final bool trusted;

  /// A user-safe explanation, or `null` when not applicable. Set only when this handshake
  /// recovered from a rejected `trusted_device_credential` hello (revoked or unrecognized) by
  /// discarding the stale credential and re-authenticating as unpaired, so the caller can still
  /// tell the user why they are being asked to pair again.
  final String? credentialRejectedMessage;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [bridgeVersion, trusted, credentialRejectedMessage];
}
