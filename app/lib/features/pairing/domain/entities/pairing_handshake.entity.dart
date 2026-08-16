import 'package:equatable/equatable.dart';

/// The result of authenticating a bridge session for the pairing flow.
class PairingHandshakeEntity extends Equatable {
  /// Creates a pairing handshake result.
  const PairingHandshakeEntity({
    required this.bridgeVersion,
    required this.trusted,
  });

  /// The DovahLink Bridge/mod release version reported by `hello_ack`.
  final String bridgeVersion;

  /// Whether this session already holds a trusted credential -- either from
  /// `hello`'s own trust tier or from recovering an interrupted pairing
  /// confirmation.
  final bool trusted;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [bridgeVersion, trusted];
}
