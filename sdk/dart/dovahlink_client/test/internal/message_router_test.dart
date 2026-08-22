import 'dart:convert';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Mock request manager used to isolate message routing tests.
class MockRequestManager extends Mock implements RequestManager {}

/// Mock lifecycle reporter used to capture router decisions.
class MockConnectionLifecycleReporter extends Mock
    implements ConnectionLifecycleReporter {}

/// Fake envelope used to register mocktail fallbacks.
class FakeEnvelope extends Fake implements Envelope {}

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

void main() {
  late MockRequestManager requestManager;
  late MockConnectionLifecycleReporter reporter;
  late MessageRouter router;

  setUpAll(() {
    registerFallbackValue(FakeEnvelope());
    registerFallbackValue(Exception('fallback for any()'));
    registerFallbackValue(AdministrativeInvalidationReason.revoked);
  });

  setUp(() {
    requestManager = MockRequestManager();
    reporter = MockConnectionLifecycleReporter();
    router = MessageRouter(requestManager: requestManager, reporter: reporter);
  });

  group('Method handleIncoming behaves correctly', () {
    test(
      'Method handleIncoming resolves a correlated reply through RequestManager',
      () {
        when(() => requestManager.resolveReply(any(), any())).thenReturn(true);

        router.handleIncoming(
          rawEnvelope(
            messageType: 'pairing_status',
            payload: const <String, dynamic>{'state': 'unavailable'},
            correlationId: 'message-outgoing-1',
          ),
        );

        final Envelope resolved =
            verify(
                  () => requestManager.resolveReply(
                    captureAny(that: equals('message-outgoing-1')),
                    captureAny(),
                  ),
                ).captured.last
                as Envelope;
        expect(resolved.messageType, ProtocolMessageType.pairingStatus);
        expect(resolved.correlationId, 'message-outgoing-1');
        expect(resolved.payload, <String, dynamic>{'state': 'unavailable'});
        expect(resolved.sessionId, 'session-1');
        expect(resolved.bridgeInstanceId, 'bridge-1');
        verifyNever(
          () => reporter.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handleIncoming reports a protocol violation when no pending operation matches the correlationId',
      () {
        when(() => requestManager.resolveReply(any(), any())).thenReturn(false);

        router.handleIncoming(
          rawEnvelope(
            messageType: 'pairing_status',
            payload: const <String, dynamic>{'state': 'unavailable'},
            correlationId: 'unknown-id',
          ),
        );

        final VerificationResult verification = verify(
          () => reporter.onProtocolViolation(
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
      'Method handleIncoming reports a protocol violation for malformed JSON',
      () {
        router.handleIncoming('not valid json');

        final VerificationResult verification = verify(
          () => reporter.onProtocolViolation(
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
        verifyNever(() => requestManager.resolveReply(any(), any()));
      },
    );

    test(
      'Method handleIncoming reports a protocol violation for valid JSON that is not a well-formed envelope',
      () {
        // Valid JSON, but missing every required Envelope field.
        router.handleIncoming(jsonEncode(<String, dynamic>{}));

        final VerificationResult verification = verify(
          () => reporter.onProtocolViolation(
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
        verifyNever(() => requestManager.resolveReply(any(), any()));
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
          () => reporter.onProtocolViolation(
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
        verifyNever(() => requestManager.resolveReply(any(), any()));
        verifyNever(() => reporter.onSessionInvalidated(any()));
      },
    );

    test(
      'Method handleIncoming routes a well-formed session_invalidated push to onSessionInvalidated',
      () {
        router.handleIncoming(
          rawEnvelope(
            messageType: 'session_invalidated',
            payload: const <String, dynamic>{'reason': 'revoked'},
          ),
        );

        verify(
          () => reporter.onSessionInvalidated(
            AdministrativeInvalidationReason.revoked,
          ),
        ).called(1);
        verifyNever(
          () => reporter.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
        verifyNever(() => requestManager.resolveReply(any(), any()));
      },
    );
  });
}
