import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connections.dart'
    show DovahLinkConnections;
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart'
    show IAuthenticationService;
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart'
    show IReconnectService;
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart'
    show IRequestService;
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart'
    show ISessionService;
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart'
    show ISubscriptionService;
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart'
    show ProtocolErrorCode, ProtocolMessageType, TimeoutClass;
import 'fixtures/fixtures.dart';

/// Mocks the existing session lifecycle owner.
class MockConnectionsSessionService extends Mock implements ISessionService {}

/// Mocks the existing authentication owner.
class MockConnectionsAuthenticationService extends Mock
    implements IAuthenticationService {}

/// Mocks established-session recovery.
class MockConnectionsReconnectService extends Mock
    implements IReconnectService {}

/// Mocks desired state subscription ownership.
class MockConnectionsSubscriptionService extends Mock
    implements ISubscriptionService {}

/// Mocks the shared correlated request owner.
class MockConnectionsRequestService extends Mock implements IRequestService {}

/// Builds a fallback successful handshake for the mocked retry callback.
Future<HelloResult> buildRetryFallback() async => Fixtures.buildHelloResult();

/// Builds a typed `rename_outcome` envelope for connection-operation tests.
/// @param outcome The canonical wire result.
/// @param displayName The resulting name, or `null` when the name was cleared or rejected.
/// @return A representative rename reply envelope.
Envelope buildRenameOutcomeEnvelope({
  required String outcome,
  required String? displayName,
}) => Fixtures.buildEnvelope(
  messageType: ProtocolMessageType.renameOutcome,
  payload: <String, dynamic>{'outcome': outcome, 'displayName': displayName},
);

/// Tests the grouped connection view over the existing SDK engine.
void main() {
  late MockConnectionsSessionService sessionService;
  late MockConnectionsAuthenticationService authenticationService;
  late MockConnectionsReconnectService reconnectService;
  late MockConnectionsSubscriptionService subscriptionService;
  late MockConnectionsRequestService requestService;
  late DovahLinkConnections connections;

  setUpAll(() {
    registerFallbackValue(buildRetryFallback);
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
    sessionService = MockConnectionsSessionService();
    authenticationService = MockConnectionsAuthenticationService();
    reconnectService = MockConnectionsReconnectService();
    subscriptionService = MockConnectionsSubscriptionService();
    requestService = MockConnectionsRequestService();
    when(() => reconnectService.connectWithInitialRetry(any())).thenAnswer((
      Invocation invocation,
    ) {
      final Future<HelloResult> Function() attempt =
          invocation.positionalArguments.single
              as Future<HelloResult> Function();
      return attempt();
    });
    when(() => reconnectService.initialConnectionRetryChanges).thenAnswer(
      (_) => const Stream<DovahLinkInitialConnectionRetryStatus>.empty(),
    );
    connections = DovahLinkConnections(
      sessionService: sessionService,
      authenticationService: authenticationService,
      reconnectService: reconnectService,
      subscriptionService: subscriptionService,
      requestService: requestService,
    );
  });

  group('Method connectCandidate behaves correctly', () {
    test(
      'Method connectCandidate authenticates the supplied candidate endpoint',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        final HelloResult result = Fixtures.buildHelloResult(
          trustState: DovahLinkTrustState.unpaired,
        );
        when(
          () => authenticationService.authenticateCandidate(uri),
        ).thenAnswer((_) async => result);

        expect(await connections.connectCandidate(uri), same(result));
        verify(() => reconnectService.connectWithInitialRetry(any())).called(1);
      },
    );

    test(
      'Method connectCandidate propagates typed connection failures',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');
        const DovahLinkConnectionException failure =
            DovahLinkConnectionException('unreachable');
        when(
          () => authenticationService.authenticateCandidate(uri),
        ).thenAnswer((_) async => throw failure);

        await expectLater(
          connections.connectCandidate(uri),
          throwsA(same(failure)),
        );
      },
    );
  });

  group('Method connectKnownHost behaves correctly', () {
    test(
      'Method connectKnownHost authenticates the supplied Host identity',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        final HelloResult result = Fixtures.buildHelloResult();
        when(
          () => authenticationService.authenticateKnownHost(hostId),
        ).thenAnswer((_) async => result);

        expect(await connections.connectKnownHost(hostId), same(result));
        verify(() => reconnectService.connectWithInitialRetry(any())).called(1);
      },
    );

    test('Method connectKnownHost propagates unknown Host failures', () async {
      final DovahLinkHostId hostId = DovahLinkHostId(
        '81869993-955c-4ba3-a7d0-d35ca86078ea',
      );
      const DovahLinkKnownHostNotFoundException failure =
          DovahLinkKnownHostNotFoundException(
            '81869993-955c-4ba3-a7d0-d35ca86078ea',
          );
      when(
        () => authenticationService.authenticateKnownHost(hostId),
      ).thenAnswer((_) async => throw failure);

      await expectLater(
        connections.connectKnownHost(hostId),
        throwsA(same(failure)),
      );
    });
  });

  group('Method renameDevice behaves correctly', () {
    test('Method renameDevice preserves the Host rename outcome', () async {
      when(
        () => requestService.sendAndAwait(
          messageType: any(named: 'messageType'),
          payload: any(named: 'payload'),
          expectedType: any(named: 'expectedType'),
          policy: any(named: 'policy'),
        ),
      ).thenAnswer(
        (_) async => buildRenameOutcomeEnvelope(
          outcome: 'renamed',
          displayName: 'Living Room PC',
        ),
      );

      expect(
        await connections.renameDevice('Living Room PC'),
        RenameOutcome.renamed,
      );
      final List<Object?> captured = verify(
        () => requestService.sendAndAwait(
          messageType: ProtocolMessageType.renameRequest,
          payload: captureAny(named: 'payload'),
          expectedType: ProtocolMessageType.renameOutcome,
          policy: const RequestPolicy(
            retrySafe: false,
            requiredTrustState: DovahLinkTrustState.trusted,
            timeoutClass: TimeoutClass.normal,
          ),
        ),
      ).captured;
      expect((captured.single! as JsonMap)['displayName'], 'Living Room PC');
    });

    test(
      'Method renameDevice preserves invalid and untrusted outcomes',
      () async {
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer(
          (_) async => buildRenameOutcomeEnvelope(
            outcome: 'invalid_display_name',
            displayName: null,
          ),
        );

        expect(
          await connections.renameDevice('bad name'),
          RenameOutcome.invalidDisplayName,
        );

        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer(
          (_) async => buildRenameOutcomeEnvelope(
            outcome: 'not_trusted',
            displayName: null,
          ),
        );

        expect(
          await connections.renameDevice('New Name'),
          RenameOutcome.notTrusted,
        );
      },
    );

    test(
      'Method renameDevice sends an empty name to clear the Host label',
      () async {
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer(
          (_) async =>
              buildRenameOutcomeEnvelope(outcome: 'renamed', displayName: null),
        );

        expect(await connections.renameDevice(''), RenameOutcome.renamed);
        final List<Object?> captured = verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.renameRequest,
            payload: captureAny(named: 'payload'),
            expectedType: ProtocolMessageType.renameOutcome,
            policy: const RequestPolicy(
              retrySafe: false,
              requiredTrustState: DovahLinkTrustState.trusted,
              timeoutClass: TimeoutClass.normal,
            ),
          ),
        ).captured;
        expect((captured.single! as JsonMap)['displayName'], '');
      },
    );

    test(
      'Method renameDevice reports malformed outcomes as protocol failures',
      () async {
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer(
          (_) async =>
              buildRenameOutcomeEnvelope(outcome: 'unknown', displayName: null),
        );

        await expectLater(
          connections.renameDevice('New Name'),
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

    test('Method renameDevice propagates transport failures', () async {
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

      await expectLater(
        connections.renameDevice('New Name'),
        throwsA(same(failure)),
      );
    });
  });

  group('Property state behaves correctly', () {
    test('Property state reads the authoritative session phase', () {
      when(
        () => sessionService.connectionState,
      ).thenReturn(DovahLinkConnectionState.reauthenticating);

      expect(connections.state, DovahLinkConnectionState.reauthenticating);
      verify(() => sessionService.connectionState).called(1);
    });
  });

  group('Property invalidationReason behaves correctly', () {
    test(
      'Property invalidationReason reads the session invalidation reason',
      () {
        when(
          () => sessionService.invalidationReason,
        ).thenReturn(AdministrativeInvalidationReason.revoked);

        expect(
          connections.invalidationReason,
          AdministrativeInvalidationReason.revoked,
        );
        verify(() => sessionService.invalidationReason).called(1);
      },
    );
  });

  group('Property knownHostInvalidations behaves correctly', () {
    test(
      'Property knownHostInvalidations exposes session invalidation events',
      () {
        const Stream<DovahLinkKnownHostInvalidation> changes =
            Stream<DovahLinkKnownHostInvalidation>.empty();
        when(
          () => sessionService.knownHostInvalidations,
        ).thenAnswer((_) => changes);

        expect(identical(connections.knownHostInvalidations, changes), isTrue);
        verify(() => sessionService.knownHostInvalidations).called(1);
      },
    );
  });

  group('Property stateChanges behaves correctly', () {
    test('Property stateChanges exposes the session lifecycle stream', () {
      const Stream<DovahLinkConnectionState> changes =
          Stream<DovahLinkConnectionState>.empty();
      when(
        () => sessionService.connectionStateChanges,
      ).thenAnswer((_) => changes);

      expect(identical(connections.stateChanges, changes), isTrue);
      verify(() => sessionService.connectionStateChanges).called(1);
    });
  });

  group('Property initialConnectionRetryChanges behaves correctly', () {
    test(
      'Property initialConnectionRetryChanges exposes the SDK retry lifecycle',
      () {
        const Stream<DovahLinkInitialConnectionRetryStatus> changes =
            Stream<DovahLinkInitialConnectionRetryStatus>.empty();
        when(
          () => reconnectService.initialConnectionRetryChanges,
        ).thenAnswer((_) => changes);

        expect(
          identical(connections.initialConnectionRetryChanges, changes),
          isTrue,
        );
        verify(() => reconnectService.initialConnectionRetryChanges).called(1);
      },
    );
  });

  group('Method disconnect behaves correctly', () {
    test(
      'Method disconnect cancels authentication and recovery before teardown',
      () async {
        when(() => sessionService.disconnect()).thenAnswer((_) async {});

        await connections.disconnect();

        verifyInOrder([
          () => reconnectService.stopInitialConnectionRetry(),
          () => reconnectService.stopRecovery(),
          () => subscriptionService.clearDesiredStateAreas(),
          () => sessionService.disconnect(),
        ]);
      },
    );

    test('Method disconnect preserves a teardown failure', () async {
      const DovahLinkConnectionException failure = DovahLinkConnectionException(
        'teardown failed',
      );
      when(() => sessionService.disconnect()).thenThrow(failure);

      await expectLater(connections.disconnect(), throwsA(same(failure)));
      verify(() => reconnectService.stopInitialConnectionRetry()).called(1);
      verify(() => reconnectService.stopRecovery()).called(1);
      verify(() => subscriptionService.clearDesiredStateAreas()).called(1);
    });
  });
}
