import 'dart:async';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/internal/requests/pending_operation.fixture.dart';
import '../../fixtures/protocol/envelope.fixture.dart';
import '../../fixtures/request_policy.fixture.dart';

/// Non-retry-safe policy for operations whose lost response makes retransmission ambiguous.
final RequestPolicy _nonRetrySafePolicy = buildRequestPolicy(
  retrySafe: false,
  requiredTrustState: null,
  timeoutClass: TimeoutClass.normal,
);

/// Builds one reply envelope correlated to [correlationId].
Envelope buildReply(String correlationId) => buildEnvelope(
  messageType: ProtocolMessageType.pairingStatus,
  correlationId: correlationId,
);

/// Runs pending-operation-bookkeeping behavior tests.
void main() {
  late PendingOperationBookkeeping bookkeeping;

  setUp(() {
    bookkeeping = PendingOperationBookkeeping();
  });

  group('Method resolveReply behaves correctly', () {
    test(
      'Method resolveReply returns false when no pending operation matches',
      () {
        final bool resolved = bookkeeping.resolveReply(
          'no-such-id',
          buildReply('no-such-id'),
        );

        expect(resolved, isFalse);
      },
    );

    test(
      'Method resolveReply completes the registered operation and returns true',
      () {
        final PendingOperation operation = buildPendingOperation();
        bookkeeping.register('id-1', operation);
        final Envelope reply = buildReply('id-1');

        final bool resolved = bookkeeping.resolveReply('id-1', reply);

        expect(resolved, isTrue);
        expect(operation.completer.isCompleted, isTrue);
      },
    );

    test('Method resolveReply cancels the operation timer', () async {
      final PendingOperation operation = buildPendingOperation();
      bool timerFired = false;
      operation.timer = Timer(
        const Duration(milliseconds: 20),
        () => timerFired = true,
      );
      bookkeeping.register('id-1', operation);

      bookkeeping.resolveReply('id-1', buildReply('id-1'));
      await Future<void>.delayed(const Duration(milliseconds: 40));

      expect(timerFired, isFalse);
    });

    test('Method resolveReply resolves the same id only once', () {
      final PendingOperation operation = buildPendingOperation();
      bookkeeping.register('id-1', operation);
      bookkeeping.resolveReply('id-1', buildReply('id-1'));

      final bool resolvedAgain = bookkeeping.resolveReply(
        'id-1',
        buildReply('id-1'),
      );

      expect(resolvedAgain, isFalse);
    });
  });

  group('Method failAll behaves correctly', () {
    test(
      'Method failAll fails a non-retrySafe operation immediately even when orphaning is requested',
      () {
        final PendingOperation operation = buildPendingOperation(
          policy: _nonRetrySafePolicy,
        );
        bookkeeping.register('id-1', operation);

        bookkeeping.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        operation.completer.future.ignore();

        expect(operation.completer.isCompleted, isTrue);
        expect(bookkeeping.takeOrphaned(), isEmpty);
      },
    );

    test(
      'Method failAll fails a retrySafe operation immediately when not orphaning',
      () {
        final PendingOperation operation = buildPendingOperation();
        bookkeeping.register('id-1', operation);

        bookkeeping.failAll(
          const DovahLinkConnectionException('protocol violation'),
          orphanRetrySafeOperations: false,
        );
        operation.completer.future.ignore();

        expect(operation.completer.isCompleted, isTrue);
      },
    );

    test(
      'Method failAll orphans a retrySafe operation instead of failing it, when requested',
      () {
        final PendingOperation operation = buildPendingOperation();
        bookkeeping.register('id-1', operation);

        bookkeeping.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );

        expect(operation.completer.isCompleted, isFalse);
        expect(bookkeeping.takeOrphaned(), <PendingOperation>[operation]);
      },
    );

    test(
      'Method failAll fails an already-retried operation immediately instead of re-orphaning it',
      () {
        final PendingOperation operation = buildPendingOperation()
          ..hasRetried = true;
        bookkeeping.register('id-1', operation);

        bookkeeping.failAll(
          const DovahLinkConnectionException('lost again'),
          orphanRetrySafeOperations: true,
        );
        operation.completer.future.ignore();

        expect(operation.completer.isCompleted, isTrue);
        expect(bookkeeping.takeOrphaned(), isEmpty);
      },
    );

    test(
      'Method failAll cancels the timer of every operation it fails',
      () async {
        final PendingOperation operation = buildPendingOperation(
          policy: _nonRetrySafePolicy,
        );
        bool timerFired = false;
        operation.timer = Timer(
          const Duration(milliseconds: 20),
          () => timerFired = true,
        );
        bookkeeping.register('id-1', operation);

        bookkeeping.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        operation.completer.future.ignore();
        await Future<void>.delayed(const Duration(milliseconds: 40));

        expect(timerFired, isFalse);
      },
    );

    test(
      'Method failAll fails an already-orphaned operation too when a later call does not orphan',
      () {
        final PendingOperation operation = buildPendingOperation();
        bookkeeping.register('id-1', operation);
        bookkeeping.failAll(
          const DovahLinkConnectionException('first loss'),
          orphanRetrySafeOperations: true,
        );

        bookkeeping.failAll(
          const DovahLinkConnectionException('second loss'),
          orphanRetrySafeOperations: false,
        );
        operation.completer.future.ignore();

        expect(operation.completer.isCompleted, isTrue);
        expect(bookkeeping.takeOrphaned(), isEmpty);
      },
    );

    test(
      'Method failAll is a no-op with no pending or orphaned operations',
      () {
        expect(
          () => bookkeeping.failAll(
            const DovahLinkConnectionException('nothing to fail'),
            orphanRetrySafeOperations: true,
          ),
          returnsNormally,
        );
      },
    );

    test(
      'Method failAll fails every pending operation, not just the first',
      () {
        final PendingOperation first = buildPendingOperation(
          policy: _nonRetrySafePolicy,
        );
        final PendingOperation second = buildPendingOperation(
          policy: _nonRetrySafePolicy,
        );
        bookkeeping.register('id-1', first);
        bookkeeping.register('id-2', second);

        bookkeeping.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        first.completer.future.ignore();
        second.completer.future.ignore();

        expect(first.completer.isCompleted, isTrue);
        expect(second.completer.isCompleted, isTrue);
      },
    );
  });

  group('Method takeOrphaned behaves correctly', () {
    test('Method takeOrphaned returns an empty list with nothing orphaned', () {
      expect(bookkeeping.takeOrphaned(), isEmpty);
    });

    test(
      'Method takeOrphaned clears the orphaned list so a second call returns nothing',
      () {
        final PendingOperation operation = buildPendingOperation();
        bookkeeping.register('id-1', operation);
        bookkeeping.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );

        bookkeeping.takeOrphaned();

        expect(bookkeeping.takeOrphaned(), isEmpty);
      },
    );
  });
}
