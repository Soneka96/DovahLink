import 'package:equatable/equatable.dart';

/// Protocol message values after wire decoding.
class ProtocolEnvelopeEntity extends Equatable {
  /// Creates a protocol envelope contract.
  const ProtocolEnvelopeEntity({
    required this.protocolVersion,
    required this.messageType,
    required this.messageId,
    required this.sessionId,
    required this.correlationId,
    required this.payload,
    this.bridgeInstanceId,
    this.playContextId,
    this.clientId,
  });

  /// The negotiated protocol version for this message.
  final int protocolVersion;

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

  /// The identity of the bridge instance that produced this message.
  /// Present only once [protocolVersion] is 2 or higher.
  final String? bridgeInstanceId;

  /// The identity of the currently loaded play context, when one is active.
  /// Present only once [protocolVersion] is 2 or higher.
  final String? playContextId;

  /// The identity of the logical client, established at `hello`. Present
  /// only once [protocolVersion] is 2 or higher.
  final String? clientId;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    protocolVersion,
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
