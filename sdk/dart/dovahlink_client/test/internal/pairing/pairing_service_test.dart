import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_handshake.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing/pairing_service.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_trust_service.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';

/// Mock request service used to isolate pairing service tests, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockRequestService extends Mock implements IRequestService {}

/// Mock session trust service -- its own trust-upgrade logic is
/// `session_trust_service_test.dart`'s responsibility; this file only proves
/// [PairingService] calls it after a successful acknowledgement.
class MockSessionTrustService extends Mock implements ISessionTrustService {}

/// Mocks session lifecycle so pairing tests can control the admitted Host context.
class MockSessionService extends Mock implements ISessionService {}

/// Mock client storage -- its own persistence mechanics are covered by its own implementation's
/// test file; this file only proves [PairingService] reads and writes the right state, and
/// never touches it on a rejected outcome.
class MockClientStateService extends Mock implements IClientStateService {}

/// Mocks the single owner of runtime Known Host availability.
class MockHostAvailabilityService extends Mock
    implements IHostAvailabilityService {}

class MockAuthenticationService extends Mock
    implements IAuthenticationService {}

class MockReconnectService extends Mock implements IReconnectService {}

/// Holds a state update until a pairing test releases it to control lifecycle timing.
class GatedClientStateService implements IClientStateService {
  /// Creates a state owner beginning with [loadedState].
  /// @param loadedState The initial client state.
  /// @param updateGate The signal that permits the next update to commit.
  GatedClientStateService({
    required PersistedClientState loadedState,
    required this.updateGate,
  }) : _state = loadedState;

  /// Signals that [updateState] has started.
  final Completer<void> updateStarted = Completer<void>();

  /// Controls when [updateState] commits.
  final Completer<void> updateGate;

  /// Latest state committed by this fake owner.
  PersistedClientState _state;

  /// Returns the latest committed state.
  @override
  Future<PersistedClientState> load() async => _state;

  /// Applies [update] after the gate opens.
  @override
  Future<void> updateState(
    PersistedClientState Function(PersistedClientState state) update,
  ) async {
    updateStarted.complete();
    await updateGate.future;
    _state = update(_state);
  }

  /// Emits the current Known Host to a new subscriber.
  @override
  Stream<List<PersistedKnownHost>> get knownHostsChanges =>
      Stream<List<PersistedKnownHost>>.value(
        _state.knownHosts.values.toList(growable: false),
      );
}

/// Builds a persisted relationship for pairing tests through the central fixture catalog.
/// @param clientId The stable local Client ID.
/// @param credential The current Host-scoped credential, if any.
/// @param pairingRequired Whether the Host's last-known recovery hint is set.
/// @param recoveryState The Host-owned pending pairing recovery phase.
/// @param knownHost The explicit Host relationship, or the fixture Host by default.
/// @return A persisted state containing the requested pairing lifecycle.
PersistedClientState _state({
  String? clientId = 'client-1',
  String? credential,
  bool pairingRequired = false,
  PairingRecoveryState recoveryState = PairingRecoveryState.none,
  DovahLinkHost? knownHost,
}) => Fixtures.buildPersistedClientState(
  clientId: clientId,
  credential: credential,
  pairingRequired: pairingRequired,
  recoveryState: recoveryState,
  host: knownHost,
);

/// Builds the current Host's persisted confirmation state.
/// @param credential The Host-issued credential to store.
/// @return A persisted state owned by the current Host.
PersistedClientState _confirmingState({String? credential = 'credential-a'}) =>
    _state(
      clientId: 'client-1',
      credential: credential,
      recoveryState: PairingRecoveryState.confirming,
      knownHost: _currentHost(),
    );

/// Builds the Host associated with the session used by pairing tests.
DovahLinkHost _currentHost() => DovahLinkHost(
  hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
  hostName: 'LOCAL-HOST',
  endpoint: Uri.parse('ws://127.0.0.1:58231/'),
);

/// Builds a decoded `pairing_outcome` reply envelope carrying [outcome], with every other field
/// present-but-empty per that message's wire contract.
/// [attemptsRemaining] and [retryAfterSeconds] provide Host-reported runtime metadata.
Envelope buildPairingOutcomeEnvelope({
  PairingOutcome outcome = PairingOutcome.alreadyIdle,
  String? credential,
  String? shortId,
  String? displayName,
  int? attemptsRemaining,
  int? retryAfterSeconds,
}) => Fixtures.buildEnvelope(
  messageType: ProtocolMessageType.pairingOutcome,
  payload: <String, dynamic>{
    'outcome': _wirePairingOutcome(outcome),
    'credential': credential,
    'shortId': shortId,
    'displayName': displayName,
    'attemptsRemaining': attemptsRemaining,
    'retryAfterSeconds': retryAfterSeconds,
  },
);

/// Converts a typed pairing outcome to its protocol wire value.
String _wirePairingOutcome(PairingOutcome outcome) => switch (outcome) {
  PairingOutcome.credentialIssued => 'credential_issued',
  PairingOutcome.trusted => 'trusted',
  PairingOutcome.alreadyTrusted => 'already_trusted',
  PairingOutcome.expired => 'expired',
  PairingOutcome.invalid => 'invalid',
  PairingOutcome.pacingLimited => 'pacing_limited',
  PairingOutcome.hardLimitReached => 'hard_limit_reached',
  PairingOutcome.pendingNotFound => 'pending_not_found',
  PairingOutcome.renotified => 'renotified',
  PairingOutcome.renotifyCooldown => 'renotify_cooldown',
  PairingOutcome.cancelled => 'cancelled',
  PairingOutcome.alreadyIdle => 'already_idle',
  PairingOutcome.pairingInvalidated => 'pairing_invalidated',
};

/// Stubs `sendAndAwait` to answer with [envelope], matching any call.
void stubSendAndAwait(MockRequestService requestService, Envelope envelope) {
  when(
    () => requestService.sendAndAwait(
      messageType: any(named: 'messageType'),
      payload: any(named: 'payload'),
      expectedType: any(named: 'expectedType'),
      policy: any(named: 'policy'),
    ),
  ).thenAnswer((_) async => envelope);
}

/// Verifies that a rejected pairing operation did not touch [storage].
void verifyNoStorageCalls(MockClientStateService storage) {
  verifyNever(() => storage.load());
  verifyNever(() => storage.updateState(any()));
}

/// Verifies that a failed acknowledgement did not commit client state.
void verifyNoStateMutations(MockClientStateService storage) {
  verifyNever(() => storage.updateState(any()));
}

/// Runs pairing service behavior tests.
void main() {
  late MockRequestService requestService;
  late MockSessionTrustService sessionTrustService;
  late MockSessionService sessionService;
  late MockClientStateService storage;
  late MockHostAvailabilityService hostAvailabilityService;
  late MockAuthenticationService authenticationService;
  late MockReconnectService reconnectService;
  late PersistedClientState? updatedState;
  late PairingService service;
  late String currentSessionId;
  late DovahLinkHost currentHost;

  setUpAll(() {
    registerFallbackValue(Uri.parse('ws://127.0.0.1:58231/'));
    registerFallbackValue(ProtocolMessageType.pairingRequest);
    registerFallbackValue(
      Fixtures.buildRequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    registerFallbackValue(Fixtures.buildPersistedClientState());
    registerFallbackValue((PersistedClientState state) => state);
    registerFallbackValue(() async => Fixtures.buildHelloResult());
    registerFallbackValue(
      DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
    );
    registerFallbackValue(DovahLinkHostAvailability.unknown);
  });

  setUp(() {
    requestService = MockRequestService();
    sessionTrustService = MockSessionTrustService();
    sessionService = MockSessionService();
    storage = MockClientStateService();
    hostAvailabilityService = MockHostAvailabilityService();
    authenticationService = MockAuthenticationService();
    reconnectService = MockReconnectService();
    updatedState = null;
    currentSessionId = 'session-1';
    currentHost = _currentHost();
    when(() => sessionTrustService.markTrusted()).thenReturn(null);
    when(() => sessionService.currentHost).thenAnswer((_) => currentHost);
    when(
      () => sessionService.currentSessionId,
    ).thenAnswer((_) => currentSessionId);
    when(() => storage.load()).thenAnswer(
      (_) async => _state(clientId: 'client-1', knownHost: _currentHost()),
    );
    when(() => storage.updateState(any())).thenAnswer((invocation) async {
      final PersistedClientState Function(PersistedClientState) update =
          invocation.positionalArguments.single
              as PersistedClientState Function(PersistedClientState);
      updatedState = update(await storage.load());
    });
    when(
      () => hostAvailabilityService.setAvailability(any(), any()),
    ).thenReturn(null);
    when(() => sessionService.associateKnownHost(any())).thenReturn(null);
    when(() => authenticationService.authenticateCandidate(any())).thenAnswer(
      (_) async =>
          Fixtures.buildHelloResult(trustState: DovahLinkTrustState.trusted),
    );
    when(() => authenticationService.authenticateKnownHost(any())).thenAnswer(
      (_) async =>
          Fixtures.buildHelloResult(trustState: DovahLinkTrustState.trusted),
    );
    when(() => reconnectService.connectWithInitialRetry(any())).thenAnswer((
      invocation,
    ) {
      final Future<HelloResult> Function() attempt =
          invocation.positionalArguments.single
              as Future<HelloResult> Function();
      return attempt();
    });
    service = PairingService(
      authenticationService: authenticationService,
      reconnectService: reconnectService,
      sessionService: sessionService,
      sessionTrustService: sessionTrustService,
      requestService: requestService,
      clientStateService: storage,
      hostAvailabilityService: hostAvailabilityService,
    );
  });

  group('Method authenticateCandidate behaves correctly', () {
    test(
      'Method authenticateCandidate runs shared authentication and retry',
      () async {
        final Uri uri = Uri.parse('ws://127.0.0.1:58231/');

        final DovahLinkPairingHandshake result = await service
            .authenticateCandidate(uri);

        expect(result.hello.trustState, DovahLinkTrustState.trusted);
        expect(result.trustState, DovahLinkTrustState.trusted);
        verify(() => reconnectService.connectWithInitialRetry(any())).called(1);
        verify(
          () => authenticationService.authenticateCandidate(uri),
        ).called(1);
      },
    );

    test(
      'Method authenticateCandidate recovers pending confirmation after an unpaired hello',
      () async {
        when(
          () => authenticationService.authenticateCandidate(any()),
        ).thenAnswer(
          (_) async => Fixtures.buildHelloResult(
            trustState: DovahLinkTrustState.unpaired,
          ),
        );
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.trusted,
            credential: 'credential-a',
            shortId: 'abc123',
            displayName: 'LOCAL-HOST',
          ),
        );

        final DovahLinkPairingHandshake result = await service
            .authenticateCandidate(Uri.parse('ws://127.0.0.1:58231/'));

        expect(result.hello.trustState, DovahLinkTrustState.unpaired);
        expect(result.trustState, DovahLinkTrustState.trusted);
        verify(
          () => authenticationService.authenticateCandidate(any()),
        ).called(1);
        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.pairingAck,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: any(named: 'policy'),
          ),
        ).called(1);
      },
    );
  });

  group('Method authenticateKnownHost behaves correctly', () {
    test(
      'Method authenticateKnownHost preserves the requested relationship ID',
      () async {
        final DovahLinkHostId hostId = DovahLinkHostId(_currentHost().hostId);

        final DovahLinkPairingHandshake result = await service
            .authenticateKnownHost(hostId);

        expect(result.hello.trustState, DovahLinkTrustState.trusted);
        expect(result.trustState, DovahLinkTrustState.trusted);
        verify(() => reconnectService.connectWithInitialRetry(any())).called(1);
        verify(
          () => authenticationService.authenticateKnownHost(hostId),
        ).called(1);
      },
    );
  });

  test(
    'Method authenticateCandidate suppresses recovery results from a replaced session',
    () async {
      when(() => authenticationService.authenticateCandidate(any())).thenAnswer(
        (_) async =>
            Fixtures.buildHelloResult(trustState: DovahLinkTrustState.unpaired),
      );
      when(() => storage.load()).thenAnswer((_) async => _confirmingState());
      when(
        () => requestService.sendAndAwait(
          messageType: any(named: 'messageType'),
          payload: any(named: 'payload'),
          expectedType: any(named: 'expectedType'),
          policy: any(named: 'policy'),
        ),
      ).thenAnswer((_) async {
        currentSessionId = 'session-replaced';
        return buildPairingOutcomeEnvelope(
          outcome: PairingOutcome.trusted,
          credential: 'credential-a',
          shortId: 'abc123',
          displayName: 'LOCAL-HOST',
        );
      });

      await expectLater(
        service.authenticateCandidate(Uri.parse('ws://127.0.0.1:58231/')),
        throwsA(isA<DovahLinkConnectionException>()),
      );
      verifyNever(() => sessionTrustService.markTrusted());
    },
  );

  test(
    'Method authenticateCandidate suppresses recovery results after the active Host changes',
    () async {
      when(() => authenticationService.authenticateCandidate(any())).thenAnswer(
        (_) async =>
            Fixtures.buildHelloResult(trustState: DovahLinkTrustState.unpaired),
      );
      when(() => storage.load()).thenAnswer((_) async => _confirmingState());
      when(
        () => requestService.sendAndAwait(
          messageType: any(named: 'messageType'),
          payload: any(named: 'payload'),
          expectedType: any(named: 'expectedType'),
          policy: any(named: 'policy'),
        ),
      ).thenAnswer((_) async {
        currentHost = DovahLinkHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          hostName: 'OTHER-HOST',
          endpoint: Uri.parse('ws://127.0.0.1:58232/'),
        );
        return buildPairingOutcomeEnvelope(
          outcome: PairingOutcome.trusted,
          credential: 'credential-a',
          shortId: 'abc123',
          displayName: 'LOCAL-HOST',
        );
      });

      await expectLater(
        service.authenticateCandidate(Uri.parse('ws://127.0.0.1:58231/')),
        throwsA(isA<DovahLinkConnectionException>()),
      );
      verifyNever(() => sessionTrustService.markTrusted());
    },
  );

  group('Method requestPairing behaves correctly', () {
    test('Method requestPairing decodes the pairing_status reply', () async {
      stubSendAndAwait(
        requestService,
        Fixtures.buildEnvelope(
          messageType: ProtocolMessageType.pairingStatus,
          payload: <String, dynamic>{
            'state': 'available',
            'expiresInSeconds': 60,
          },
        ),
      );

      final PairingChallengeStatus status = await service.requestPairing();

      verify(
        () => requestService.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingStatus,
          policy: Fixtures.buildRequestPolicy(),
        ),
      ).called(1);
      expect(status.availability, PairingAvailability.available);
      expect(status.expiresInSeconds, 60);
    });

    test(
      'Method requestPairing decodes otherDevicePairing with a null expiresInSeconds',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingStatus,
            payload: <String, dynamic>{'state': 'other_device_pairing'},
          ),
        );

        final PairingChallengeStatus status = await service.requestPairing();

        expect(status.availability, PairingAvailability.otherDevicePairing);
        expect(status.expiresInSeconds, isNull);
      },
    );

    test(
      'Method requestPairing decodes unavailable with a null expiresInSeconds',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingStatus,
            payload: <String, dynamic>{
              'state': 'unavailable',
              'expiresInSeconds': null,
            },
          ),
        );

        final PairingChallengeStatus status = await service.requestPairing();

        expect(status.availability, PairingAvailability.unavailable);
        expect(status.expiresInSeconds, isNull);
      },
    );

    test(
      'Method requestPairing decodes an active inProgress challenge with its expiry',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingStatus,
            payload: <String, dynamic>{
              'state': 'in_progress',
              'expiresInSeconds': 45,
            },
          ),
        );

        final PairingChallengeStatus status = await service.requestPairing();

        expect(status.availability, PairingAvailability.inProgress);
        expect(status.expiresInSeconds, 45);
      },
    );

    test(
      'Method requestPairing decodes a pending inProgress state without an expiry',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingStatus,
            payload: <String, dynamic>{
              'state': 'in_progress',
              'expiresInSeconds': null,
            },
          ),
        );

        final PairingChallengeStatus status = await service.requestPairing();

        expect(status.availability, PairingAvailability.inProgress);
        expect(status.expiresInSeconds, isNull);
      },
    );

    test(
      'Method requestPairing throws malformed_message when pairing_status fails to decode',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingStatus,
            payload: <String, dynamic>{},
          ),
        );

        await expectLater(
          service.requestPairing(),
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
      'Method requestPairing translates an impossible pairing_status expiry into malformed_message',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingStatus,
            payload: <String, dynamic>{
              'state': 'available',
              'expiresInSeconds': null,
            },
          ),
        );

        await expectLater(
          service.requestPairing(),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException error) => error.code,
                  'code',
                  ProtocolErrorCode.malformedMessage,
                )
                .having(
                  (DovahLinkProtocolException error) => error.retryable,
                  'retryable',
                  isFalse,
                ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );

    test(
      'Method requestPairing translates an omitted required expiry into malformed_message',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingStatus,
            payload: <String, dynamic>{'state': 'in_progress'},
          ),
        );

        await expectLater(
          service.requestPairing(),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException error) => error.code,
                  'code',
                  ProtocolErrorCode.malformedMessage,
                )
                .having(
                  (DovahLinkProtocolException error) => error.retryable,
                  'retryable',
                  isFalse,
                ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );

    test(
      'Method requestPairing propagates a connection failure without changing trust state',
      () async {
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(const DovahLinkConnectionException('lost'));

        await expectLater(
          service.requestPairing(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );
  });

  group('Method requestPairingRenotify behaves correctly', () {
    test(
      'Method requestPairingRenotify decodes a renotified outcome',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.renotified,
            retryAfterSeconds: 5,
          ),
        );

        final PairingRenotifyResult result = await service
            .requestPairingRenotify();

        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.pairingRenotify,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: Fixtures.buildRequestPolicy(),
          ),
        ).called(1);
        expect(result.status, PairingRenotifyStatus.renotified);
        expect(result.retryAfterSeconds, 5);
      },
    );

    test(
      'Method requestPairingRenotify decodes a renotify_cooldown outcome with retryAfterSeconds',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.renotifyCooldown,
            retryAfterSeconds: 5,
          ),
        );

        final PairingRenotifyResult result = await service
            .requestPairingRenotify();

        expect(result.status, PairingRenotifyStatus.cooldown);
        expect(result.retryAfterSeconds, 5);
      },
    );

    test(
      'Method requestPairingRenotify decodes an already_idle outcome',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.alreadyIdle),
        );

        final PairingRenotifyResult result = await service
            .requestPairingRenotify();

        expect(result.status, PairingRenotifyStatus.alreadyIdle);
        expect(result.retryAfterSeconds, isNull);
      },
    );

    test(
      'Method requestPairingRenotify throws malformed_message for an outcome not valid for this exchange',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'credential-1',
          ),
        );

        await expectLater(
          service.requestPairingRenotify(),
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
                )
                .having(
                  (DovahLinkProtocolException e) => e.message,
                  'message',
                  'Unexpected pairing_renotify outcome: PairingOutcome.credentialIssued',
                ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );

    test(
      'Method requestPairingRenotify throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingOutcome,
            payload: <String, dynamic>{},
          ),
        );

        await expectLater(
          service.requestPairingRenotify(),
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
      'Method requestPairingRenotify propagates a protocol failure without marking trust',
      () async {
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.rateLimited,
            message: 'slow down',
            retryable: true,
          ),
        );

        await expectLater(
          service.requestPairingRenotify(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.rateLimited,
            ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );
  });

  group('Method cancelPairing behaves correctly', () {
    test('Method cancelPairing decodes a cancelled outcome', () async {
      stubSendAndAwait(
        requestService,
        buildPairingOutcomeEnvelope(outcome: PairingOutcome.cancelled),
      );

      final PairingCancelOutcome result = await service.cancelPairing();

      verify(
        () => requestService.sendAndAwait(
          messageType: ProtocolMessageType.pairingCancel,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingOutcome,
          policy: Fixtures.buildRequestPolicy(),
        ),
      ).called(1);
      expect(result.status, PairingCancelStatus.cancelled);
    });

    test('Method cancelPairing decodes an already_idle outcome', () async {
      stubSendAndAwait(
        requestService,
        buildPairingOutcomeEnvelope(outcome: PairingOutcome.alreadyIdle),
      );

      final PairingCancelOutcome result = await service.cancelPairing();

      expect(result.status, PairingCancelStatus.alreadyIdle);
    });

    test(
      'Method cancelPairing throws malformed_message for an outcome not valid for this exchange',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.renotified,
            retryAfterSeconds: 5,
          ),
        );

        await expectLater(
          service.cancelPairing(),
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
                )
                .having(
                  (DovahLinkProtocolException e) => e.message,
                  'message',
                  'Unexpected pairing_cancel outcome: PairingOutcome.renotified',
                ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );

    test(
      'Method cancelPairing throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingOutcome,
            payload: <String, dynamic>{},
          ),
        );

        await expectLater(
          service.cancelPairing(),
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
      'Method cancelPairing propagates a connection failure without touching storage',
      () async {
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(const DovahLinkConnectionException('lost'));

        await expectLater(
          service.cancelPairing(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNoStorageCalls(storage);
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );
  });

  group('Method confirmPairingCode behaves correctly', () {
    test(
      'Method confirmPairingCode does not persist a credential owned by another pending Host',
      () async {
        const String otherHostId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        when(() => storage.load()).thenAnswer(
          (_) async => PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              _currentHost().hostId: PersistedKnownHost(
                host: _currentHost(),
                credential: 'old-credential',
              ),
              otherHostId: PersistedKnownHost(
                host: DovahLinkHost(
                  hostId: otherHostId,
                  hostName: 'OTHER-HOST',
                  endpoint: Uri.parse('ws://127.0.0.1:58232/'),
                ),
              ),
            },
            pendingPairingRecovery: const PendingPairingRecovery(
              hostId: otherHostId,
              state: PairingRecoveryState.confirming,
            ),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'new-credential',
          ),
        );

        await expectLater(
          service.confirmPairingCode(code: '123456'),
          throwsA(isA<DovahLinkHostIdentityMismatchException>()),
        );

        verify(() => storage.updateState(any())).called(1);
        expect(updatedState, isNull);
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );

    test(
      'Method confirmPairingCode keeps three paired Host records independent',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        const String hostCId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
        final DovahLinkHost hostA = _currentHost();
        final DovahLinkHost hostB = DovahLinkHost(
          hostId: hostBId,
          hostName: 'HOST-B',
          endpoint: Uri.parse('ws://127.0.0.1:58232/'),
        );
        final DovahLinkHost hostC = DovahLinkHost(
          hostId: hostCId,
          hostName: 'HOST-C',
          endpoint: Uri.parse('ws://127.0.0.1:58233/'),
        );
        DovahLinkHost activeHost = hostA;
        PersistedClientState current = _state(clientId: 'client-1');
        when(() => sessionService.currentHost).thenAnswer((_) => activeHost);
        when(() => storage.load()).thenAnswer((_) async => current);
        when(() => storage.updateState(any())).thenAnswer((invocation) async {
          final PersistedClientState Function(PersistedClientState) update =
              invocation.positionalArguments.single
                  as PersistedClientState Function(PersistedClientState);
          current = update(current);
        });
        int issuedCredential = 0;
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((invocation) async {
          final ProtocolMessageType type =
              invocation.namedArguments[#messageType] as ProtocolMessageType;
          if (type == ProtocolMessageType.pairingConfirm) {
            issuedCredential++;
            return buildPairingOutcomeEnvelope(
              outcome: PairingOutcome.credentialIssued,
              credential: 'credential-$issuedCredential',
            );
          }
          final String credential =
              (invocation.namedArguments[#payload] as JsonMap)['credential']
                  as String;
          return buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.trusted,
            credential: credential,
            shortId: '12345',
          );
        });

        for (final DovahLinkHost host in <DovahLinkHost>[hostA, hostB, hostC]) {
          activeHost = host;
          await service.confirmPairingCode(code: '123456');
          await service.acknowledgeTrustedCredential();
        }

        expect(current.knownHosts.keys.toSet(), <String>{
          hostAId,
          hostBId,
          hostCId,
        });
        expect(current.knownHosts[hostAId]?.credential, 'credential-1');
        expect(current.knownHosts[hostBId]?.credential, 'credential-2');
        expect(current.knownHosts[hostCId]?.credential, 'credential-3');
        expect(current.pendingPairingRecovery, isNull);
      },
    );

    test(
      'Method confirmPairingCode persists credential, current Host, and CONFIRMING in one write',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'new-cred',
          ),
        );

        await service.confirmPairingCode(code: '123456');

        verifyInOrder([
          () => storage.updateState(any()),
          () => sessionService.associateKnownHost(
            DovahLinkHostId(_currentHost().hostId),
          ),
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(_currentHost().hostId),
            DovahLinkHostAvailability.online,
          ),
        ]);
        expect(
          updatedState,
          _state(
            clientId: 'client-1',
            credential: 'new-cred',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: _currentHost(),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode does not report online when durable persistence fails',
      () async {
        when(
          () => storage.updateState(any()),
        ).thenThrow(StateError('storage write failed'));
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'new-cred',
          ),
        );

        await expectLater(
          service.confirmPairingCode(code: '123456'),
          throwsA(
            isA<StateError>().having(
              (StateError error) => error.message,
              'message',
              'storage write failed',
            ),
          ),
        );

        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method confirmPairingCode persists the Host that issued the credential while storage is pending',
      () async {
        final DovahLinkHost hostA = _currentHost();
        final DovahLinkHost hostB = DovahLinkHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          hostName: 'OTHER-HOST',
          endpoint: Uri.parse('ws://127.0.0.1:58232/'),
        );
        DovahLinkHost currentHost = hostA;
        when(() => sessionService.currentHost).thenAnswer((_) => currentHost);
        final Completer<void> updateGate = Completer<void>();
        final GatedClientStateService gatedStorage = GatedClientStateService(
          loadedState: _state(clientId: 'client-1'),
          updateGate: updateGate,
        );
        service = PairingService(
          authenticationService: authenticationService,
          reconnectService: reconnectService,
          sessionService: sessionService,
          sessionTrustService: sessionTrustService,
          requestService: requestService,
          clientStateService: gatedStorage,
          hostAvailabilityService: hostAvailabilityService,
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'new-cred',
          ),
        );

        final Future<void> confirmation = service.confirmPairingCode(
          code: '123456',
        );
        await gatedStorage.updateStarted.future;
        verifyNever(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(hostA.hostId),
            DovahLinkHostAvailability.online,
          ),
        );
        currentHost = hostB;
        currentSessionId = 'session-2';
        updateGate.complete();

        await confirmation;
        expect(
          await gatedStorage.load(),
          _state(
            clientId: 'client-1',
            credential: 'new-cred',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: hostA,
          ),
        );
        verifyNever(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(hostA.hostId),
            DovahLinkHostAvailability.online,
          ),
        );
        verifyNever(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(hostB.hostId),
            DovahLinkHostAvailability.online,
          ),
        );
        verifyNever(
          () =>
              sessionService.associateKnownHost(DovahLinkHostId(hostA.hostId)),
        );
      },
    );

    test(
      'Method confirmPairingCode preserves recovery without associating a replacement session for the same Host ID',
      () async {
        final DovahLinkHost hostA = _currentHost();
        final Completer<void> updateGate = Completer<void>();
        final GatedClientStateService gatedStorage = GatedClientStateService(
          loadedState: _state(clientId: 'client-1'),
          updateGate: updateGate,
        );
        service = PairingService(
          authenticationService: authenticationService,
          reconnectService: reconnectService,
          sessionService: sessionService,
          sessionTrustService: sessionTrustService,
          requestService: requestService,
          clientStateService: gatedStorage,
          hostAvailabilityService: hostAvailabilityService,
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'new-cred',
          ),
        );

        final Future<void> confirmation = service.confirmPairingCode(
          code: '123456',
        );
        await gatedStorage.updateStarted.future;
        currentSessionId = 'session-2';
        updateGate.complete();

        await confirmation;
        expect(
          await gatedStorage.load(),
          _state(
            clientId: 'client-1',
            credential: 'new-cred',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: hostA,
          ),
        );
        verifyNever(
          () =>
              sessionService.associateKnownHost(DovahLinkHostId(hostA.hostId)),
        );
        verifyNever(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(hostA.hostId),
            DovahLinkHostAvailability.online,
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode does not save a credential without current Host context',
      () async {
        when(() => sessionService.currentHost).thenReturn(null);
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'new-cred',
          ),
        );

        await expectLater(
          service.confirmPairingCode(code: '123456'),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        verifyNever(() => storage.updateState(any()));
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method confirmPairingCode throws malformed_message when credential_issued carries no credential',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.credentialIssued),
        );

        await expectLater(
          service.confirmPairingCode(code: '123456'),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method confirmPairingCode throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingOutcome,
            payload: <String, dynamic>{},
          ),
        );

        await expectLater(
          service.confirmPairingCode(code: '123456'),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method confirmPairingCode sends the code and displayName in the outgoing payload',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'new-cred',
          ),
        );

        await service.confirmPairingCode(code: '123456', displayName: 'My PC');

        final JsonMap sentPayload =
            verify(
                  () => requestService.sendAndAwait(
                    messageType: ProtocolMessageType.pairingConfirm,
                    payload: captureAny(named: 'payload'),
                    expectedType: ProtocolMessageType.pairingOutcome,
                    policy: Fixtures.buildRequestPolicy(
                      retrySafe: false,
                      timeoutClass: TimeoutClass.normal,
                    ),
                  ),
                ).captured.single
                as JsonMap;
        expect(sentPayload['code'], '123456');
        expect(sentPayload['displayName'], 'My PC');
      },
    );

    test(
      'Method confirmPairingCode throws typed pairing exceptions for every valid rejected outcome',
      () async {
        const Map<PairingOutcome, int?> rejectedOutcomes =
            <PairingOutcome, int?>{
              PairingOutcome.expired: null,
              PairingOutcome.invalid: null,
              PairingOutcome.pacingLimited: 2,
              PairingOutcome.hardLimitReached: null,
              PairingOutcome.pairingInvalidated: null,
            };
        for (final MapEntry<PairingOutcome, int?> entry
            in rejectedOutcomes.entries) {
          stubSendAndAwait(
            requestService,
            buildPairingOutcomeEnvelope(
              outcome: entry.key,
              attemptsRemaining: entry.key == PairingOutcome.invalid ? 4 : null,
              retryAfterSeconds: entry.value,
            ),
          );

          await expectLater(
            service.confirmPairingCode(code: '123456'),
            throwsA(
              isA<DovahLinkPairingException>()
                  .having(
                    (DovahLinkPairingException error) => error.outcome,
                    'outcome',
                    entry.key,
                  )
                  .having(
                    (DovahLinkPairingException error) =>
                        error.retryAfterSeconds,
                    'retryAfterSeconds',
                    entry.value,
                  )
                  .having(
                    (DovahLinkPairingException error) =>
                        error.attemptsRemaining,
                    'attemptsRemaining',
                    entry.key == PairingOutcome.invalid ? 4 : null,
                  ),
            ),
          );
          verifyNever(() => sessionTrustService.markTrusted());
        }
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method confirmPairingCode throws malformed_message for an outcome from another exchange',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.trusted,
            credential: 'credential-1',
            shortId: '12345',
          ),
        );

        await expectLater(
          service.confirmPairingCode(code: '123456'),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException error) => error.code,
                  'code',
                  ProtocolErrorCode.malformedMessage,
                )
                .having(
                  (DovahLinkProtocolException error) => error.retryable,
                  'retryable',
                  isFalse,
                )
                .having(
                  (DovahLinkProtocolException error) => error.message,
                  'message',
                  'Unexpected pairing_confirm outcome: PairingOutcome.trusted',
                ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method confirmPairingCode propagates a connection failure without touching storage',
      () async {
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(const DovahLinkConnectionException('lost'));

        await expectLater(
          service.confirmPairingCode(code: '123456'),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNoStorageCalls(storage);
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );
  });

  group('Method confirmPairingCodeAndAcknowledge behaves correctly', () {
    test(
      'Method confirmPairingCodeAndAcknowledge clears the repair hint when acknowledgement fails',
      () async {
        final PersistedClientState initialState = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            _currentHost().hostId: PersistedKnownHost(
              host: _currentHost(),
              pairingRequired: true,
            ),
          },
        );
        when(
          () => storage.load(),
        ).thenAnswer((_) async => updatedState ?? initialState);
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((invocation) async {
          final ProtocolMessageType messageType =
              invocation.namedArguments[#messageType] as ProtocolMessageType;
          if (messageType == ProtocolMessageType.pairingAck) {
            throw const DovahLinkConnectionException('lost');
          }
          return buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'issued-credential',
          );
        });

        await expectLater(
          service.confirmPairingCodeAndAcknowledge(code: '123456'),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        expect(
          updatedState?.knownHosts[_currentHost().hostId]?.credential,
          'issued-credential',
        );
        expect(
          updatedState?.knownHosts[_currentHost().hostId]?.pairingRequired,
          isFalse,
        );
        expect(
          updatedState?.pendingPairingRecovery?.state,
          PairingRecoveryState.confirming,
        );
        verify(() => storage.updateState(any())).called(1);
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );

    test(
      'Method confirmPairingCodeAndAcknowledge keeps protocol sequencing in the SDK',
      () async {
        final List<ProtocolMessageType> sent = <ProtocolMessageType>[];
        final DovahLinkHost otherHost = DovahLinkHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          hostName: 'OTHER-HOST',
          endpoint: Uri.parse('ws://127.0.0.1:58232/'),
        );
        final PersistedClientState initialState = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            _currentHost().hostId: PersistedKnownHost(
              host: _currentHost(),
              pairingRequired: true,
            ),
            otherHost.hostId: PersistedKnownHost(
              host: otherHost,
              credential: 'other-credential',
              pairingRequired: true,
            ),
          },
        );
        when(
          () => storage.load(),
        ).thenAnswer((_) async => updatedState ?? initialState);
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((invocation) async {
          final ProtocolMessageType messageType =
              invocation.namedArguments[#messageType] as ProtocolMessageType;
          sent.add(messageType);
          return buildPairingOutcomeEnvelope(
            outcome: messageType == ProtocolMessageType.pairingConfirm
                ? PairingOutcome.credentialIssued
                : PairingOutcome.trusted,
            credential: 'issued-credential',
            shortId: messageType == ProtocolMessageType.pairingConfirm
                ? null
                : 'abc123',
            displayName: 'My PC',
          );
        });

        await service.confirmPairingCodeAndAcknowledge(
          code: '123456',
          displayName: 'My PC',
        );

        expect(sent, <ProtocolMessageType>[
          ProtocolMessageType.pairingConfirm,
          ProtocolMessageType.pairingAck,
        ]);
        expect(updatedState?.pendingPairingRecovery, isNull);
        expect(
          updatedState?.knownHosts[_currentHost().hostId]?.credential,
          'issued-credential',
        );
        expect(
          updatedState?.knownHosts[_currentHost().hostId]?.pairingRequired,
          isFalse,
        );
        expect(
          updatedState?.knownHosts[otherHost.hostId]?.credential,
          'other-credential',
        );
        expect(
          updatedState?.knownHosts[otherHost.hostId]?.pairingRequired,
          isTrue,
        );
        verify(() => sessionTrustService.markTrusted()).called(1);
      },
    );

    test(
      'Method confirmPairingCodeAndAcknowledge preserves recovery and rejects a replacement session with the same Host ID',
      () async {
        final DovahLinkHost hostA = _currentHost();
        final Completer<void> updateGate = Completer<void>();
        final GatedClientStateService gatedStorage = GatedClientStateService(
          loadedState: _state(clientId: 'client-1'),
          updateGate: updateGate,
        );
        service = PairingService(
          authenticationService: authenticationService,
          reconnectService: reconnectService,
          sessionService: sessionService,
          sessionTrustService: sessionTrustService,
          requestService: requestService,
          clientStateService: gatedStorage,
          hostAvailabilityService: hostAvailabilityService,
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'credential-A',
          ),
        );

        final Future<void> confirmation = service
            .confirmPairingCodeAndAcknowledge(code: '123456');
        await gatedStorage.updateStarted.future;
        currentSessionId = 'session-2';
        updateGate.complete();

        await expectLater(
          confirmation,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(
          await gatedStorage.load(),
          _state(
            clientId: 'client-1',
            credential: 'credential-A',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: hostA,
          ),
        );
        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.pairingConfirm,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: any(named: 'policy'),
          ),
        ).called(1);
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.pairingAck,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNever(() => sessionService.associateKnownHost(any()));
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );
  });

  group('Method acknowledgeTrustedCredential behaves correctly', () {
    test(
      'Method acknowledgeTrustedCredential does not send Host A credential after the Host changes during storage load',
      () async {
        final Completer<PersistedClientState> loadGate =
            Completer<PersistedClientState>();
        final Completer<void> loadStarted = Completer<void>();
        when(() => storage.load()).thenAnswer((_) {
          loadStarted.complete();
          return loadGate.future;
        });

        final Future<void> acknowledgement = service
            .acknowledgeTrustedCredential();
        await loadStarted.future;
        currentHost = DovahLinkHost(
          hostId: '81f6cc90-3a88-40c7-8351-104d4a36c971',
          hostName: 'HOST-B',
          endpoint: Uri.parse('ws://127.0.0.1:58232/'),
        );
        currentSessionId = 'session-2';
        loadGate.complete(_confirmingState());

        await expectLater(
          acknowledgement,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(await loadGate.future, _confirmingState());
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(() => storage.updateState(any()));
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNever(() => sessionService.associateKnownHost(any()));
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method acknowledgeTrustedCredential rejects a changed session with the same Host ID before sending the credential',
      () async {
        final Completer<PersistedClientState> loadGate =
            Completer<PersistedClientState>();
        final Completer<void> loadStarted = Completer<void>();
        when(() => storage.load()).thenAnswer((_) {
          loadStarted.complete();
          return loadGate.future;
        });

        final Future<void> acknowledgement = service
            .acknowledgeTrustedCredential();
        await loadStarted.future;
        currentSessionId = 'session-2';
        loadGate.complete(_confirmingState());

        await expectLater(
          acknowledgement,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        expect(await loadGate.future, _confirmingState());
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(() => storage.updateState(any()));
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNever(() => sessionService.associateKnownHost(any()));
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method acknowledgeTrustedCredential does not trust a replacement session after the acknowledgement reply',
      () async {
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((_) async {
          currentSessionId = 'session-2';
          return buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.trusted,
            credential: 'credential-a',
            shortId: '12345',
          );
        });

        await service.acknowledgeTrustedCredential();

        verify(() => storage.updateState(any())).called(1);
        expect(updatedState?.pendingPairingRecovery, isNull);
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNever(() => sessionService.associateKnownHost(any()));
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method acknowledgeTrustedCredential does not send a credential for another Host recovery',
      () async {
        const String otherHostId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        when(() => sessionService.currentHost).thenReturn(
          DovahLinkHost(
            hostId: otherHostId,
            hostName: 'OTHER-HOST',
            endpoint: Uri.parse('ws://127.0.0.1:58232/'),
          ),
        );
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());

        await expectLater(
          service.acknowledgeTrustedCredential(),
          throwsA(isA<DovahLinkHostIdentityMismatchException>()),
        );

        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNoStateMutations(storage);
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );

    test(
      'Method acknowledgeTrustedCredential marks the session trusted and clears the recovery state on a trusted outcome',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => _state(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: _currentHost(),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.trusted,
            credential: 'cred',
            shortId: '12345',
          ),
        );

        await service.acknowledgeTrustedCredential();

        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.pairingAck,
            payload: <String, dynamic>{'credential': 'cred'},
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: Fixtures.buildRequestPolicy(),
          ),
        ).called(1);
        verify(() => sessionTrustService.markTrusted()).called(1);
        verify(() => storage.updateState(any())).called(1);
        verifyInOrder([
          () => sessionService.associateKnownHost(
            DovahLinkHostId(_currentHost().hostId),
          ),
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(_currentHost().hostId),
            DovahLinkHostAvailability.online,
          ),
        ]);
        expect(
          updatedState,
          _state(
            clientId: 'client-1',
            credential: 'cred',
            knownHost: _currentHost(),
          ),
        );
      },
    );

    test(
      'Method acknowledgeTrustedCredential also marks the session trusted on an already_trusted outcome',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => _state(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: _currentHost(),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.alreadyTrusted,
            credential: 'cred',
            shortId: '12345',
          ),
        );

        await service.acknowledgeTrustedCredential();

        verify(() => sessionTrustService.markTrusted()).called(1);
        verify(() => storage.updateState(any())).called(1);
        expect(
          updatedState,
          _state(
            clientId: 'client-1',
            credential: 'cred',
            knownHost: _currentHost(),
          ),
        );
      },
    );

    test(
      'Method acknowledgeTrustedCredential keeps the owning Host metadata',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => _state(
            clientId: 'client-1',
            credential: 'legacy-credential',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: _currentHost(),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.trusted,
            credential: 'legacy-credential',
            shortId: '12345',
          ),
        );

        await service.acknowledgeTrustedCredential();

        verify(() => storage.updateState(any())).called(1);
        expect(
          updatedState,
          _state(
            clientId: 'client-1',
            credential: 'legacy-credential',
            knownHost: _currentHost(),
          ),
        );
      },
    );

    test(
      'Method acknowledgeTrustedCredential does not mark trust without current Host context',
      () async {
        when(() => sessionService.currentHost).thenReturn(null);
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.trusted,
            credential: 'cred',
            shortId: '12345',
          ),
        );

        await expectLater(
          service.acknowledgeTrustedCredential(),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential never marks the session trusted and throws '
      'DovahLinkPairingException for a rejected acknowledgement',
      () async {
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.pendingNotFound),
        );

        await expectLater(
          service.acknowledgeTrustedCredential(),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException e) => e.outcome,
              'outcome',
              PairingOutcome.pendingNotFound,
            ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStateMutations(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential exposes pairing_invalidated without marking trust',
      () async {
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.pairingInvalidated,
          ),
        );

        await expectLater(
          service.acknowledgeTrustedCredential(),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException e) => e.outcome,
              'outcome',
              PairingOutcome.pairingInvalidated,
            ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStateMutations(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential throws malformed_message for an outcome from another exchange',
      () async {
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.expired),
        );

        await expectLater(
          service.acknowledgeTrustedCredential(),
          throwsA(
            isA<DovahLinkProtocolException>()
                .having(
                  (DovahLinkProtocolException error) => error.code,
                  'code',
                  ProtocolErrorCode.malformedMessage,
                )
                .having(
                  (DovahLinkProtocolException error) => error.retryable,
                  'retryable',
                  isFalse,
                )
                .having(
                  (DovahLinkProtocolException error) => error.message,
                  'message',
                  'Unexpected pairing_ack outcome: PairingOutcome.expired',
                ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStateMutations(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential throws malformed_message when pairing_outcome fails to decode',
      () async {
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingOutcome,
            payload: <String, dynamic>{},
          ),
        );

        await expectLater(
          service.acknowledgeTrustedCredential(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStateMutations(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential propagates a connection failure without marking trust or '
      'touching storage',
      () async {
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(const DovahLinkConnectionException('lost'));

        await expectLater(
          service.acknowledgeTrustedCredential(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStateMutations(storage);
      },
    );
  });

  group('Method recoverPendingPairing behaves correctly', () {
    test(
      'Method recoverPendingPairing fails closed when another Host is connected',
      () async {
        const String otherHostId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        when(() => sessionService.currentHost).thenReturn(
          DovahLinkHost(
            hostId: otherHostId,
            hostName: 'OTHER-HOST',
            endpoint: Uri.parse('ws://127.0.0.1:58232/'),
          ),
        );
        when(() => storage.load()).thenAnswer((_) async => _confirmingState());

        await expectLater(
          service.recoverPendingPairing(),
          throwsA(isA<DovahLinkHostIdentityMismatchException>()),
        );

        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNoStateMutations(storage);
        verifyNever(() => sessionTrustService.markTrusted());
      },
    );

    test(
      'Method recoverPendingPairing is a no-op returning unpaired when no confirmation is outstanding',
      () async {
        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
      },
    );

    test(
      'Method recoverPendingPairing is a no-op returning unpaired when confirming but no credential is stored',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
      },
    );

    test(
      'Method recoverPendingPairing retries the stored credential and returns trusted on success',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => _state(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: _currentHost(),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.trusted,
            credential: 'stored-cred',
            shortId: '12345',
          ),
        );

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.trusted);
        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.pairingAck,
            payload: <String, dynamic>{'credential': 'stored-cred'},
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: Fixtures.buildRequestPolicy(),
          ),
        ).called(1);
        verify(() => sessionTrustService.markTrusted()).called(1);
        verify(() => storage.updateState(any())).called(1);
        expect(
          updatedState,
          _state(
            clientId: 'client-1',
            credential: 'stored-cred',
            knownHost: _currentHost(),
          ),
        );
      },
    );

    test(
      'Method recoverPendingPairing discards the credential and resets to unpaired when the host '
      'reports pending_not_found',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => _state(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: _currentHost(),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.pendingNotFound),
        );

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        verify(() => storage.updateState(any())).called(1);
        expect(
          updatedState,
          _state(clientId: 'client-1', knownHost: _currentHost()),
        );
      },
    );

    test(
      'Method recoverPendingPairing discards the credential and resets to unpaired when the host '
      'reports pairing_invalidated',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => _state(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
            knownHost: _currentHost(),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.pairingInvalidated,
          ),
        );

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        verify(() => storage.updateState(any())).called(1);
        expect(
          updatedState,
          _state(clientId: 'client-1', knownHost: _currentHost()),
        );
      },
    );

    test(
      'Method recoverPendingPairing leaves the CONFIRMING state untouched and rethrows for any other failure',
      () async {
        final PersistedClientState confirmingState = _state(
          clientId: 'client-1',
          credential: 'stored-cred',
          recoveryState: PairingRecoveryState.confirming,
          knownHost: _currentHost(),
        );
        when(() => storage.load()).thenAnswer((_) async => confirmingState);
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        await expectLater(
          service.recoverPendingPairing(),
          throwsA(isA<DovahLinkPairingException>()),
        );
        verifyNever(() => storage.updateState(any()));
        final PersistedClientState stored = await storage.load();
        expect(
          stored.knownHosts.values.single.credential,
          confirmingState.knownHosts.values.single.credential,
        );
        expect(
          stored.pendingPairingRecovery?.state,
          PairingRecoveryState.confirming,
        );
        expect(stored.knownHosts.values.single.host, _currentHost());
      },
    );
  });
}
