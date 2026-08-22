import 'dart:convert';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_client_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

class MockRequestManager extends Mock implements RequestManager {}

class MockConnectionLifecycleReporter extends Mock
    implements ConnectionLifecycleReporter {}

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
  'clientId': 'client-1',
});

void main() {
  late MockRequestManager requestManager;
  late MockConnectionLifecycleReporter reporter;
  late MessageRouter router;

  setUpAll(() {
    registerFallbackValue(FakeEnvelope());
    registerFallbackValue(
      const DovahLinkConnectionException('fallback for any()'),
    );
    registerFallbackValue(AdministrativeInvalidationReason.revoked);
  });

  setUp(() {
    requestManager = MockRequestManager();
    reporter = MockConnectionLifecycleReporter();
    router = MessageRouter(requestManager: requestManager, reporter: reporter);
  });

  group('handleIncoming', () {
    test('resolves a correlated reply through RequestManager', () {
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
      expect(resolved.messageType, 'pairing_status');
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
    });

    test(
      'reports a protocol violation when no pending operation matches the correlationId',
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
          'unexpected_correlation_id',
        );
        expect(verification.captured[1], isFalse);
      },
    );

    test('reports a protocol violation for malformed JSON', () {
      router.handleIncoming('not valid json');

      final VerificationResult verification = verify(
        () => reporter.onProtocolViolation(
          captureAny(),
          orphanRetrySafeOperations: captureAny(
            named: 'orphanRetrySafeOperations',
          ),
        ),
      );
      expect(verification.captured[0], isA<DovahLinkConnectionException>());
      expect(verification.captured[1], isFalse);
      verifyNever(() => requestManager.resolveReply(any(), any()));
    });

    test(
      'reports a protocol violation for valid JSON that is not a well-formed envelope',
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
        expect(verification.captured[0], isA<DovahLinkConnectionException>());
        expect(verification.captured[1], isFalse);
        verifyNever(() => requestManager.resolveReply(any(), any()));
      },
    );

    test('ignores an unsolicited capabilities message', () {
      router.handleIncoming(
        rawEnvelope(
          messageType: 'capabilities',
          payload: const <String, dynamic>{},
        ),
      );

      verifyNever(() => requestManager.resolveReply(any(), any()));
      verifyNever(() => reporter.onSessionInvalidated(any()));
      verifyNever(
        () => reporter.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
    });

    test(
      'ignores an unsolicited message type this client does not yet model',
      () {
        router.handleIncoming(
          rawEnvelope(
            messageType: 'future_unmodeled_push',
            payload: const <String, dynamic>{},
          ),
        );

        verifyNever(() => requestManager.resolveReply(any(), any()));
        verifyNever(() => reporter.onSessionInvalidated(any()));
        verifyNever(
          () => reporter.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'routes a well-formed session_invalidated push to onSessionInvalidated',
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

    /// One malformed `session_invalidated` payload per distinct `SessionInvalidatedPayload.fromJson`
    /// failure cause -- an unrecognized enum value, a missing required key, and the wrong type for
    /// that key are three different `CheckedFromJsonException` paths, all still wrapped as the
    /// same `ProtocolFormatException` -> `DovahLinkProtocolException(malformed_message)`.
    final Map<String, JsonMap> malformedSessionInvalidatedPayloads =
        <String, JsonMap>{
          'an unrecognized reason': const <String, dynamic>{
            'reason': 'not-a-real-reason',
          },
          'a missing reason key': const <String, dynamic>{},
          'the wrong type for reason': const <String, dynamic>{'reason': 7},
        };
    for (final MapEntry<String, JsonMap> entry
        in malformedSessionInvalidatedPayloads.entries) {
      test('reports a protocol violation, orphaning retry-safe operations, for '
          '${entry.key}', () {
        router.handleIncoming(
          rawEnvelope(messageType: 'session_invalidated', payload: entry.value),
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
          'malformed_message',
        );
        expect(verification.captured[1], isTrue);
        verifyNever(() => reporter.onSessionInvalidated(any()));
        verifyNever(() => requestManager.resolveReply(any(), any()));
      });
    }
  });
}
