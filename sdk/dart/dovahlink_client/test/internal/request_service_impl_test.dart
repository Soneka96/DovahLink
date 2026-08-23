import 'dart:async';
import 'dart:convert';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/request_service_impl.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Mock transport used to isolate request-service tests from socket I/O.
class MockDovahLinkTransport extends Mock implements DovahLinkTransport {}

/// Mock session service used to control connection state, identity, and trust state, and to
/// capture reactive report forwarding, per `ai/context/sdk/testing.md`'s "Service test
/// boundaries".
class MockSessionService extends Mock implements SessionService {}

/// Short, real (non-faked) timeout durations for tests that exercise the timer path -- matches
/// this test suite's existing convention (`dovahlink_client_test.dart`'s "request policy: timeout"
/// group) of real millisecond timers rather than a fake clock.
const Map<TimeoutClass, Duration> _shortTimeouts = <TimeoutClass, Duration>{
  TimeoutClass.short: Duration(milliseconds: 20),
  TimeoutClass.normal: Duration(milliseconds: 20),
  TimeoutClass.heavy: Duration(milliseconds: 20),
};

/// A generous, real timeout unlikely to fire during a fast-resolving test.
const Map<TimeoutClass, Duration> _longTimeouts = <TimeoutClass, Duration>{
  TimeoutClass.short: Duration(seconds: 30),
  TimeoutClass.normal: Duration(seconds: 30),
  TimeoutClass.heavy: Duration(seconds: 30),
};

/// Retry-safe policy for operations that require an admitted unpaired session.
const RequestPolicy _retrySafeUnpairedPolicy = RequestPolicy(
  retrySafe: true,
  requiredTrustState: DovahLinkTrustState.unpaired,
  timeoutClass: TimeoutClass.short,
);

/// Non-retry-safe policy for operations whose lost response makes retransmission ambiguous.
const RequestPolicy _nonRetrySafePolicy = RequestPolicy(
  retrySafe: false,
  requiredTrustState: null,
  timeoutClass: TimeoutClass.normal,
);

/// Retry-safe policy for operations with no trust-state requirement to revalidate on retry.
const RequestPolicy _retrySafeAnyTrustPolicy = RequestPolicy(
  retrySafe: true,
  requiredTrustState: null,
  timeoutClass: TimeoutClass.short,
);

/// Builds one raw wire envelope as sent by the bridge.
String rawEnvelope({
  required String messageType,
  required JsonMap payload,
  String? correlationId,
}) => jsonEncode(<String, dynamic>{
  'messageType': messageType,
  'messageId': 'message-1',
  'sessionId': 'session-1',
  'correlationId': correlationId,
  'payload': payload,
  'bridgeInstanceId': 'bridge-1',
  'playContextId': null,
  'clientId': null,
});

/// Runs request-service behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockSessionService sessionService;

  setUpAll(() {
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
    registerFallbackValue(
      const ErrorPayload(
        code: ProtocolErrorCode.malformedMessage,
        message: 'fallback for any()',
        retryable: false,
      ),
    );
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    sessionService = MockSessionService();
    when(() => sessionService.currentSessionId).thenReturn('session-1');
    when(
      () => sessionService.currentTrustState,
    ).thenReturn(DovahLinkTrustState.unpaired);
    when(
      () => sessionService.connectionState,
    ).thenReturn(DovahLinkConnectionState.connected);
    when(() => transport.send(any())).thenAnswer((_) async {});
  });

  /// Builds a request service using the current test doubles and timeout policy.
  RequestServiceImpl buildService({
    Map<TimeoutClass, Duration> timeoutDurations = _longTimeouts,
  }) => RequestServiceImpl(
    transport: transport,
    timeoutDurations: timeoutDurations,
    sessionService: sessionService,
  );

  /// Extracts the `messageId` of the single envelope sent so far.
  String sentMessageId() {
    final JsonMap sent =
        jsonDecode(verify(() => transport.send(captureAny())).captured.single)
            as JsonMap;
    return sent['messageId'] as String;
  }

  group('Method sendAndAwait behaves correctly', () {
    test(
      'Method sendAndAwait stamps the outgoing envelope with the current sessionId',
      () async {
        final RequestServiceImpl service = buildService();

        unawaited(
          service.sendAndAwait(
            messageType: ProtocolMessageType.pairingRequest,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.pairingStatus,
            policy: _retrySafeUnpairedPolicy,
          ),
        );
        await pumpEventQueue();

        final JsonMap sent =
            jsonDecode(
                  verify(() => transport.send(captureAny())).captured.single,
                )
                as JsonMap;
        expect(sent['messageType'], 'pairing_request');
        expect(sent['sessionId'], 'session-1');
        expect(sent['payload'], <String, dynamic>{});
      },
    );

    test(
      'Method sendAndAwait resolves with the correlated reply envelope',
      () async {
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        final Envelope reply = Envelope(
          messageType: ProtocolMessageType.pairingStatus,
          messageId: 'reply-1',
          sessionId: 'session-1',
          correlationId: messageId,
          payload: const <String, dynamic>{'state': 'unavailable'},
          bridgeInstanceId: 'bridge-1',
          playContextId: null,
          clientId: 'client-1',
        );
        final bool resolved = service.resolveReply(messageId, reply);

        expect(resolved, isTrue);
        expect(await pending, same(reply));
      },
    );

    test(
      'Method sendAndAwait throws DovahLinkProtocolException for a wire error reply',
      () async {
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        service.resolveReply(
          messageId,
          Envelope(
            messageType: ProtocolMessageType.error,
            messageId: 'reply-1',
            sessionId: 'session-1',
            correlationId: messageId,
            payload: const <String, dynamic>{
              'code': 'unauthenticated',
              'message': 'nope',
              'retryable': false,
            },
            bridgeInstanceId: 'bridge-1',
            playContextId: null,
            clientId: 'client-1',
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
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        service.resolveReply(
          messageId,
          Envelope(
            messageType: ProtocolMessageType.pairingOutcome,
            messageId: 'reply-1',
            sessionId: 'session-1',
            correlationId: messageId,
            payload: const <String, dynamic>{},
            bridgeInstanceId: 'bridge-1',
            playContextId: null,
            clientId: 'client-1',
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

    test(
      'Method sendAndAwait reports onUnhealthy and never resolves on timeout',
      () async {
        final RequestServiceImpl service = buildService(
          timeoutDurations: _shortTimeouts,
        );

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        // Silence the "unhandled" warning for a Future this test deliberately does not await.
        pending.ignore();

        await Future<void>.delayed(const Duration(milliseconds: 60));

        final List<Exception> reported = verify(
          () => sessionService.onUnhealthy(captureAny()),
        ).captured.cast<Exception>();
        expect(reported, hasLength(1));
        expect(reported.single, isA<DovahLinkConnectionException>());
      },
    );

    test(
      'Method sendAndAwait reports onUnhealthy when the transport send fails',
      () async {
        when(
          () => transport.send(any()),
        ).thenAnswer((_) async => throw StateError('socket closed'));
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        pending.ignore();
        await pumpEventQueue();

        final List<Exception> reported = verify(
          () => sessionService.onUnhealthy(captureAny()),
        ).captured.cast<Exception>();
        expect(reported, hasLength(1));
        expect(reported.single, isA<DovahLinkConnectionException>());
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
        final RequestServiceImpl service = buildService();

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transport.send(any()));
      },
    );

    test(
      'Behavior connectionState guard fails a request immediately when administratively invalidated',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.administrativelyInvalidated);
        final RequestServiceImpl service = buildService();

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transport.send(any()));
      },
    );

    test(
      'Behavior connectionState guard fails a request immediately while a connect attempt is still in flight',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connecting);
        final RequestServiceImpl service = buildService();

        await expectLater(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => transport.send(any()));
      },
    );

    test(
      'Behavior connectionState guard allows a request while reconnecting',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.reconnecting);
        final RequestServiceImpl service = buildService();

        unawaited(
          service.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.helloAck,
            policy: _nonRetrySafePolicy,
          ),
        );
        await pumpEventQueue();

        verify(() => transport.send(any())).called(1);
      },
    );
  });

  group('Method handleIncoming behaves correctly', () {
    test(
      'Method handleIncoming resolves a correlated reply awaited through sendAndAwait',
      () async {
        final RequestServiceImpl service = buildService();
        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        service.handleIncoming(
          rawEnvelope(
            messageType: 'pairing_status',
            payload: const <String, dynamic>{'state': 'unavailable'},
            correlationId: messageId,
          ),
        );

        final Envelope result = await pending;
        expect(result.correlationId, messageId);
        verifyNever(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handleIncoming forwards a decode failure to onProtocolViolation',
      () {
        final RequestServiceImpl service = buildService();

        service.handleIncoming('not valid json');

        final VerificationResult verification = verify(
          () => sessionService.onProtocolViolation(
            captureAny(),
            orphanRetrySafeOperations: captureAny(
              named: 'orphanRetrySafeOperations',
            ),
          ),
        );
        expect(verification.captured[0], isA<DovahLinkProtocolException>());
        expect(
          (verification.captured[0] as DovahLinkProtocolException).code,
          ProtocolErrorCode.malformedMessage,
        );
        expect(verification.captured[1], isFalse);
      },
    );

    test(
      'Method handleIncoming forwards an unmatched correlation to onProtocolViolation',
      () {
        final RequestServiceImpl service = buildService();

        service.handleIncoming(
          rawEnvelope(
            messageType: 'pairing_status',
            payload: const <String, dynamic>{'state': 'unavailable'},
            correlationId: 'unknown-id',
          ),
        );

        final VerificationResult verification = verify(
          () => sessionService.onProtocolViolation(
            captureAny(),
            orphanRetrySafeOperations: captureAny(
              named: 'orphanRetrySafeOperations',
            ),
          ),
        );
        expect(verification.captured[0], isA<DovahLinkProtocolException>());
        expect(
          (verification.captured[0] as DovahLinkProtocolException).code,
          ProtocolErrorCode.malformedMessage,
        );
        expect(verification.captured[1], isFalse);
      },
    );

    test(
      'Method handleIncoming routes a well-formed session_invalidated push to onSessionInvalidated',
      () {
        final RequestServiceImpl service = buildService();

        service.handleIncoming(
          rawEnvelope(
            messageType: 'session_invalidated',
            payload: const <String, dynamic>{'reason': 'revoked'},
          ),
        );

        verify(
          () => sessionService.onSessionInvalidated(
            AdministrativeInvalidationReason.revoked,
          ),
        ).called(1);
      },
    );

    test(
      'Method handleIncoming routes a well-formed unsolicited error push to onUnsolicitedError',
      () {
        final RequestServiceImpl service = buildService();

        service.handleIncoming(
          rawEnvelope(
            messageType: 'error',
            payload: const <String, dynamic>{
              'code': 'rate_limited',
              'message': 'slow down',
              'retryable': true,
            },
          ),
        );

        final VerificationResult verification = verify(
          () => sessionService.onUnsolicitedError(captureAny()),
        );
        verification.called(1);
        final ErrorPayload error = verification.captured.single as ErrorPayload;
        expect(error.code, ProtocolErrorCode.rateLimited);
        expect(error.retryable, isTrue);
      },
    );
  });

  group('Method resolveReply behaves correctly', () {
    test(
      'Method resolveReply returns false when no pending operation matches',
      () {
        final RequestServiceImpl service = buildService();

        final bool resolved = service.resolveReply(
          'no-such-id',
          const Envelope(
            messageType: ProtocolMessageType.pairingStatus,
            messageId: 'reply-1',
            sessionId: 'session-1',
            correlationId: 'no-such-id',
            payload: <String, dynamic>{},
            bridgeInstanceId: 'bridge-1',
            playContextId: null,
            clientId: 'client-1',
          ),
        );

        expect(resolved, isFalse);
      },
    );
  });

  group('Method failAll behaves correctly', () {
    test(
      'Method failAll fails a non-retrySafe operation immediately even when orphaning is requested',
      () async {
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.hello,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.helloAck,
          policy: _nonRetrySafePolicy,
        );
        await pumpEventQueue();

        service.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );

    test(
      'Method failAll fails a retrySafe operation immediately when not orphaning',
      () async {
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();

        service.failAll(
          const DovahLinkConnectionException('protocol violation'),
          orphanRetrySafeOperations: false,
        );

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
      },
    );

    test(
      'Method failAll orphans a retrySafe operation instead of failing it, and retryOrphanedOperations '
      'retransmits it',
      () async {
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String initialMessageId = sentMessageId();

        service.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        service.retryOrphanedOperations();
        await pumpEventQueue();

        // Only the retry's send is unverified at this point -- the original send was already
        // verified above, so this captures exactly the retransmission.
        final String retryMessageId = sentMessageId();
        expect(retryMessageId, isNot(initialMessageId));

        final Envelope reply = Envelope(
          messageType: ProtocolMessageType.pairingStatus,
          messageId: 'reply-1',
          sessionId: 'session-1',
          correlationId: retryMessageId,
          payload: const <String, dynamic>{'state': 'unavailable'},
          bridgeInstanceId: 'bridge-1',
          playContextId: null,
          clientId: 'client-1',
        );
        service.resolveReply(retryMessageId, reply);

        expect(await pending, same(reply));
      },
    );

    test(
      'Method failAll fails an already-orphaned operation too when a later call does not orphan',
      () async {
        final RequestServiceImpl service = buildService();
        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        service.failAll(
          const DovahLinkConnectionException('first loss'),
          orphanRetrySafeOperations: true,
        );

        service.failAll(
          const DovahLinkConnectionException('second loss'),
          orphanRetrySafeOperations: false,
        );

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        // The already-orphaned operation was failed, not retransmitted.
        verify(() => transport.send(any())).called(1);
      },
    );

    test(
      'Method failAll retransmits with the live sessionId, not a stale snapshot',
      () async {
        final RequestServiceImpl service = buildService();

        service
            .sendAndAwait(
              messageType: ProtocolMessageType.pairingRequest,
              payload: const <String, dynamic>{},
              expectedType: ProtocolMessageType.pairingStatus,
              policy: _retrySafeUnpairedPolicy,
            )
            .ignore();
        await pumpEventQueue();
        verify(() => transport.send(any())).called(1);

        service.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        // A new session was admitted by the time of retry -- currentSessionId changed.
        when(() => sessionService.currentSessionId).thenReturn('session-2');
        service.retryOrphanedOperations();
        await pumpEventQueue();

        final JsonMap sent =
            jsonDecode(
                  verify(() => transport.send(captureAny())).captured.single,
                )
                as JsonMap;
        expect(sent['sessionId'], 'session-2');
      },
    );

    test(
      'Method failAll is a no-op with no pending or orphaned operations',
      () {
        final RequestServiceImpl service = buildService();

        expect(
          () => service.failAll(
            const DovahLinkConnectionException('nothing to fail'),
            orphanRetrySafeOperations: true,
          ),
          returnsNormally,
        );
        verifyNever(() => transport.send(any()));
      },
    );

    test(
      'Method failAll fails every pending operation, not just the first',
      () async {
        final RequestServiceImpl service = buildService();

        final Future<Envelope> first = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _nonRetrySafePolicy,
        );
        final Future<Envelope> second = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingCancel,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingOutcome,
          policy: _nonRetrySafePolicy,
        );
        await pumpEventQueue();

        service.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );

        await expectLater(first, throwsA(isA<DovahLinkConnectionException>()));
        await expectLater(second, throwsA(isA<DovahLinkConnectionException>()));
      },
    );
  });

  group('Method retryOrphanedOperations behaves correctly', () {
    test('Method retryOrphanedOperations is a no-op with nothing orphaned', () {
      final RequestServiceImpl service = buildService();

      service.retryOrphanedOperations();

      verifyNever(() => transport.send(any()));
    });

    test(
      'Method retryOrphanedOperations fails without retransmission when the new session no longer satisfies '
      'requiredTrustState',
      () async {
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();

        service.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        service.retryOrphanedOperations();

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        // Only the original send -- the mismatched retry never retransmits.
        verify(() => transport.send(any())).called(1);
      },
    );

    test(
      'Method retryOrphanedOperations retransmits regardless of trust-state change when the policy has no '
      'requiredTrustState',
      () async {
        final RequestServiceImpl service = buildService();

        final Future<Envelope> pending = service.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeAnyTrustPolicy,
        );
        await pumpEventQueue();

        service.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        service.retryOrphanedOperations();
        await pumpEventQueue();

        // The original send plus the retransmission.
        verify(() => transport.send(any())).called(2);
        pending.ignore();
      },
    );

    test('Method retryOrphanedOperations retries at most once: a second orphaning of an already-retried '
        'operation fails it immediately instead of retrying again', () async {
      final RequestServiceImpl service = buildService();

      final Future<Envelope> pending = service.sendAndAwait(
        messageType: ProtocolMessageType.pairingRequest,
        payload: const <String, dynamic>{},
        expectedType: ProtocolMessageType.pairingStatus,
        policy: _retrySafeUnpairedPolicy,
      );
      await pumpEventQueue();

      service.failAll(
        const DovahLinkConnectionException('first loss'),
        orphanRetrySafeOperations: true,
      );
      service.retryOrphanedOperations();
      await pumpEventQueue();
      // Both the original send and the one retry so far -- neither previously verified.
      verify(() => transport.send(any())).called(2);

      service.failAll(
        const DovahLinkConnectionException('second loss'),
        orphanRetrySafeOperations: true,
      );

      await expectLater(pending, throwsA(isA<DovahLinkConnectionException>()));
      // No further retransmission was attempted for the already-retried operation.
      verifyNever(() => transport.send(any()));
    });
  });
}
