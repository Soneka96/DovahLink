import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/envelope_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Expects [EnvelopeValidator] to accept one typed envelope shape.
void expectValidEnvelope({
  required ProtocolMessageType messageType,
  required String? sessionId,
  required String? correlationId,
  required String? bridgeInstanceId,
  required String? playContextId,
  required String? clientId,
}) {
  expect(
    () => EnvelopeValidator.validate(
      messageType: messageType,
      messageId: 'message-1',
      sessionId: sessionId,
      correlationId: correlationId,
      bridgeInstanceId: bridgeInstanceId,
      playContextId: playContextId,
      clientId: clientId,
    ),
    returnsNormally,
  );
}

/// Expects [EnvelopeValidator] to reject one typed envelope shape.
void expectInvalidEnvelope({
  ProtocolMessageType messageType = ProtocolMessageType.pong,
  String messageId = 'message-1',
  String? sessionId = 'session-1',
  String? correlationId = 'message-1',
  String? bridgeInstanceId = 'bridge-1',
  String? playContextId,
  String? clientId,
}) {
  expect(
    () => EnvelopeValidator.validate(
      messageType: messageType,
      messageId: messageId,
      sessionId: sessionId,
      correlationId: correlationId,
      bridgeInstanceId: bridgeInstanceId,
      playContextId: playContextId,
      clientId: clientId,
    ),
    throwsA(isA<ProtocolFormatException>()),
  );
}

/// Runs envelope validator behavior tests.
void main() {
  group('Method validate behaves correctly', () {
    test('accepts valid hello, error, and capability shapes', () {
      expectValidEnvelope(
        messageType: ProtocolMessageType.hello,
        sessionId: null,
        correlationId: null,
        bridgeInstanceId: null,
        playContextId: null,
        clientId: null,
      );
      expectValidEnvelope(
        messageType: ProtocolMessageType.error,
        sessionId: null,
        correlationId: null,
        bridgeInstanceId: null,
        playContextId: null,
        clientId: null,
      );
      expectValidEnvelope(
        messageType: ProtocolMessageType.capabilities,
        sessionId: 'session-1',
        correlationId: null,
        bridgeInstanceId: 'bridge-1',
        playContextId: null,
        clientId: 'client-1',
      );
    });

    test('accepts valid correlated replies and client requests', () {
      expectValidEnvelope(
        messageType: ProtocolMessageType.pairingStatus,
        sessionId: 'session-1',
        correlationId: 'message-1',
        bridgeInstanceId: 'bridge-1',
        playContextId: null,
        clientId: null,
      );
      expectValidEnvelope(
        messageType: ProtocolMessageType.pairingRequest,
        sessionId: 'session-1',
        correlationId: null,
        bridgeInstanceId: 'bridge-1',
        playContextId: null,
        clientId: 'client-1',
      );
      expectValidEnvelope(
        messageType: ProtocolMessageType.ping,
        sessionId: 'session-1',
        correlationId: null,
        bridgeInstanceId: 'bridge-1',
        playContextId: null,
        clientId: 'client-1',
      );
    });

    test('rejects empty message and correlation identifiers', () {
      expectInvalidEnvelope(messageId: '');
      expectInvalidEnvelope(correlationId: '');
    });

    test('rejects impossible correlation requirements', () {
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.helloAck,
        correlationId: null,
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.hello,
        correlationId: 'message-1',
        sessionId: null,
        bridgeInstanceId: null,
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.pairingRequest,
        correlationId: 'message-1',
        clientId: 'client-1',
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.stateEvent,
        correlationId: 'message-1',
        clientId: null,
      );
    });

    test('rejects impossible session and identity shapes', () {
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.hello,
        sessionId: 'session-1',
        correlationId: null,
        bridgeInstanceId: null,
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.error,
        sessionId: '',
        correlationId: null,
      );
      expectInvalidEnvelope(sessionId: null);
      expectInvalidEnvelope(bridgeInstanceId: '');
      expectInvalidEnvelope(playContextId: '');
      expectInvalidEnvelope(clientId: '');
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.hello,
        sessionId: null,
        correlationId: null,
        bridgeInstanceId: 'bridge-1',
      );
    });

    test('rejects invalid client identity requirements', () {
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.helloAck,
        clientId: null,
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.pairingRequest,
        clientId: null,
        correlationId: null,
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.pairingStatus,
        clientId: 'client-1',
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.error,
        correlationId: null,
        clientId: 'client-1',
      );
    });
  });
}
