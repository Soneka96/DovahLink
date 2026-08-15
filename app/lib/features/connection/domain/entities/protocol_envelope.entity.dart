import 'package:equatable/equatable.dart';

/// Protocol message values after wire decoding.
class ProtocolEnvelopeEntity extends Equatable {
  /// Creates a protocol envelope contract.
  const ProtocolEnvelopeEntity({
    required this.messageType,
    required this.messageId,
    required this.sessionId,
    required this.correlationId,
    required this.payload,
    this.bridgeInstanceId,
    this.playContextId,
    this.clientId,
  });

  /// The canonical message type.
  final String messageType;

  /// The unique message identifier for the session.
  final String messageId;

  /// The server-issued session identifier, when available.
  final String? sessionId;

  /// The identifier of the message being answered, when correlated.
  final String? correlationId;

  /// The message-specific payload.
  final Map<String, dynamic> payload;

  /// The identity of the bridge instance that produced this message. `null`
  /// on the client's own `hello` (the client does not know it yet) and on a
  /// narrow set of early connection-hygiene rejections the bridge cannot
  /// attach an identity to; present otherwise.
  final String? bridgeInstanceId;

  /// The identity of the currently loaded play context, when one is active.
  /// `null` outside an active play context -- genuine semantic absence, not
  /// a placeholder.
  final String? playContextId;

  /// The identity of the logical client, established at `hello`. `null`
  /// before `hello` completes, the same shape [sessionId] already uses.
  final String? clientId;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    messageType,
    messageId,
    sessionId,
    correlationId,
    payload,
    bridgeInstanceId,
    playContextId,
    clientId,
  ];
}
