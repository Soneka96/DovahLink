import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'fixtures.dart';

/// Runs SDK fixture-catalog behavior tests.
void main() {
  group('Method buildRequestPolicy behaves correctly', () {
    test('Method buildRequestPolicy builds representative defaults', () {
      final RequestPolicy policy = Fixtures.buildRequestPolicy();

      expect(policy.retrySafe, isTrue);
      expect(policy.requiredTrustState, DovahLinkTrustState.unpaired);
      expect(policy.timeoutClass, TimeoutClass.short);
    });

    test('Method buildRequestPolicy applies named overrides', () {
      final RequestPolicy policy = Fixtures.buildRequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      );

      expect(policy.retrySafe, isFalse);
      expect(policy.requiredTrustState, isNull);
      expect(policy.timeoutClass, TimeoutClass.normal);
    });

    test('Method buildRequestPolicy returns a fresh value per call', () {
      final RequestPolicy first = Fixtures.buildRequestPolicy();
      final RequestPolicy second = Fixtures.buildRequestPolicy();

      expect(first, second);
      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildPendingOperation behaves correctly', () {
    test(
      'Method buildPendingOperation composes the representative policy by default',
      () {
        final PendingOperation operation = Fixtures.buildPendingOperation();

        expect(operation.messageType, ProtocolMessageType.pairingRequest);
        expect(operation.payload, isEmpty);
        expect(operation.policy, Fixtures.buildRequestPolicy());
      },
    );

    test('Method buildPendingOperation preserves named overrides', () {
      final RequestPolicy policy = Fixtures.buildRequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      );
      final PendingOperation operation = Fixtures.buildPendingOperation(
        messageType: ProtocolMessageType.pairingCancel,
        payload: <String, dynamic>{'key': 'value'},
        policy: policy,
      );

      expect(operation.messageType, ProtocolMessageType.pairingCancel);
      expect(operation.payload, <String, dynamic>{'key': 'value'});
      expect(identical(operation.policy, policy), isTrue);
    });

    test('Method buildPendingOperation returns a fresh operation per call', () {
      final PendingOperation first = Fixtures.buildPendingOperation();
      final PendingOperation second = Fixtures.buildPendingOperation();

      expect(identical(first, second), isFalse);
      expect(identical(first.completer, second.completer), isFalse);
      expect(identical(first.policy, second.policy), isFalse);
    });
  });

  group('Method buildEnvelope behaves correctly', () {
    test('Method buildEnvelope builds representative identity defaults', () {
      final Envelope envelope = Fixtures.buildEnvelope();

      expect(envelope.messageType, ProtocolMessageType.pong);
      expect(envelope.messageId, 'reply-1');
      expect(envelope.sessionId, 'session-1');
      expect(envelope.correlationId, 'req-1');
      expect(envelope.bridgeInstanceId, 'bridge-1');
      expect(envelope.playContextId, isNull);
      expect(envelope.clientId, isNull);
      expect(envelope.payload, isEmpty);
    });

    test(
      'Method buildEnvelope preserves named identity and payload overrides',
      () {
        final Envelope envelope = Fixtures.buildEnvelope(
          messageType: ProtocolMessageType.pong,
          messageId: 'message-1',
          sessionId: null,
          correlationId: null,
          payload: <String, dynamic>{'bridgeVersion': '1.2.3'},
          bridgeInstanceId: null,
          playContextId: 'context-1',
          clientId: 'client-1',
        );

        expect(envelope.messageType, ProtocolMessageType.pong);
        expect(envelope.messageId, 'message-1');
        expect(envelope.sessionId, isNull);
        expect(envelope.correlationId, isNull);
        expect(envelope.payload, <String, dynamic>{'bridgeVersion': '1.2.3'});
        expect(envelope.bridgeInstanceId, isNull);
        expect(envelope.playContextId, 'context-1');
        expect(envelope.clientId, 'client-1');
      },
    );

    test('Method buildEnvelope returns a fresh envelope per call', () {
      final Envelope first = Fixtures.buildEnvelope();
      final Envelope second = Fixtures.buildEnvelope();

      expect(identical(first, second), isFalse);
    });
  });

  group('Method buildPersistedClientState behaves correctly', () {
    test('Method buildPersistedClientState builds representative defaults', () {
      final PersistedClientState state = Fixtures.buildPersistedClientState();

      expect(state.clientId, 'client-1');
      expect(state.credential, isNull);
      expect(state.recoveryState, PairingRecoveryState.none);
    });

    test(
      'Method buildPersistedClientState preserves nullable and recovery overrides',
      () {
        final PersistedClientState state = Fixtures.buildPersistedClientState(
          clientId: null,
          credential: 'credential-1',
          recoveryState: PairingRecoveryState.confirming,
        );

        expect(state.clientId, isNull);
        expect(state.credential, 'credential-1');
        expect(state.recoveryState, PairingRecoveryState.confirming);
      },
    );

    test('Method buildPersistedClientState returns a fresh value per call', () {
      final PersistedClientState first = Fixtures.buildPersistedClientState();
      final PersistedClientState second = Fixtures.buildPersistedClientState();

      expect(first, second);
      expect(identical(first, second), isFalse);
    });
  });
}
