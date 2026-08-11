/// Protocol message values after wire decoding.
abstract class ProtocolEnvelopeEntity {
  /// Creates a protocol envelope contract.
  const ProtocolEnvelopeEntity();

  /// The negotiated protocol version for this message.
  int get protocolVersion;

  /// The canonical message type.
  String get messageType;

  /// The unique message identifier for the session.
  String get messageId;

  /// The server-issued session identifier, when available.
  String? get sessionId;

  /// The identifier of the message being answered, when correlated.
  String? get correlationId;

  /// The message-specific payload.
  Map<String, dynamic> get payload;
}
