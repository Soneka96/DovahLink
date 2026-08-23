import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/unsolicited_message_handler.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../fixtures/protocol/envelope.fixture.dart';

/// Mock lifecycle reporter used to capture unsolicited-message decisions.
class MockConnectionLifecycleReporter extends Mock
    implements ConnectionLifecycleReporter {}

/// Runs unsolicited-message handler behavior tests.
void main() {
  late MockConnectionLifecycleReporter reporter;
  late UnsolicitedMessageHandler handler;

  setUpAll(() {
    registerFallbackValue(Exception('fallback for any()'));
    registerFallbackValue(AdministrativeInvalidationReason.revoked);
    registerFallbackValue(
      const ErrorPayload(
        code: ProtocolErrorCode.malformedMessage,
        message: 'fallback for any()',
        retryable: false,
      ),
    );
  });

  setUp(() {
    reporter = MockConnectionLifecycleReporter();
    handler = UnsolicitedMessageHandler(reporter: reporter);
  });

  group('Method handle behaves correctly', () {
    test('Method handle reports a valid session_invalidated reason', () {
      handler.handle(
        buildEnvelope(
          messageType: ProtocolMessageType.sessionInvalidated,
          correlationId: null,
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
    });

    test('Method handle reports malformed session_invalidated payloads', () {
      const List<JsonMap> malformedPayloads = <JsonMap>[
        <String, dynamic>{'reason': 'future_reason'},
        <String, dynamic>{},
        <String, dynamic>{'reason': 7},
      ];

      for (final JsonMap payload in malformedPayloads) {
        handler.handle(
          buildEnvelope(
            messageType: ProtocolMessageType.sessionInvalidated,
            correlationId: null,
            payload: payload,
          ),
        );
      }

      final VerificationResult verification = verify(
        () => reporter.onProtocolViolation(
          captureAny(),
          orphanRetrySafeOperations: captureAny(
            named: 'orphanRetrySafeOperations',
          ),
        ),
      );
      expect(verification.captured, hasLength(malformedPayloads.length * 2));
      for (int index = 0; index < malformedPayloads.length; index++) {
        expect(
          (verification.captured[index * 2] as DovahLinkProtocolException).code,
          ProtocolErrorCode.malformedMessage,
        );
        expect(verification.captured[index * 2 + 1], isFalse);
      }
      verifyNever(() => reporter.onSessionInvalidated(any()));
    });

    test('Method handle ignores known unsupported unsolicited messages', () {
      handler.handle(
        buildEnvelope(
          messageType: ProtocolMessageType.capabilities,
          correlationId: null,
          payload: const <String, dynamic>{},
        ),
      );

      verifyNever(() => reporter.onSessionInvalidated(any()));
      verifyNever(
        () => reporter.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
    });

    test('Method handle reports a valid unsolicited error payload', () {
      handler.handle(
        buildEnvelope(
          messageType: ProtocolMessageType.error,
          correlationId: null,
          payload: const <String, dynamic>{
            'code': 'rate_limited',
            'message': 'Too many requests.',
            'retryable': true,
          },
        ),
      );

      final VerificationResult verification = verify(
        () => reporter.onUnsolicitedError(captureAny()),
      );
      verification.called(1);
      final ErrorPayload payload = verification.captured.single as ErrorPayload;
      expect(payload.code, ProtocolErrorCode.rateLimited);
      expect(payload.message, 'Too many requests.');
      expect(payload.retryable, isTrue);
      verifyNever(
        () => reporter.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
      verifyNever(() => reporter.onSessionInvalidated(any()));
    });

    test('Method handle reports malformed unsolicited error payloads', () {
      const List<JsonMap> malformedPayloads = <JsonMap>[
        <String, dynamic>{'message': 'missing code/retryable'},
        <String, dynamic>{
          'code': 'not_a_real_code',
          'message': 'bad enum',
          'retryable': false,
        },
        <String, dynamic>{
          'code': 'rate_limited',
          'message': 'wrong type',
          'retryable': 'yes',
        },
      ];

      for (final JsonMap payload in malformedPayloads) {
        handler.handle(
          buildEnvelope(
            messageType: ProtocolMessageType.error,
            correlationId: null,
            payload: payload,
          ),
        );
      }

      final VerificationResult verification = verify(
        () => reporter.onProtocolViolation(
          captureAny(),
          orphanRetrySafeOperations: captureAny(
            named: 'orphanRetrySafeOperations',
          ),
        ),
      );
      expect(verification.captured, hasLength(malformedPayloads.length * 2));
      for (int index = 0; index < malformedPayloads.length; index++) {
        expect(
          (verification.captured[index * 2] as DovahLinkProtocolException).code,
          ProtocolErrorCode.malformedMessage,
        );
        expect(verification.captured[index * 2 + 1], isFalse);
      }
      verifyNever(() => reporter.onUnsolicitedError(any()));
    });
  });
}
