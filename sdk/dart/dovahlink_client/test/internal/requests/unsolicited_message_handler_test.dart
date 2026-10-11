import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/unsolicited_message_handler.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/gameplay_readiness_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';
import '../readiness/mock_gameplay_readiness_service.dart';
import '../state/mock_state_message_handler.dart';

/// Mock session service used to capture unsolicited-message decisions.
class MockSessionService extends Mock implements ISessionService {}

/// Runs unsolicited-message handler behavior tests.
void main() {
  late MockSessionService sessionService;
  late MockStateMessageHandler stateMessageHandler;
  late MockGameplayReadinessService readiness;
  late UnsolicitedMessageHandler handler;

  setUpAll(() {
    registerFallbackValue(Exception('fallback for any()'));
    registerFallbackValue(AdministrativeInvalidationReason.revoked);
    registerFallbackValue(Fixtures.buildEnvelope());
    registerFallbackValue(Fixtures.buildGameplayReadinessPayload());
    registerFallbackValue(
      const ErrorPayload(
        code: ProtocolErrorCode.malformedMessage,
        message: 'fallback for any()',
        retryable: false,
      ),
    );
  });

  setUp(() {
    sessionService = MockSessionService();
    stateMessageHandler = MockStateMessageHandler();
    readiness = MockGameplayReadinessService();
    when(
      () => readiness.observe(
        sessionId: any(named: 'sessionId'),
        stateAuthorityId: any(named: 'stateAuthorityId'),
        payload: any(named: 'payload'),
      ),
    ).thenAnswer((_) {});
    when(() => stateMessageHandler.handle(any())).thenAnswer((_) {});
    when(
      () => stateMessageHandler.isPendingBaselineCorrelation(any()),
    ).thenReturn(false);
    handler = UnsolicitedMessageHandler(
      sessionService: sessionService,
      stateMessageHandler: stateMessageHandler,
      gameplayReadinessService: readiness,
    );
  });

  group('Method handle for gameplay_readiness behaves correctly', () {
    /// Builds a valid unsolicited readiness envelope carrying [payload].
    Envelope buildReadiness(
      JsonMap payload, {
      String? sessionId = 'session-1',
    }) => Fixtures.buildEnvelope(
      messageType: ProtocolMessageType.gameplayReadiness,
      sessionId: sessionId,
      correlationId: null,
      stateAuthorityId: 'authority-1',
      payload: payload,
    );

    test(
      'Method handle gives a valid readiness message to the readiness owner',
      () {
        handler.handle(
          buildReadiness(const <String, dynamic>{'ready': true, 'sequence': 3}),
        );

        final GameplayReadinessPayload payload =
            verify(
                  () => readiness.observe(
                    sessionId: 'session-1',
                    stateAuthorityId: 'authority-1',
                    payload: captureAny(named: 'payload'),
                  ),
                ).captured.single
                as GameplayReadinessPayload;
        expect(payload.ready, isTrue);
        expect(payload.sequence, 3);
        verifyNever(() => stateMessageHandler.handle(any()));
        verifyNever(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handle reports malformed readiness payloads without observing them',
      () {
        const List<JsonMap> malformedPayloads = <JsonMap>[
          <String, dynamic>{'ready': true, 'sequence': 0},
          <String, dynamic>{'ready': true, 'sequence': 1.5},
          <String, dynamic>{'ready': 'yes', 'sequence': 1},
          <String, dynamic>{'sequence': 1},
          <String, dynamic>{'ready': true, 'sequence': 1, 'reason': 'loading'},
          <String, dynamic>{},
        ];

        for (final JsonMap payload in malformedPayloads) {
          handler.handle(buildReadiness(payload));
        }

        final VerificationResult verification = verify(
          () => sessionService.onProtocolViolation(
            captureAny(),
            orphanRetrySafeOperations: captureAny(
              named: 'orphanRetrySafeOperations',
            ),
          ),
        );
        expect(verification.callCount, malformedPayloads.length);
        expect(
          verification.captured.whereType<DovahLinkProtocolException>().map(
            (DovahLinkProtocolException error) => error.code,
          ),
          everyElement(ProtocolErrorCode.malformedMessage),
        );
        verifyNever(
          () => readiness.observe(
            sessionId: any(named: 'sessionId'),
            stateAuthorityId: any(named: 'stateAuthorityId'),
            payload: any(named: 'payload'),
          ),
        );
      },
    );

    test(
      'Method handle reports a readiness envelope without session identity',
      () {
        handler.handle(
          buildReadiness(const <String, dynamic>{
            'ready': true,
            'sequence': 1,
          }, sessionId: null),
        );

        verify(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: false,
          ),
        ).called(1);
        verifyNever(
          () => readiness.observe(
            sessionId: any(named: 'sessionId'),
            stateAuthorityId: any(named: 'stateAuthorityId'),
            payload: any(named: 'payload'),
          ),
        );
      },
    );
  });

  group('Method handle behaves correctly', () {
    test('Method handle reports a valid session_invalidated reason', () {
      handler.handle(
        Fixtures.buildEnvelope(
          messageType: ProtocolMessageType.sessionInvalidated,
          correlationId: null,
          payload: const <String, dynamic>{'reason': 'revoked'},
        ),
      );

      verify(
        () => sessionService.onSessionInvalidated(
          AdministrativeInvalidationReason.revoked,
        ),
      ).called(1);
      verifyNever(
        () => sessionService.onProtocolViolation(
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
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.sessionInvalidated,
            correlationId: null,
            payload: payload,
          ),
        );
      }

      final VerificationResult verification = verify(
        () => sessionService.onProtocolViolation(
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
      verifyNever(() => sessionService.onSessionInvalidated(any()));
    });

    test('Method handle ignores known unsupported unsolicited messages', () {
      handler.handle(
        Fixtures.buildEnvelope(
          messageType: ProtocolMessageType.capabilities,
          correlationId: null,
          payload: const <String, dynamic>{},
        ),
      );

      verifyNever(() => sessionService.onSessionInvalidated(any()));
      verifyNever(
        () => sessionService.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
      verifyNever(() => stateMessageHandler.handle(any()));
    });

    test(
      'Method handle routes state Snapshots and Events to the state handler',
      () {
        handler.handle(
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.stateSnapshot,
            correlationId: null,
            stateAuthorityId: 'authority-1',
            payload: <String, dynamic>{
              'stateArea': 'character_xp',
              'revision': 1,
              'occurredAt': '2026-09-23T12:00:00Z',
              'data': <String, dynamic>{'value': 42.5},
            },
          ),
        );
        handler.handle(
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.stateEvent,
            correlationId: null,
            stateAuthorityId: 'authority-1',
            payload: <String, dynamic>{
              'stateArea': 'character_level',
              'baseRevision': 1,
              'revision': 2,
              'occurredAt': '2026-09-23T12:00:00Z',
              'data': <String, dynamic>{'value': 2},
            },
          ),
        );

        final List<Envelope> routed = verify(
          () => stateMessageHandler.handle(captureAny()),
        ).captured.cast<Envelope>();
        expect(
          routed.map((Envelope envelope) => envelope.messageType),
          <ProtocolMessageType>[
            ProtocolMessageType.stateSnapshot,
            ProtocolMessageType.stateEvent,
          ],
        );
      },
    );

    test('Method handle reports a valid unsolicited error payload', () {
      handler.handle(
        Fixtures.buildEnvelope(
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
        () => sessionService.onUnsolicitedError(captureAny()),
      );
      verification.called(1);
      final ErrorPayload payload = verification.captured.single as ErrorPayload;
      expect(payload.code, ProtocolErrorCode.rateLimited);
      expect(payload.message, 'Too many requests.');
      expect(payload.retryable, isTrue);
      verifyNever(
        () => sessionService.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
      verifyNever(() => sessionService.onSessionInvalidated(any()));
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
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.error,
            correlationId: null,
            payload: payload,
          ),
        );
      }

      final VerificationResult verification = verify(
        () => sessionService.onProtocolViolation(
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
      verifyNever(() => sessionService.onUnsolicitedError(any()));
    });
  });

  group('Method handleCorrelatedBaselineError behaves correctly', () {
    test(
      'Method handleCorrelatedBaselineError consumes a known retryable baseline timeout',
      () {
        when(
          () => stateMessageHandler.isPendingBaselineCorrelation('subscribe-1'),
        ).thenReturn(true);

        expect(
          handler.handleCorrelatedBaselineError(
            Fixtures.buildEnvelope(
              messageType: ProtocolMessageType.error,
              correlationId: 'subscribe-1',
              payload: const <String, dynamic>{
                'code': 'temporarily_unavailable',
                'message': 'No authoritative baseline is available yet.',
                'retryable': true,
              },
            ),
          ),
          isTrue,
        );
        verify(
          () => stateMessageHandler.isPendingBaselineCorrelation('subscribe-1'),
        ).called(1);
        verifyNever(() => sessionService.onUnsolicitedError(any()));
        verifyNever(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method handleCorrelatedBaselineError leaves unknown correlations unconsumed',
      () {
        expect(
          handler.handleCorrelatedBaselineError(
            Fixtures.buildEnvelope(
              messageType: ProtocolMessageType.error,
              correlationId: 'unknown',
              payload: const <String, dynamic>{
                'code': 'temporarily_unavailable',
                'message': 'No authoritative baseline is available yet.',
                'retryable': true,
              },
            ),
          ),
          isFalse,
        );
        verify(
          () => stateMessageHandler.isPendingBaselineCorrelation('unknown'),
        ).called(1);
        verifyNever(() => sessionService.onUnsolicitedError(any()));
      },
    );

    test(
      'Method handleCorrelatedBaselineError rejects non-retryable baseline errors',
      () {
        expect(
          handler.handleCorrelatedBaselineError(
            Fixtures.buildEnvelope(
              messageType: ProtocolMessageType.error,
              correlationId: 'subscribe-1',
              payload: const <String, dynamic>{
                'code': 'temporarily_unavailable',
                'message': 'No authoritative baseline is available yet.',
                'retryable': false,
              },
            ),
          ),
          isFalse,
        );
        verifyNever(
          () => stateMessageHandler.isPendingBaselineCorrelation(any()),
        );
      },
    );

    test(
      'Method handleCorrelatedBaselineError reports malformed correlated errors',
      () {
        expect(
          handler.handleCorrelatedBaselineError(
            Fixtures.buildEnvelope(
              messageType: ProtocolMessageType.error,
              correlationId: 'subscribe-1',
              payload: const <String, dynamic>{'code': 'unknown'},
            ),
          ),
          isTrue,
        );

        final DovahLinkProtocolException error =
            verify(
                  () => sessionService.onProtocolViolation(
                    captureAny(),
                    orphanRetrySafeOperations: false,
                  ),
                ).captured.single
                as DovahLinkProtocolException;
        expect(error.code, ProtocolErrorCode.malformedMessage);
        verifyNever(() => sessionService.onUnsolicitedError(any()));
      },
    );
  });
}
