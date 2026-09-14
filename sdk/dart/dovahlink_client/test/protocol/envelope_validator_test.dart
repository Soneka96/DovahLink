import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/envelope_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Expects [EnvelopeValidator] to accept one typed envelope shape.
void expectValidEnvelope({
  required ProtocolMessageType messageType,
  required String? sessionId,
  required String? correlationId,
  required String? stateAuthorityId,
  bool? stateAuthorityIdPresent,
  required String? playContextId,
  required String? clientId,
}) {
  expect(
    () => EnvelopeValidator.validate(
      messageType: messageType,
      messageId: 'message-1',
      sessionId: sessionId,
      correlationId: correlationId,
      stateAuthorityId: stateAuthorityId,
      stateAuthorityIdPresent:
          stateAuthorityIdPresent ?? (stateAuthorityId != null),
      playContextId: playContextId,
      clientId: clientId,
    ),
    returnsNormally,
  );
}

/// Expects [EnvelopeValidator] to reject one typed envelope shape. Defaults to `pong` with a
/// `null` [stateAuthorityId] (a valid combination on its own) so a default-driven call only
/// throws because of the field the test actually overrides, not incidentally from the default
/// shape itself. [stateAuthorityIdPresent] defaults to whether [stateAuthorityId] is non-null,
/// the ordinary case where "present" and "non-null" coincide; pass it explicitly to construct the
/// key-present-but-null shape a real decoded JSON payload can have but a typed value alone cannot
/// distinguish.
void expectInvalidEnvelope({
  ProtocolMessageType messageType = ProtocolMessageType.pong,
  String messageId = 'message-1',
  String? sessionId = 'session-1',
  String? correlationId = 'message-1',
  String? stateAuthorityId,
  bool? stateAuthorityIdPresent,
  String? playContextId,
  String? clientId,
}) {
  expect(
    () => EnvelopeValidator.validate(
      messageType: messageType,
      messageId: messageId,
      sessionId: sessionId,
      correlationId: correlationId,
      stateAuthorityId: stateAuthorityId,
      stateAuthorityIdPresent:
          stateAuthorityIdPresent ?? (stateAuthorityId != null),
      playContextId: playContextId,
      clientId: clientId,
    ),
    throwsA(isA<ProtocolFormatException>()),
  );
}

/// Runs envelope validator behavior tests.
void main() {
  group('Method validate behaves correctly', () {
    test(
      'Method validate accepts valid hello, error, and capability shapes',
      () {
        expectValidEnvelope(
          messageType: ProtocolMessageType.hello,
          sessionId: null,
          correlationId: null,
          stateAuthorityId: null,
          playContextId: null,
          clientId: null,
        );
        expectValidEnvelope(
          messageType: ProtocolMessageType.error,
          sessionId: null,
          correlationId: null,
          stateAuthorityId: null,
          playContextId: null,
          clientId: null,
        );
        expectValidEnvelope(
          messageType: ProtocolMessageType.capabilities,
          sessionId: 'session-1',
          correlationId: null,
          stateAuthorityId: null,
          playContextId: null,
          clientId: 'client-1',
        );
      },
    );

    test(
      'Method validate accepts valid correlated replies and client requests',
      () {
        expectValidEnvelope(
          messageType: ProtocolMessageType.helloAck,
          sessionId: 'session-1',
          correlationId: 'message-1',
          stateAuthorityId: 'state-authority-1',
          playContextId: null,
          clientId: 'client-1',
        );
        expectValidEnvelope(
          messageType: ProtocolMessageType.pairingStatus,
          sessionId: 'session-1',
          correlationId: 'message-1',
          stateAuthorityId: null,
          playContextId: null,
          clientId: null,
        );
        expectValidEnvelope(
          messageType: ProtocolMessageType.pairingRequest,
          sessionId: 'session-1',
          correlationId: null,
          stateAuthorityId: null,
          playContextId: null,
          clientId: 'client-1',
        );
        expectValidEnvelope(
          messageType: ProtocolMessageType.ping,
          sessionId: 'session-1',
          correlationId: null,
          stateAuthorityId: null,
          playContextId: null,
          clientId: 'client-1',
        );
      },
    );

    test(
      'Method validate rejects empty message and correlation identifiers',
      () {
        expectInvalidEnvelope(messageId: '');
        expectInvalidEnvelope(correlationId: '');
      },
    );

    test('Method validate rejects impossible correlation requirements', () {
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.helloAck,
        correlationId: null,
        stateAuthorityId: 'state-authority-1',
        clientId: 'client-1',
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.hello,
        correlationId: 'message-1',
        sessionId: null,
        stateAuthorityId: null,
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.pairingRequest,
        correlationId: 'message-1',
        clientId: 'client-1',
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.stateEvent,
        correlationId: 'message-1',
        stateAuthorityId: 'state-authority-1',
        clientId: null,
      );
    });

    test('Method validate rejects impossible session and identity shapes', () {
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.hello,
        sessionId: 'session-1',
        correlationId: null,
        stateAuthorityId: null,
      );
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.error,
        sessionId: '',
        correlationId: null,
      );
      expectInvalidEnvelope(sessionId: null);
      expectInvalidEnvelope(stateAuthorityId: '');
      expectInvalidEnvelope(playContextId: '');
      expectInvalidEnvelope(clientId: '');
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.hello,
        sessionId: null,
        correlationId: null,
        stateAuthorityId: 'state-authority-1',
      );
    });

    test('Method validate rejects invalid client identity requirements', () {
      expectInvalidEnvelope(
        messageType: ProtocolMessageType.helloAck,
        stateAuthorityId: 'state-authority-1',
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

    test(
      'Method validate rejects stateAuthorityId absent on a gated message type',
      () {
        expectInvalidEnvelope(messageType: ProtocolMessageType.stateSnapshot);
      },
    );

    test(
      'Method validate rejects stateAuthorityId absent for every gated message type, not only state_snapshot',
      () {
        expectInvalidEnvelope(
          messageType: ProtocolMessageType.stateEvent,
          correlationId: null,
        );
      },
    );

    test(
      'Method validate rejects an empty stateAuthorityId on a gated message type',
      () {
        expectInvalidEnvelope(
          messageType: ProtocolMessageType.helloAck,
          stateAuthorityId: '',
          clientId: 'client-1',
        );
      },
    );

    test(
      'Method validate rejects a non-null stateAuthorityId on a non-gated message type',
      () {
        expectInvalidEnvelope(
          messageType: ProtocolMessageType.pairingStatus,
          correlationId: 'message-1',
          stateAuthorityId: 'state-authority-1',
        );
      },
    );

    test(
      'Method validate rejects a present stateAuthorityId key with a null value on a non-gated message type',
      () {
        expectInvalidEnvelope(
          messageType: ProtocolMessageType.pong,
          stateAuthorityId: null,
          stateAuthorityIdPresent: true,
        );
      },
    );
  });

  group('Method isClientIdRequired behaves correctly', () {
    test(
      'Method isClientIdRequired returns true only for every message type that carries an '
      'envelope-level clientId',
      () {
        const Set<ProtocolMessageType> requiresClientId = <ProtocolMessageType>{
          ProtocolMessageType.helloAck,
          ProtocolMessageType.pairingRequest,
          ProtocolMessageType.pairingConfirm,
          ProtocolMessageType.pairingAck,
          ProtocolMessageType.pairingRenotify,
          ProtocolMessageType.pairingCancel,
          ProtocolMessageType.renameRequest,
          ProtocolMessageType.subscribe,
          ProtocolMessageType.snapshotRequest,
          ProtocolMessageType.ping,
        };
        for (final ProtocolMessageType messageType
            in ProtocolMessageType.values) {
          expect(
            EnvelopeValidator.isClientIdRequired(messageType),
            requiresClientId.contains(messageType),
            reason: 'unexpected isClientIdRequired($messageType)',
          );
        }
      },
    );
  });
}
