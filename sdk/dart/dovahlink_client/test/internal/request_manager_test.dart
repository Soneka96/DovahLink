import 'dart:async';
import 'dart:convert';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Mock transport used to isolate request-manager tests from socket I/O.
class MockDovahLinkTransport extends Mock implements DovahLinkTransport {}

/// Mock session context used to control session identity and trust state.
class MockSessionContext extends Mock implements SessionContext {}

/// Mock lifecycle reporter used to capture timeout and send-failure notifications.
class MockConnectionLifecycleReporter extends Mock
    implements ConnectionLifecycleReporter {}

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

const RequestPolicy _retrySafeUnpairedPolicy = RequestPolicy(
  retrySafe: true,
  requiredTrustState: DovahLinkTrustState.unpaired,
  timeoutClass: TimeoutClass.short,
);

const RequestPolicy _nonRetrySafePolicy = RequestPolicy(
  retrySafe: false,
  requiredTrustState: null,
  timeoutClass: TimeoutClass.normal,
);

/// Runs request-manager behavior tests.
void main() {
  late MockDovahLinkTransport transport;
  late MockSessionContext sessionContext;
  late MockConnectionLifecycleReporter reporter;

  setUpAll(() {
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
  });

  setUp(() {
    transport = MockDovahLinkTransport();
    sessionContext = MockSessionContext();
    reporter = MockConnectionLifecycleReporter();
    when(() => sessionContext.currentSessionId).thenReturn('session-1');
    when(
      () => sessionContext.currentTrustState,
    ).thenReturn(DovahLinkTrustState.unpaired);
    when(() => transport.send(any())).thenAnswer((_) async {});
  });

  /// Builds a request manager using the current test doubles and timeout policy.
  RequestManager buildManager({
    Map<TimeoutClass, Duration> timeoutDurations = _longTimeouts,
  }) => RequestManager(
    transport: transport,
    timeoutDurations: timeoutDurations,
    sessionContext: sessionContext,
    reporter: reporter,
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
        final RequestManager manager = buildManager();

        unawaited(
          manager.sendAndAwait(
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
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
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
        final bool resolved = manager.resolveReply(messageId, reply);

        expect(resolved, isTrue);
        expect(await pending, same(reply));
      },
    );

    test(
      'Method sendAndAwait throws DovahLinkProtocolException for a wire error reply',
      () async {
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        manager.resolveReply(
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
      'Method sendAndAwait translates a malformed wire error payload into malformed_message',
      () async {
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        manager.resolveReply(
          messageId,
          Envelope(
            messageType: ProtocolMessageType.error,
            messageId: 'reply-1',
            sessionId: 'session-1',
            correlationId: messageId,
            payload: const <String, dynamic>{
              'code': 'future_error_code',
              'message': 'malformed code',
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
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException e) => e.code,
                  'code',
                  ProtocolErrorCode.malformedMessage,
                )
                .having(
                  (DovahLinkProtocolException e) => e.retryable,
                  'retryable',
                  isFalse,
                ),
          ),
        );
      },
    );

    test(
      'Method sendAndAwait throws DovahLinkProtocolException carrying retryable: true for a retryable wire error',
      () async {
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        manager.resolveReply(
          messageId,
          Envelope(
            messageType: ProtocolMessageType.error,
            messageId: 'reply-1',
            sessionId: 'session-1',
            correlationId: messageId,
            payload: const <String, dynamic>{
              'code': 'rate_limited',
              'message': 'try again',
              'retryable': true,
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
              (DovahLinkProtocolException e) => e.retryable,
              'retryable',
              isTrue,
            ),
          ),
        );
      },
    );

    test(
      'Method sendAndAwait throws DovahLinkProtocolException(unexpected_message_type) for a mismatched reply',
      () async {
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        manager.resolveReply(
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
        final RequestManager manager = buildManager(
          timeoutDurations: _shortTimeouts,
        );

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        // Silence the "unhandled" warning for a Future this test deliberately never awaits --
        // the timeout path never completes it, by design.
        pending.ignore();

        await Future<void>.delayed(const Duration(milliseconds: 60));

        final List<Exception> reported = verify(
          () => reporter.onUnhealthy(captureAny()),
        ).captured.cast<Exception>();
        expect(reported, hasLength(1));
        expect(reported.single, isA<DovahLinkConnectionException>());
      },
    );

    test(
      'Method sendAndAwait a reply that arrives after the timer fires but before failAll runs still resolves '
      'the operation -- RequestManager itself does not guard this race; that is whichever '
      'caller sequences onUnhealthy and failAll together',
      () async {
        final RequestManager manager = buildManager(
          timeoutDurations: _shortTimeouts,
        );

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        final String messageId = sentMessageId();

        await Future<void>.delayed(const Duration(milliseconds: 60));
        verify(() => reporter.onUnhealthy(any())).called(1);

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
        final bool resolved = manager.resolveReply(messageId, reply);

        expect(resolved, isTrue);
        expect(await pending, same(reply));
      },
    );

    test(
      'Method sendAndAwait reports onUnhealthy when the transport send fails',
      () async {
        when(
          () => transport.send(any()),
        ).thenAnswer((_) async => throw StateError('socket closed'));
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        pending.ignore();
        await pumpEventQueue();

        final List<Exception> reported = verify(
          () => reporter.onUnhealthy(captureAny()),
        ).captured.cast<Exception>();
        expect(reported, hasLength(1));
        expect(reported.single, isA<DovahLinkConnectionException>());
      },
    );
  });

  group('Method resolveReply behaves correctly', () {
    test(
      'Method resolveReply returns false when no pending operation matches',
      () {
        final RequestManager manager = buildManager();

        final bool resolved = manager.resolveReply(
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
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.hello,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.helloAck,
          policy: _nonRetrySafePolicy,
        );
        await pumpEventQueue();

        manager.failAll(
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
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();

        manager.failAll(
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
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();
        verify(() => transport.send(any())).called(1);

        manager.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        manager.retryOrphanedOperations();
        await pumpEventQueue();

        // Only the retry's send is unverified at this point -- the original send was already
        // verified above, so this captures exactly the retransmission.
        final String retryMessageId = sentMessageId();

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
        manager.resolveReply(retryMessageId, reply);

        expect(await pending, same(reply));
      },
    );

    test(
      'Method failAll retransmits with the live sessionId, not a stale snapshot',
      () async {
        final RequestManager manager = buildManager();

        manager
            .sendAndAwait(
              messageType: ProtocolMessageType.pairingRequest,
              payload: const <String, dynamic>{},
              expectedType: ProtocolMessageType.pairingStatus,
              policy: _retrySafeUnpairedPolicy,
            )
            .ignore();
        await pumpEventQueue();
        verify(() => transport.send(any())).called(1);

        manager.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        // A new session was admitted by the time of retry -- currentSessionId changed.
        when(() => sessionContext.currentSessionId).thenReturn('session-2');
        manager.retryOrphanedOperations();
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
        final RequestManager manager = buildManager();

        expect(
          () => manager.failAll(
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
        final RequestManager manager = buildManager();

        final Future<Envelope> first = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _nonRetrySafePolicy,
        );
        final Future<Envelope> second = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingCancel,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingOutcome,
          policy: _nonRetrySafePolicy,
        );
        await pumpEventQueue();

        manager.failAll(
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
      final RequestManager manager = buildManager();

      manager.retryOrphanedOperations();

      verifyNever(() => transport.send(any()));
    });

    test(
      'Method retryOrphanedOperations fails without retransmission when the new session no longer satisfies '
      'requiredTrustState',
      () async {
        final RequestManager manager = buildManager();

        final Future<Envelope> pending = manager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: _retrySafeUnpairedPolicy,
        );
        await pumpEventQueue();

        manager.failAll(
          const DovahLinkConnectionException('lost'),
          orphanRetrySafeOperations: true,
        );
        when(
          () => sessionContext.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        manager.retryOrphanedOperations();

        await expectLater(
          pending,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        // Only the original send -- the mismatched retry never retransmits.
        verify(() => transport.send(any())).called(1);
      },
    );

    test('Method retryOrphanedOperations retries at most once: a second orphaning of an already-retried operation fails it '
        'immediately instead of retrying again', () async {
      final RequestManager manager = buildManager();

      final Future<Envelope> pending = manager.sendAndAwait(
        messageType: ProtocolMessageType.pairingRequest,
        payload: const <String, dynamic>{},
        expectedType: ProtocolMessageType.pairingStatus,
        policy: _retrySafeUnpairedPolicy,
      );
      await pumpEventQueue();

      manager.failAll(
        const DovahLinkConnectionException('first loss'),
        orphanRetrySafeOperations: true,
      );
      manager.retryOrphanedOperations();
      await pumpEventQueue();
      // Both the original send and the one retry so far -- neither previously verified.
      verify(() => transport.send(any())).called(2);

      manager.failAll(
        const DovahLinkConnectionException('second loss'),
        orphanRetrySafeOperations: true,
      );

      await expectLater(pending, throwsA(isA<DovahLinkConnectionException>()));
      // No further retransmission was attempted for the already-retried operation.
      verifyNever(() => transport.send(any()));
    });
  });
}
