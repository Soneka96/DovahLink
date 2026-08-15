import 'package:equatable/equatable.dart';

/// The server-issued identity of one negotiated client session.
class ConnectionSessionEntity extends Equatable {
  /// Creates a negotiated session identity.
  const ConnectionSessionEntity({
    required this.sessionId,
    this.bridgeInstanceId,
    this.playContextId,
  });

  /// The opaque identifier that binds messages to this session.
  final String sessionId;

  /// The identity of the bridge instance that negotiated this session. Null
  /// on a narrow set of early connection-hygiene rejections the bridge
  /// cannot attach an identity to; present otherwise.
  final String? bridgeInstanceId;

  /// The identity of the play context active at negotiation time, when one
  /// was. Null outside an active play context -- genuine semantic absence,
  /// not a placeholder.
  final String? playContextId;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [sessionId, bridgeInstanceId, playContextId];
}
