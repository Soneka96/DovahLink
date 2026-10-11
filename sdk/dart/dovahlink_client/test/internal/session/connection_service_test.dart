import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/connection_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart'
    show ProtocolErrorCode, ProtocolMessageType, TimeoutClass;
import '../../fixtures/fixtures.dart';

/// Mocks the session lifecycle owner.
class MockSessionService extends Mock implements ISessionService {}

/// Mocks the authentication owner.
class MockAuthenticationService extends Mock
    implements IAuthenticationService {}

/// Mocks the reconnect owner.
class MockReconnectService extends Mock implements IReconnectService {}

/// Mocks desired subscription intent.
class MockSubscriptionService extends Mock implements ISubscriptionService {}

/// Mocks the correlated request owner.
class MockRequestService extends Mock implements IRequestService {}

/// Builds a typed `rename_outcome` envelope.
/// @param outcome The canonical wire result.
/// @param displayName The resulting name, or `null` when cleared or rejected.
Envelope _renameOutcomeEnvelope({
  required String outcome,
  required String? displayName,
}) => Fixtures.buildEnvelope(
  messageType: ProtocolMessageType.renameOutcome,
  payload: <String, dynamic>{'outcome': outcome, 'displayName': displayName},
);

/// Runs connection-service behavior tests.
void main() {
  late MockSessionService sessionService;
  late MockAuthenticationService authenticationService;
  late MockReconnectService reconnectService;
  late MockSubscriptionService subscriptionService;
  late MockRequestService requestService;
  late List<String> calls;
  late ConnectionService service;

  setUpAll(() {
    registerFallbackValue(const <String, dynamic>{});
    registerFallbackValue(ProtocolMessageType.renameRequest);
    registerFallbackValue(
      Fixtures.buildRequestPolicy(
        retrySafe: false,
        requiredTrustState: DovahLinkTrustState.trusted,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    registerFallbackValue(
      const DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'test fallback',
        retryable: false,
      ),
    );
  });

  setUp(() {
    sessionService = MockSessionService();
    authenticationService = MockAuthenticationService();
    reconnectService = MockReconnectService();
    subscriptionService = MockSubscriptionService();
    requestService = MockRequestService();
    calls = <String>[];
    when(
      () => authenticationService.cancelPendingAuthentication(),
    ).thenAnswer((_) => calls.add('cancelAuthentication'));
    when(
      () => reconnectService.stopInitialConnectionRetry(),
    ).thenAnswer((_) => calls.add('stopInitialRetry'));
    when(
      () => reconnectService.stopRecovery(),
    ).thenAnswer((_) => calls.add('stopRecovery'));
    when(
      () => subscriptionService.clearDesiredStateAreas(),
    ).thenAnswer((_) => calls.add('clearIntent'));
    when(() => sessionService.disconnect()).thenAnswer((_) async {
      calls.add('disconnect');
    });
    when(() => sessionService.close()).thenAnswer((_) async {
      calls.add('close');
    });
    service = ConnectionService(
      sessionService: sessionService,
      authenticationService: authenticationService,
      reconnectService: reconnectService,
      subscriptionService: subscriptionService,
      requestService: requestService,
    );
  });

  /// Stubs the rename round trip to answer with [envelope].
  void stubRename(Envelope envelope) {
    when(
      () => requestService.sendAndAwait(
        messageType: any(named: 'messageType'),
        payload: any(named: 'payload'),
        expectedType: any(named: 'expectedType'),
        policy: any(named: 'policy'),
      ),
    ).thenAnswer((_) async => envelope);
  }

  const List<String> cancellationOrder = <String>[
    'cancelAuthentication',
    'stopInitialRetry',
    'stopRecovery',
    'clearIntent',
  ];

  group('Method disconnect behaves correctly', () {
    test(
      'Method disconnect cancels authentication, retry, recovery, and intent before teardown',
      () async {
        await service.disconnect();

        expect(calls, <String>[...cancellationOrder, 'disconnect']);
      },
    );

    test('Method disconnect does not close the session lifecycle', () async {
      await service.disconnect();

      verifyNever(() => sessionService.close());
    });

    test('Method disconnect preserves a teardown failure', () async {
      const DovahLinkConnectionException failure = DovahLinkConnectionException(
        'teardown failed',
      );
      when(() => sessionService.disconnect()).thenThrow(failure);

      await expectLater(service.disconnect(), throwsA(same(failure)));
      expect(calls, cancellationOrder);
    });
  });

  group('Behavior a failing cancellation step behaves correctly', () {
    test(
      'Behavior disconnect surfaces it and skips session teardown',
      () async {
        final StateError failure = StateError('stopRecovery failed');
        when(() => reconnectService.stopRecovery()).thenThrow(failure);

        await expectLater(service.disconnect(), throwsA(same(failure)));
        verifyNever(() => sessionService.disconnect());
        verifyNever(() => subscriptionService.clearDesiredStateAreas());
      },
    );

    test(
      'Behavior shutdown surfaces it and skips the terminal close',
      () async {
        final StateError failure = StateError('stopRecovery failed');
        when(() => reconnectService.stopRecovery()).thenThrow(failure);

        await expectLater(service.shutdown(), throwsA(same(failure)));
        verifyNever(() => sessionService.close());
      },
    );
  });

  group('Method shutdown behaves correctly', () {
    test(
      'Method shutdown runs the same cancellation sequence before the terminal close',
      () async {
        await service.shutdown();

        expect(calls, <String>[...cancellationOrder, 'close']);
        verifyNever(() => sessionService.disconnect());
      },
    );

    test('Method shutdown preserves a close failure', () async {
      const DovahLinkConnectionException failure = DovahLinkConnectionException(
        'close failed',
      );
      when(() => sessionService.close()).thenAnswer((_) async => throw failure);

      await expectLater(service.shutdown(), throwsA(same(failure)));
      expect(calls, cancellationOrder);
    });
  });

  group('Method renameDevice behaves correctly', () {
    test(
      'Method renameDevice sends a trusted-only non-retry request',
      () async {
        stubRename(
          _renameOutcomeEnvelope(
            outcome: 'renamed',
            displayName: 'Living Room',
          ),
        );

        expect(
          await service.renameDevice('Living Room'),
          RenameOutcome.renamed,
        );

        final List<Object?> captured = verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.renameRequest,
            payload: captureAny(named: 'payload'),
            expectedType: ProtocolMessageType.renameOutcome,
            policy: Fixtures.buildRequestPolicy(
              retrySafe: false,
              requiredTrustState: DovahLinkTrustState.trusted,
              timeoutClass: TimeoutClass.normal,
            ),
          ),
        ).captured;
        expect((captured.single! as JsonMap)['displayName'], 'Living Room');
      },
    );

    test(
      'Method renameDevice sends an empty name to clear the label',
      () async {
        stubRename(
          _renameOutcomeEnvelope(outcome: 'renamed', displayName: null),
        );

        expect(await service.renameDevice(''), RenameOutcome.renamed);
        final List<Object?> captured = verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.renameRequest,
            payload: captureAny(named: 'payload'),
            expectedType: ProtocolMessageType.renameOutcome,
            policy: any(named: 'policy'),
          ),
        ).captured;
        expect((captured.single! as JsonMap)['displayName'], '');
      },
    );

    test('Method renameDevice preserves rejected outcomes', () async {
      stubRename(
        _renameOutcomeEnvelope(
          outcome: 'invalid_display_name',
          displayName: null,
        ),
      );
      expect(
        await service.renameDevice('bad name'),
        RenameOutcome.invalidDisplayName,
      );

      stubRename(
        _renameOutcomeEnvelope(outcome: 'not_trusted', displayName: null),
      );
      expect(await service.renameDevice('Name'), RenameOutcome.notTrusted);
    });

    test(
      'Method renameDevice reports a malformed outcome as a protocol violation',
      () async {
        stubRename(
          _renameOutcomeEnvelope(outcome: 'unknown', displayName: null),
        );

        await expectLater(
          service.renameDevice('Name'),
          throwsA(isA<DovahLinkProtocolException>()),
        );
        verify(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: false,
          ),
        ).called(1);
      },
    );

    test(
      'Method renameDevice never cancels or tears down the session',
      () async {
        stubRename(
          _renameOutcomeEnvelope(outcome: 'renamed', displayName: null),
        );

        await service.renameDevice('Name');

        expect(calls, isEmpty);
        verifyNever(() => sessionService.disconnect());
        verifyNever(() => sessionService.close());
      },
    );

    test(
      'Method renameDevice propagates a Host protocol failure without reporting a violation',
      () async {
        const DovahLinkProtocolException failure = DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'host error',
          retryable: false,
        );
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((_) async => throw failure);

        await expectLater(service.renameDevice('Name'), throwsA(same(failure)));
        verifyNever(
          () => sessionService.onProtocolViolation(
            any(),
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test('Method renameDevice does not report transport failures', () async {
      const DovahLinkConnectionException failure = DovahLinkConnectionException(
        'connection lost during rename',
      );
      when(
        () => requestService.sendAndAwait(
          messageType: any(named: 'messageType'),
          payload: any(named: 'payload'),
          expectedType: any(named: 'expectedType'),
          policy: any(named: 'policy'),
        ),
      ).thenAnswer((_) async => throw failure);

      await expectLater(service.renameDevice('Name'), throwsA(same(failure)));
      verifyNever(
        () => sessionService.onProtocolViolation(
          any(),
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
    });
  });
}
