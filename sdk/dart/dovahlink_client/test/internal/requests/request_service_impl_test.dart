import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service_impl.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/internal/requests/pending_operation.fixture.dart';
import '../../fixtures/protocol/envelope.fixture.dart';
import '../../fixtures/request_policy.fixture.dart';

/// Mock session service used to control connection state and trust state, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionService extends Mock implements SessionService {}

/// Mock pending-operation bookkeeping -- its own fail/orphan logic is
/// `pending_operation_bookkeeping_test.dart`'s responsibility; this file only proves
/// [RequestServiceImpl] calls it with the right arguments.
class MockPendingOperationBookkeeping extends Mock
    implements PendingOperationBookkeeping {}

/// Mock pending-operation transmitter -- its own wire mechanics (message-ID generation, envelope
/// encoding, timeout arming, transport send) are `pending_operation_transmitter_test.dart`'s
/// responsibility; this file only proves [RequestServiceImpl] hands it the right operation.
class MockPendingOperationTransmitter extends Mock
    implements PendingOperationTransmitter {}

/// Mock message router -- its own decoding/correlation/routing logic is
/// `message_router_test.dart`'s responsibility; this file only proves [RequestServiceImpl] forwards
/// to it.
class MockMessageRouter extends Mock implements MessageRouter {}

/// Retry-safe policy for operations that require an admitted unpaired session.
final RequestPolicy _retrySafeUnpairedPolicy = buildRequestPolicy();

/// Non-retry-safe policy for operations whose lost response makes retransmission ambiguous.
final RequestPolicy _nonRetrySafePolicy = buildRequestPolicy(
  retrySafe: false,
  requiredTrustState: null,
  timeoutClass: TimeoutClass.normal,
);

/// Retry-safe policy for operations with no trust-state requirement to revalidate on retry.
final RequestPolicy _retrySafeAnyTrustPolicy = buildRequestPolicy(
  requiredTrustState: null,
);

/// Runs request-service behavior tests.
void main() {
  late MockSessionService sessionService;
  late MockPendingOperationBookkeeping bookkeeping;
  late MockPendingOperationTransmitter transmitter;
  late MockMessageRouter messageRouter;
  late RequestServiceImpl service;

  setUpAll(() {
    registerFallbackValue(buildPendingOperation());
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
  });

  setUp(() {
    sessionService = MockSessionService();
    bookkeeping = MockPendingOperationBookkeeping();
    transmitter = MockPendingOperationTransmitter();
    messageRouter = MockMessageRouter();
    when(
      () => sessionService.currentTrustState,
    ).thenReturn(DovahLinkTrustState.unpaired);
    when(
      () => sessionService.connectionState,
    ).thenReturn(DovahLinkConnectionState.connected);
    when(() => transmitter.transmit(any())).thenAnswer((_) {});
    when(
      () => bookkeeping.failAll(
        any(),
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
      ),
    ).thenAnswer((_) {});
    service = RequestServiceImpl(
      sessionService: sessionService,
      bookkeeping: bookkeeping,
      transmitter: transmitter,
      messageRouter: messageRouter,
    );
  });

  group('Method sendAndAwait behaves correctly', () {
    test(
      'Method sendAndAwait constructs a PendingOperation and hands it to the transmitter',
      () async {
        unawaited(
          service.sendAndAwait(
            messageType: ProtocolMessageType.pairingRequest,
            payload: const <String, dynamic>{'key': 'value'},
            expectedType: ProtocolMessageType.pairingStatus,
            policy: _retrySafeUnpairedPolicy,
          ),
        );
        await pumpEventQueue();

        final VerificationResult verification = verify(
          () => transmitter.transmit(captureAny()),
        );
        verification.called(1);
        final PendingOperation operation =
            verification.captured.single as PendingOperation;
        expect(operation.messageType, ProtocolMessageType.pairingRequest);
        expect(operation.payload, <String, dynamic>{'key': 'value'});
        expect(operation.policy, _retrySafeUnpairedPolicy);
      },
    );

    test(
      'Method sendAndAwait resolves with the validated reply once the transmitted operation completes',
      () async {
        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final PendingOperation operation =
            verify(() => transmitter.transmit(captureAny())).captured.single
                as PendingOperation;

        operation.completer.complete(
          buildEnvelope(
            messageType: ProtocolMessageType.pairingStatus,
            payload: const <String, dynamic>{'state': 'unavailable'},
          ),
        );

        final Envelope result = await pending;
        expect(result.messageType, ProtocolMessageType.pairingStatus);
      },
    );

    test(
      'Method sendAndAwait throws DovahLinkProtocolException for a wire error reply',
      () async {
        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final PendingOperation operation =
            verify(() => transmitter.transmit(captureAny())).captured.single
                as PendingOperation;

        operation.completer.complete(
          buildEnvelope(
            messageType: ProtocolMessageType.error,
            payload: const <String, dynamic>{
              'code': 'unauthenticated',
              'message': 'nope',
              'retryable': false,
            },
          ),
        );

        await expectLater(
          pending,
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.unauthenticated,
            ),
          ),
        );
      },
    );

    test(
      'Method sendAndAwait throws DovahLinkProtocolException(unexpected_message_type) for a mismatched reply',
      () async {
        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final PendingOperation operation =
            verify(() => transmitter.transmit(captureAny())).captured.single
                as PendingOperation;

        operation.completer.complete(
          buildEnvelope(
            messageType: ProtocolMessageType.pairingOutcome,
            payload: const <String, dynamic>{},
          ),
        );

        await expectLater(
          pending,
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
      },
    );
  });

  group('Behavior connectionState guard behaves correctly', () {
    test(
      'Behavior connectionState guard fails a request immediately with DovahLinkConnectionException '
      'when disconnected, without transmitting anything',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.disconnected);

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transmitter.transmit(any()));
      },
    );

    test(
      'Behavior connectionState guard fails a request immediately when administratively invalidated',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.administrativelyInvalidated);

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transmitter.transmit(any()));
      },
    );

    test(
      'Behavior connectionState guard fails a request immediately while a connect attempt is still in flight',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connecting);

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transmitter.transmit(any()));
      },
    );

    test(
      'Behavior connectionState guard fails a request immediately with DovahLinkConnectionException '
      'while reconnecting, without transmitting anything',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.reconnecting);

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transmitter.transmit(any()));
      },
    );

    test(
      'Behavior connectionState guard allows a request while connected',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);

        unawaited(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
        );
        await pumpEventQueue();

        verify(() => transmitter.transmit(any())).called(1);
      },
    );
  });

  group('Behavior trustState guard behaves correctly', () {
    test(
      'Behavior trustState guard fails a request whose required state does not match, without transmitting',
      () async {
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.pairingRequest,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.pairingStatus,
            policy: _retrySafeUnpairedPolicy,
          ),
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException e) => e.message,
              'message',
              contains('required trust state'),
            ),
          ),
        );
        verifyNever(() => transmitter.transmit(any()));
      },
    );

    test(
      'Behavior trustState guard fails a trusted request from an unpaired session, without transmitting',
      () async {
        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.pairingRequest,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.pairingStatus,
            policy: buildRequestPolicy(
              requiredTrustState: DovahLinkTrustState.trusted,
            ),
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transmitter.transmit(any()));
      },
    );

    test(
      'Behavior trustState guard fails a required state when no session trust exists, without transmitting',
      () async {
        when(() => sessionService.currentTrustState).thenReturn(null);

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.pairingRequest,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.pairingStatus,
            policy: _retrySafeUnpairedPolicy,
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transmitter.transmit(any()));
      },
    );

    test(
      'Behavior trustState guard allows a request whose required state matches',
      () async {
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);

        unawaited(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: buildRequestPolicy(
              retrySafe: false,
              requiredTrustState: DovahLinkTrustState.trusted,
              timeoutClass: TimeoutClass.normal,
            ),
          ),
        );
        await pumpEventQueue();

        verify(() => transmitter.transmit(any())).called(1);
      },
    );

    test(
      'Behavior trustState guard allows a request with no required state before admission',
      () async {
        when(() => sessionService.currentTrustState).thenReturn(null);

        unawaited(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
        );
        await pumpEventQueue();

        verify(() => transmitter.transmit(any())).called(1);
      },
    );
  });

  group('Method handleIncoming behaves correctly', () {
    test('Method handleIncoming forwards to MessageRouter.handleIncoming', () {
      service.handleIncoming('raw-message');

      verify(() => messageRouter.handleIncoming('raw-message')).called(1);
    });
  });

  group('Method failAll behaves correctly', () {
    test('Method failAll forwards to PendingOperationBookkeeping.failAll', () {
      const DovahLinkConnectionException reason = DovahLinkConnectionException(
        'lost',
      );

      service.failAll(reason, orphanRetrySafeOperations: true);

      verify(
        () => bookkeeping.failAll(reason, orphanRetrySafeOperations: true),
      ).called(1);
    });
  });

  group('Method retryOrphanedOperations behaves correctly', () {
    test('Method retryOrphanedOperations is a no-op with nothing orphaned', () {
      when(() => bookkeeping.takeOrphaned()).thenReturn(<PendingOperation>[]);

      service.retryOrphanedOperations();

      verifyNever(() => transmitter.transmit(any()));
    });

    test(
      'Method retryOrphanedOperations retransmits an operation whose policy has no requiredTrustState',
      () {
        final PendingOperation operation = buildPendingOperation(
          policy: _retrySafeAnyTrustPolicy,
        );
        when(
          () => bookkeeping.takeOrphaned(),
        ).thenReturn(<PendingOperation>[operation]);

        service.retryOrphanedOperations();

        verify(() => transmitter.transmit(operation)).called(1);
        expect(operation.hasRetried, isTrue);
      },
    );

    test(
      'Method retryOrphanedOperations fails without retransmission when the new session no longer satisfies '
      'requiredTrustState',
      () {
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        final PendingOperation operation = buildPendingOperation(
          policy: _retrySafeUnpairedPolicy,
        );
        when(
          () => bookkeeping.takeOrphaned(),
        ).thenReturn(<PendingOperation>[operation]);

        service.retryOrphanedOperations();

        verifyNever(() => transmitter.transmit(operation));
        expect(operation.hasRetried, isFalse);
        expectLater(
          operation.completer.future,
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );

    test(
      'Method retryOrphanedOperations retransmits an orphaned operation while reconnecting, '
      'bypassing the sendAndAwait connectionState guard',
      () {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.reconnecting);
        final PendingOperation operation = buildPendingOperation(
          policy: _retrySafeAnyTrustPolicy,
        );
        when(
          () => bookkeeping.takeOrphaned(),
        ).thenReturn(<PendingOperation>[operation]);

        service.retryOrphanedOperations();

        verify(() => transmitter.transmit(operation)).called(1);
        expect(operation.hasRetried, isTrue);
      },
    );

    test(
      'Method retryOrphanedOperations retries every orphaned operation, not just the first',
      () {
        final PendingOperation first = buildPendingOperation(
          policy: _retrySafeAnyTrustPolicy,
        );
        final PendingOperation second = buildPendingOperation(
          messageType: ProtocolMessageType.pairingCancel,
          policy: _retrySafeAnyTrustPolicy,
        );
        when(
          () => bookkeeping.takeOrphaned(),
        ).thenReturn(<PendingOperation>[first, second]);

        service.retryOrphanedOperations();

        verify(() => transmitter.transmit(first)).called(1);
        verify(() => transmitter.transmit(second)).called(1);
      },
    );
  });
}
