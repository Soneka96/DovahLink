import 'dart:convert';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'mock_unsolicited_message_handler.dart';

/// Mock pending-operation bookkeeping used to isolate message routing tests, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockPendingOperationBookkeeping extends Mock
    implements PendingOperationBookkeeping {}

/// Mock session service used to capture router decisions.
class MockSessionService extends Mock implements ISessionService {}

/// Fake envelope used to register mocktail fallbacks.
class FakeEnvelope extends Fake implements Envelope {}

/// Builds one raw wire envelope as sent by the host.
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
  if (<String>{
    'hello_ack',
    'state_snapshot',
    'state_event',
  }.contains(messageType))
    'stateAuthorityId': 'state-authority-1',
  'playContextId': null,
  'clientId':
      <String>{
        'pairing_request',
        'pairing_confirm',
        'pairing_ack',
        'pairing_renotify',
        'pairing_cancel',
        'rename_request',
        'subscribe',
        'snapshot_request',
        'ping',
      }.contains(messageType)
      ? 'client-1'
      : null,
});

/// Runs message-router behavior tests.
void main() {
  late MockPendingOperationBookkeeping bookkeeping;
  late MockSessionService sessionService;
  late MockUnsolicitedMessageHandler unsolicitedMessageHandler;
  late MessageRouter router;

  setUpAll(() {
    registerFallbackValue(FakeEnvelope());
    registerFallbackValue(Exception('fallback for any()'));
    registerFallbackValue(AdministrativeInvalidationReason.revoked);
  });

  setUp(() {
    bookkeeping = MockPendingOperationBookkeeping();
    sessionService = MockSessionService();
    unsolicitedMessageHandler = MockUnsolicitedMessageHandler();
    when(() => unsolicitedMessageHandler.handle(any())).thenAnswer((_) {});
    router = MessageRouter(
      bookkeeping: bookkeeping,
      sessionService: sessionService,
      unsolicitedMessageHandler: unsolicitedMessageHandler,
    );
  });

  group('Method handleIncoming behaves correctly', () {
    test(
      'Method handleIncoming resolves a correlated reply through PendingOperationBookkeeping',
      () {
        when(() => bookkeeping.resolveReply(any(), any())).thenReturn(true);

        router.handleIncoming(
          rawEnvelope(
            messageType: 'pairing_status',
            payload: const <String, dynamic>{'state': 'unavailable'},
            correlationId: 'message-outgoing-1',
          ),
        );

        final Envelope resolved =
            verify(
                  () => bookkeeping.resolveReply(
                    captureAny(that: equals('message-outgoing-1')),
                    captureAny(),
                  ),
                ).captured.last
                as Envelope;
        expect(resolved.messageType, ProtocolMessageType.pairingStatus);
        expect(resolved.correlationId, 'message-outgoing-1');
        expect(resolved.payload, <String, dynamic>{'state': 'unavailable'});
        expect(resolved.sessionId, 'session-1');
        expect(resolved.stateAuthorityId, isNull);
        verifyNever(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handleIncoming reports a protocol violation when no pending operation matches the correlationId',
      () {
        when(() => bookkeeping.resolveReply(any(), any())).thenReturn(false);

        router.handleIncoming(
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
        verifyNever(() => unsolicitedMessageHandler.handle(any()));
      },
    );

    test(
      'Method handleIncoming resolves a matched state snapshot as a pending reply',
      () {
        when(() => bookkeeping.resolveReply(any(), any())).thenReturn(true);

        router.handleIncoming(
          rawEnvelope(
            messageType: 'state_snapshot',
            payload: const <String, dynamic>{
              'stateArea': 'character_health',
              'revision': 2,
              'occurredAt': '2026-09-23T12:00:00Z',
              'data': <String, dynamic>{'value': 90.0},
            },
            correlationId: 'pending-snapshot-request',
          ),
        );

        final Envelope resolved =
            verify(
                  () => bookkeeping.resolveReply(
                    'pending-snapshot-request',
                    captureAny(),
                  ),
                ).captured.single
                as Envelope;
        expect(resolved.messageType, ProtocolMessageType.stateSnapshot);
        verifyNever(() => unsolicitedMessageHandler.handle(any()));
        verifyNever(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handleIncoming routes an unmatched correlated state snapshot',
      () {
        when(() => bookkeeping.resolveReply(any(), any())).thenReturn(false);

        router.handleIncoming(
          rawEnvelope(
            messageType: 'state_snapshot',
            payload: const <String, dynamic>{
              'stateArea': 'character_health',
              'revision': 2,
              'occurredAt': '2026-09-23T12:00:00Z',
              'data': <String, dynamic>{'value': 90.0},
            },
            correlationId: 'completed-subscribe-request',
          ),
        );

        final Envelope routed =
            verify(
                  () => unsolicitedMessageHandler.handle(captureAny()),
                ).captured.single
                as Envelope;
        expect(routed.messageType, ProtocolMessageType.stateSnapshot);
        expect(routed.correlationId, 'completed-subscribe-request');
        verify(
          () => bookkeeping.resolveReply('completed-subscribe-request', any()),
        ).called(1);
        verifyNever(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handleIncoming reports a protocol violation for malformed JSON',
      () {
        router.handleIncoming('not valid json');

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
        expect(
          (verification.captured[0] as DovahLinkProtocolException).retryable,
          isFalse,
        );
        expect(
          (verification.captured[0] as DovahLinkProtocolException).message,
          contains('Invalid protocol envelope'),
        );
        expect(verification.captured[1], isFalse);
        verifyNever(() => bookkeeping.resolveReply(any(), any()));
      },
    );

    test(
      'Method handleIncoming reports a protocol violation for valid JSON that is not a well-formed envelope',
      () {
        // Valid JSON, but missing every required Envelope field.
        router.handleIncoming(jsonEncode(<String, dynamic>{}));

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
        expect(
          (verification.captured[0] as DovahLinkProtocolException).retryable,
          isFalse,
        );
        expect(
          (verification.captured[0] as DovahLinkProtocolException).message,
          contains('Invalid protocol envelope'),
        );
        expect(verification.captured[1], isFalse);
        verifyNever(() => bookkeeping.resolveReply(any(), any()));
      },
    );

    test(
      'Method handleIncoming reports a protocol violation for an unrecognized message type',
      () {
        router.handleIncoming(
          rawEnvelope(
            messageType: 'future_unmodeled_push',
            payload: const <String, dynamic>{},
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
        verifyNever(() => bookkeeping.resolveReply(any(), any()));
        verifyNever(() => sessionService.onSessionInvalidated(any()));
      },
    );

    test(
      'Method handleIncoming routes an unsolicited session invalidation',
      () {
        router.handleIncoming(
          rawEnvelope(
            messageType: 'session_invalidated',
            payload: const <String, dynamic>{'reason': 'revoked'},
          ),
        );

        final Envelope routed =
            verify(
                  () => unsolicitedMessageHandler.handle(captureAny()),
                ).captured.single
                as Envelope;
        expect(routed.messageType, ProtocolMessageType.sessionInvalidated);
        verifyNever(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
        verifyNever(() => bookkeeping.resolveReply(any(), any()));
      },
    );

    test('Method handleIncoming routes an unsolicited state Snapshot', () {
      router.handleIncoming(
        rawEnvelope(
          messageType: 'state_snapshot',
          payload: const <String, dynamic>{
            'stateArea': 'character_xp',
            'revision': 1,
            'occurredAt': '2026-09-23T12:00:00Z',
            'data': <String, dynamic>{'value': 42.5},
          },
        ),
      );

      final Envelope routed =
          verify(
                () => unsolicitedMessageHandler.handle(captureAny()),
              ).captured.single
              as Envelope;
      expect(routed.messageType, ProtocolMessageType.stateSnapshot);
      expect(routed.correlationId, isNull);
      verifyNever(
        () => sessionService.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
    });
  });
}
