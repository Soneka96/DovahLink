import 'package:equatable/equatable.dart';

/// The server-issued identity of one negotiated client session.
class ConnectionSessionEntity extends Equatable {
  /// Creates a negotiated session identity.
  const ConnectionSessionEntity({
    required this.sessionId,
    required this.protocolVersion,
    this.bridgeInstanceId,
    this.playContextId,
  });

  /// The opaque identifier that binds messages to this session.
  final String sessionId;

  /// The protocol version selected during negotiation.
  final int protocolVersion;

  /// The identity of the bridge instance that negotiated this session.
  /// Present only once [protocolVersion] is 2 or higher.
  final String? bridgeInstanceId;

  /// The identity of the play context active at negotiation time, when one
  /// was. Present only once [protocolVersion] is 2 or higher.
  final String? playContextId;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    sessionId,
    protocolVersion,
    bridgeInstanceId,
    playContextId,
  ];
}
