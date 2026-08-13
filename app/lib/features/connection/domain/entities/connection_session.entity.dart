import 'package:equatable/equatable.dart';

/// The server-issued identity of one negotiated client session.
class ConnectionSessionEntity extends Equatable {
  /// Creates a negotiated session identity.
  const ConnectionSessionEntity({
    required this.sessionId,
    required this.protocolVersion,
  });

  /// The opaque identifier that binds messages to this session.
  final String sessionId;

  /// The protocol version selected during negotiation.
  final int protocolVersion;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [sessionId, protocolVersion];
}
