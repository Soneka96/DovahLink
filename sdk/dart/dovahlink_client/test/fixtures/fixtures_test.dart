import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
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
}
