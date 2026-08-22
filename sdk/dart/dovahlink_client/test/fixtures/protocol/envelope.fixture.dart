import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Builds a decoded protocol envelope with representative identity defaults.
Envelope buildEnvelope({
  ProtocolMessageType messageType = ProtocolMessageType.pong,
  String messageId = 'reply-1',
  String? sessionId = 'session-1',
  String? correlationId = 'req-1',
  JsonMap payload = const <String, dynamic>{},
  String? bridgeInstanceId = 'bridge-1',
  String? playContextId,
  String? clientId,
}) => Envelope(
  messageType: messageType,
  messageId: messageId,
  sessionId: sessionId,
  correlationId: correlationId,
  payload: payload,
  bridgeInstanceId: bridgeInstanceId,
  playContextId: playContextId,
  clientId: clientId,
);
