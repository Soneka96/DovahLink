import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_not_found_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_cache.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../fixtures/fixtures.dart';

/// Mocks session lifecycle so these tests can inspect authentication's delegated calls.
class MockSessionService extends Mock implements ISessionService {}

/// Mocks session admission so these tests can verify [AuthenticationService]'s arguments.
class MockSessionAdmissionService extends Mock
    implements ISessionAdmissionService {}

/// Mocks request transmission so these tests can isolate [AuthenticationService].
class MockRequestService extends Mock implements IRequestService {}

/// Mocks persisted-state ownership so these tests can inspect authentication updates.
class MockClientStateService extends Mock implements IClientStateService {}

/// Mocks the single owner of runtime Known Host availability.
class MockHostAvailabilityService extends Mock
    implements IHostAvailabilityService {}

/// Mocks client ID resolution so these tests can verify [AuthenticationService] uses its result.
class MockClientIdResolver extends Mock implements ClientIdResolver {}

/// Mocks shared identity state so these tests can verify [AuthenticationService]'s cache calls.
class MockClientIdCache extends Mock implements ClientIdCache {}

/// Builds a decoded `hello_ack` reply [Envelope] from the shared envelope fixture.
Envelope buildHelloAckEnvelope({
  String? sessionId = 'session-1',
  String hostVersion = '0.5.0',
  String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea',
  String hostName = 'Soneka-Desktop',
  ClientIdentityKind kind = ClientIdentityKind.unpaired,
  String? clientId = 'client-1',
}) => Fixtures.buildEnvelope(
  messageType: ProtocolMessageType.helloAck,
  sessionId: sessionId,
  clientId: clientId,
  payload: <String, dynamic>{
    'hostVersion': hostVersion,
    'hostId': hostId,
    'hostName': hostName,
    'clientIdentityKind': kind == ClientIdentityKind.paired
        ? 'paired'
        : 'unpaired',
  },
);

/// Stubs [IRequestService.sendAndAwait] to answer with [envelope], matching any call.
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

/// Runs authentication service behavior tests.
void main() {
  late MockSessionService sessionService;
  late MockSessionAdmissionService sessionAdmissionService;
  late MockRequestService requestService;
  late MockClientStateService storage;
  late MockHostAvailabilityService hostAvailabilityService;
  late PersistedClientState? updatedState;
  late MockClientIdResolver clientIdResolver;
  late MockClientIdCache clientIdCache;
  late AuthenticationService service;

  setUpAll(() {
    registerFallbackValue(ProtocolMessageType.hello);
    registerFallbackValue(
      Fixtures.buildRequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    registerFallbackValue(Uri.parse('ws://127.0.0.1:0/'));
    registerFallbackValue(DovahLinkTrustState.unpaired);
    registerFallbackValue(
      DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
    );
    registerFallbackValue(DovahLinkHostAvailability.unknown);
    registerFallbackValue(
      DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'LOCAL-HOST',
        endpoint: Uri.parse('ws://127.0.0.1:58231/'),
      ),
    );
    registerFallbackValue(Fixtures.buildPersistedClientState());
    registerFallbackValue((PersistedClientState state) => state);
  });

  setUp(() {
    sessionService = MockSessionService();
    sessionAdmissionService = MockSessionAdmissionService();
    requestService = MockRequestService();
    storage = MockClientStateService();
    hostAvailabilityService = MockHostAvailabilityService();
    updatedState = null;
    clientIdResolver = MockClientIdResolver();
    clientIdCache = MockClientIdCache();
    when(
      () =>
          sessionService.connect(any(), knownHostId: any(named: 'knownHostId')),
    ).thenAnswer((_) async {});
    when(
      () => sessionService.disconnect(
        orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
      ),
    ).thenAnswer((_) async {});
    when(
      () => sessionService.connectionState,
    ).thenReturn(DovahLinkConnectionState.disconnected);
    when(() => sessionService.currentTrustState).thenReturn(null);
    when(
      () => sessionService.currentHost,
    ).thenReturn(Fixtures.buildDovahLinkHost());
    when(() => sessionService.currentKnownHostId).thenReturn(null);
    when(() => sessionService.associateKnownHost(any())).thenReturn(null);
    when(
      () => sessionService.currentEndpoint,
    ).thenReturn(Uri.parse('ws://127.0.0.1:58231/'));
    when(
      () => sessionAdmissionService.admitSession(
        sessionId: any(named: 'sessionId'),
        trustState: any(named: 'trustState'),
        currentHost: any(named: 'currentHost'),
      ),
    ).thenAnswer((_) {});
    when(() => storage.load()).thenAnswer(
      (_) async => Fixtures.buildPersistedClientState(clientId: 'client-1'),
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
    when(
      () => clientIdResolver.resolve(any()),
    ).thenAnswer((_) async => 'client-1');
    service = AuthenticationService(
      sessionService: sessionService,
      sessionAdmissionService: sessionAdmissionService,
      requestService: requestService,
      clientStateService: storage,
      hostAvailabilityService: hostAvailabilityService,
      clientIdResolver: clientIdResolver,
      clientIdCache: clientIdCache,
    );
  });

  group('Method hello behaves correctly', () {
    test(
      'Method hello reports cancellation when storage fails after disconnect',
      () async {
        final Completer<PersistedClientState> storageLoad =
            Completer<PersistedClientState>();
        when(() => storage.load()).thenAnswer((_) => storageLoad.future);

        final Future<HelloResult> authentication = service.hello();
        service.cancelPendingAuthentication();
        storageLoad.completeError(StateError('storage failed'));

        await expectLater(
          authentication,
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException error) => error.message,
              'message',
              'Authentication was cancelled by disconnect.',
            ),
          ),
        );
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method hello preserves a current-generation storage failure',
      () async {
        when(() => storage.load()).thenThrow(StateError('storage failed'));

        await expectLater(
          service.hello(),
          throwsA(
            isA<StateError>().having(
              (StateError error) => error.message,
              'message',
              'storage failed',
            ),
          ),
        );
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
      },
    );

    test(
      'Method hello reports cancellation when client ID resolution finishes after disconnect',
      () async {
        final Completer<String> idResolution = Completer<String>();
        final Completer<void> resolutionStarted = Completer<void>();
        when(() => clientIdResolver.resolve(any())).thenAnswer((_) {
          resolutionStarted.complete();
          return idResolution.future;
        });

        final Future<HelloResult> authentication = service.hello();
        await resolutionStarted.future;
        service.cancelPendingAuthentication();
        idResolution.complete('generated-client');

        await expectLater(
          authentication,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => clientIdCache.set(any()));
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method hello preserves a current-generation client ID resolution failure',
      () async {
        when(
          () => clientIdResolver.resolve(any()),
        ).thenThrow(StateError('client ID resolution failed'));

        await expectLater(
          service.hello(),
          throwsA(
            isA<StateError>().having(
              (StateError error) => error.message,
              'message',
              'client ID resolution failed',
            ),
          ),
        );
        verifyNever(() => clientIdCache.set(any()));
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method hello reports cancellation when Host I/O fails after disconnect',
      () async {
        final Completer<Envelope> hostReply = Completer<Envelope>();
        final Completer<void> requestStarted = Completer<void>();
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((_) {
          requestStarted.complete();
          return hostReply.future;
        });

        final Future<HelloResult> authentication = service.hello();
        await requestStarted.future;
        service.cancelPendingAuthentication();
        hostReply.completeError(StateError('Host request failed'));

        await expectLater(
          authentication,
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException error) => error.message,
              'message',
              'Authentication was cancelled by disconnect.',
            ),
          ),
        );
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
      },
    );

    test(
      'Method hello does not admit a late Host reply after disconnect',
      () async {
        final Completer<Envelope> hostReply = Completer<Envelope>();
        final Completer<void> requestStarted = Completer<void>();
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((_) {
          requestStarted.complete();
          return hostReply.future;
        });

        final Future<HelloResult> authentication = service.hello();
        await requestStarted.future;
        service.cancelPendingAuthentication();
        hostReply.complete(buildHelloAckEnvelope());

        await expectLater(
          authentication,
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method hello reports cancellation if disconnect overlaps failed-request cleanup',
      () async {
        final Completer<void> disconnectStarted = Completer<void>();
        final Completer<void> disconnectCompleted = Completer<void>();
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(StateError('Host request failed'));
        when(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).thenAnswer((_) {
          disconnectStarted.complete();
          return disconnectCompleted.future;
        });

        final Future<HelloResult> authentication = service.hello();
        await disconnectStarted.future;
        service.cancelPendingAuthentication();
        disconnectCompleted.complete();

        await expectLater(
          authentication,
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException error) => error.message,
              'message',
              'Authentication was cancelled by disconnect.',
            ),
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test('Method hello sends hello and admits the decoded session', () async {
      stubSendAndAwait(
        requestService,
        buildHelloAckEnvelope(
          sessionId: 'session-1',
          hostVersion: '0.5.0',
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          hostName: 'LIVINGROOM-PC',
          kind: ClientIdentityKind.unpaired,
        ),
      );

      final HelloResult result = await service.hello();

      verifyInOrder([
        () => requestService.sendAndAwait(
          messageType: ProtocolMessageType.hello,
          payload: any(named: 'payload'),
          expectedType: ProtocolMessageType.helloAck,
          policy: any(named: 'policy'),
        ),
        () => sessionAdmissionService.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
          currentHost: DovahLinkHost(
            hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            hostName: 'LIVINGROOM-PC',
            endpoint: Uri.parse('ws://127.0.0.1:58231/'),
          ),
        ),
      ]);
      verifyNever(
        () => sessionService.disconnect(
          orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
        ),
      );
      expect(result.hostId, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
      expect(result.hostName, 'LIVINGROOM-PC');
      expect(result.hostVersion, '0.5.0');
      expect(result.trustState, DovahLinkTrustState.unpaired);
    });

    test(
      'Method hello refreshes mutable metadata for the same trusted Host',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String credential = 'credential-1';
        final PersistedClientState knownState = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            hostId: PersistedKnownHost(
              host: DovahLinkHost(
                hostId: hostId,
                hostName: 'OLD-NAME',
                endpoint: Uri.parse('ws://127.0.0.1:58230/'),
              ),
              credential: credential,
              pairingRequired: true,
            ),
          },
        );
        when(() => storage.load()).thenAnswer((_) async => knownState);
        when(
          () => sessionService.currentEndpoint,
        ).thenReturn(Uri.parse('ws://127.0.0.1:58231/'));
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: hostId.toUpperCase(),
            hostName: 'NEW-NAME',
            kind: ClientIdentityKind.paired,
          ),
        );

        await service.authenticateKnownHost(DovahLinkHostId(hostId));

        verify(() => storage.updateState(any())).called(1);
        verify(
          () => sessionService.associateKnownHost(DovahLinkHostId(hostId)),
        ).called(1);
        verify(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(hostId),
            DovahLinkHostAvailability.online,
          ),
        ).called(1);
        expect(
          updatedState,
          PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostId: PersistedKnownHost(
                host: DovahLinkHost(
                  hostId: hostId,
                  hostName: 'NEW-NAME',
                  endpoint: Uri.parse('ws://127.0.0.1:58231/'),
                ),
                credential: credential,
                pairingRequired: false,
              ),
            },
          ),
        );
      },
    );

    test(
      'Method hello fails closed when a trusted session reports another Host ID',
      () async {
        const String knownHostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String reportedHostId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
        when(() => storage.load()).thenAnswer(
          (_) async => PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              knownHostId: PersistedKnownHost(
                host: DovahLinkHost(
                  hostId: knownHostId,
                  hostName: 'KNOWN-HOST',
                  endpoint: Uri.parse('ws://127.0.0.1:58231/'),
                ),
                credential: 'credential-1',
              ),
            },
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: reportedHostId,
            kind: ClientIdentityKind.paired,
          ),
        );

        await expectLater(
          service.authenticateKnownHost(DovahLinkHostId(knownHostId)),
          throwsA(
            isA<DovahLinkHostIdentityMismatchException>()
                .having(
                  (error) => error.knownHostId,
                  'knownHostId',
                  knownHostId,
                )
                .having(
                  (error) => error.reportedHostId,
                  'reportedHostId',
                  reportedHostId,
                ),
          ),
        );

        verifyNever(() => storage.updateState(any()));
        expect(updatedState, isNull);
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello rejects a confirming Host ID mismatch before session admission',
      () async {
        const String knownHostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String reportedHostId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
        final PersistedClientState pendingState = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            knownHostId: PersistedKnownHost(
              host: DovahLinkHost(
                hostId: knownHostId,
                hostName: 'KNOWN-HOST',
                endpoint: Uri.parse('ws://127.0.0.1:58231/'),
              ),
              credential: 'pending-credential',
            ),
          },
          pendingPairingRecovery: const PendingPairingRecovery(
            hostId: knownHostId,
            state: PairingRecoveryState.confirming,
          ),
        );
        when(() => storage.load()).thenAnswer((_) async => pendingState);
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: reportedHostId,
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await expectLater(
          service.authenticateKnownHost(DovahLinkHostId(knownHostId)),
          throwsA(
            isA<DovahLinkHostIdentityMismatchException>()
                .having(
                  (error) => error.knownHostId,
                  'knownHostId',
                  knownHostId,
                )
                .having(
                  (error) => error.reportedHostId,
                  'reportedHostId',
                  reportedHostId,
                ),
          ),
        );

        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
        verifyNever(() => storage.updateState(any()));
        verify(() => storage.load()).called(1);
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello admits a confirming session from the stored Known Host',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              '81869993-955c-4ba3-a7d0-d35ca86078ea': PersistedKnownHost(
                host: DovahLinkHost(
                  hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
                  hostName: 'KNOWN-HOST',
                  endpoint: Uri.parse('ws://127.0.0.1:58231/'),
                ),
                credential: 'pending-credential',
              ),
            },
            pendingPairingRecovery: const PendingPairingRecovery(
              hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
              state: PairingRecoveryState.confirming,
            ),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(kind: ClientIdentityKind.unpaired),
        );

        final DovahLinkHostId knownHostId = DovahLinkHostId(
          '81869993-955c-4ba3-a7d0-d35ca86078ea',
        );
        await service.authenticateKnownHost(knownHostId);

        verify(
          () => sessionService.connect(
            Uri.parse('ws://127.0.0.1:58231/'),
            knownHostId: knownHostId,
          ),
        ).called(1);

        verify(
          () => sessionAdmissionService.admitSession(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.unpaired,
            currentHost: any(named: 'currentHost'),
          ),
        ).called(1);
        verifyNever(() => storage.updateState(any()));
      },
    );

    test(
      'Method authenticateKnownHost recognizes uppercase persisted recovery and sends no credential',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(() => storage.load()).thenAnswer(
          (_) async => PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostId: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostId),
                credential: 'pending-credential',
              ),
            },
            pendingPairingRecovery: const PendingPairingRecovery(
              hostId: '81869993-955C-4BA3-A7D0-D35CA86078EA',
              state: PairingRecoveryState.confirming,
            ),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(kind: ClientIdentityKind.unpaired),
        );

        final HelloResult result = await service.authenticateKnownHost(
          DovahLinkHostId(hostId),
        );

        final JsonMap sentPayload =
            verify(
                  () => requestService.sendAndAwait(
                    messageType: ProtocolMessageType.hello,
                    payload: captureAny(named: 'payload'),
                    expectedType: ProtocolMessageType.helloAck,
                    policy: any(named: 'policy'),
                  ),
                ).captured.single
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
        expect(result.hostId, hostId);
        expect((await storage.load()).pendingPairingRecovery?.hostId, hostId);
        verify(
          () => sessionAdmissionService.admitSession(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.unpaired,
            currentHost: DovahLinkHost(
              hostId: hostId,
              hostName: 'Soneka-Desktop',
              endpoint: Uri.parse('ws://127.0.0.1:58231/'),
            ),
          ),
        ).called(1);
      },
    );

    test(
      'Method authenticateKnownHost accepts uppercase reported Host ID during pending recovery',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(() => storage.load()).thenAnswer(
          (_) async => PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostId: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostId),
                credential: 'pending-credential',
              ),
            },
            pendingPairingRecovery: const PendingPairingRecovery(
              hostId: hostId,
              state: PairingRecoveryState.confirming,
            ),
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: '81869993-955C-4BA3-A7D0-D35CA86078EA',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        final HelloResult result = await service.authenticateKnownHost(
          DovahLinkHostId(hostId),
        );

        expect(result.hostId, hostId);
        verify(
          () => sessionAdmissionService.admitSession(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.unpaired,
            currentHost: DovahLinkHost(
              hostId: hostId,
              hostName: 'Soneka-Desktop',
              endpoint: Uri.parse('ws://127.0.0.1:58231/'),
            ),
          ),
        ).called(1);
        verifyNever(() => storage.updateState(any()));
      },
    );

    test('Method hello does not persist an unpaired Host claim', () async {
      when(() => storage.load()).thenAnswer(
        (_) async => PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            '81869993-955c-4ba3-a7d0-d35ca86078ea': PersistedKnownHost(
              host: DovahLinkHost(
                hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
                hostName: 'KNOWN-HOST',
                endpoint: Uri.parse('ws://127.0.0.1:58230/'),
              ),
            ),
          },
        ),
      );
      stubSendAndAwait(
        requestService,
        buildHelloAckEnvelope(
          hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          hostName: 'UNPAIRED-CANDIDATE',
          kind: ClientIdentityKind.unpaired,
        ),
      );

      await service.hello();

      verifyNever(() => storage.updateState(any()));
      verify(
        () => sessionAdmissionService.admitSession(
          sessionId: 'session-1',
          trustState: DovahLinkTrustState.unpaired,
          currentHost: DovahLinkHost(
            hostId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            hostName: 'UNPAIRED-CANDIDATE',
            endpoint: Uri.parse('ws://127.0.0.1:58231/'),
          ),
        ),
      ).called(1);
    });

    test(
      'Method authenticateKnownHost disconnects when metadata persistence fails',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-1',
          ),
        );
        when(
          () => storage.updateState(any()),
        ).thenThrow(StateError('reconciliation save failed'));
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(kind: ClientIdentityKind.paired),
        );

        await expectLater(
          service.authenticateKnownHost(
            DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
          ),
          throwsA(isA<StateError>()),
        );

        verify(() => storage.updateState(any())).called(1);
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello closes and rejects a newer Host before session admission',
      () async {
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostVersion: '0.6.0',
            kind: ClientIdentityKind.paired,
          ),
        );

        await expectLater(
          service.hello(),
          throwsA(
            isA<DovahLinkCompatibilityException>().having(
              (DovahLinkCompatibilityException error) => error.failure,
              'failure',
              HostVersionCompatibilityFailure.hostTooNew,
            ),
          ),
        );

        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello rejects released Host 0.4.0 before session admission',
      () async {
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostVersion: '0.4.0',
            kind: ClientIdentityKind.paired,
          ),
        );

        await expectLater(
          service.hello(),
          throwsA(
            isA<DovahLinkCompatibilityException>()
                .having(
                  (DovahLinkCompatibilityException error) => error.hostVersion,
                  'hostVersion',
                  '0.4.0',
                )
                .having(
                  (DovahLinkCompatibilityException error) =>
                      error.supportedHostVersionRange,
                  'supportedHostVersionRange',
                  '0.5.x',
                )
                .having(
                  (DovahLinkCompatibilityException error) => error.failure,
                  'failure',
                  HostVersionCompatibilityFailure.hostTooOld,
                ),
          ),
        );

        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello presents unpaired when no credential is stored',
      () async {
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await service.hello();

        final JsonMap sentPayload =
            verify(
                  () => requestService.sendAndAwait(
                    messageType: ProtocolMessageType.hello,
                    payload: captureAny(named: 'payload'),
                    expectedType: ProtocolMessageType.helloAck,
                    policy: any(named: 'policy'),
                  ),
                ).captured.single
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
      },
    );

    test(
      'Method hello resolves clientId from the loaded persisted state and exposes it as its own',
      () async {
        final PersistedClientState loaded = Fixtures.buildPersistedClientState(
          clientId: 'existing-client',
        );
        when(() => storage.load()).thenAnswer((_) async => loaded);
        when(
          () => clientIdResolver.resolve(any()),
        ).thenAnswer((_) async => 'existing-client');
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await service.hello();

        verify(() => clientIdResolver.resolve(loaded)).called(1);
        verify(() => clientIdCache.set('existing-client')).called(1);
      },
    );

    test(
      'Method authenticateKnownHost presents only that Host credential',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'good-cred',
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.paired,
          ),
        );

        await service.authenticateKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );

        final JsonMap sentPayload =
            verify(
                  () => requestService.sendAndAwait(
                    messageType: ProtocolMessageType.hello,
                    payload: captureAny(named: 'payload'),
                    expectedType: ProtocolMessageType.helloAck,
                    policy: any(named: 'policy'),
                  ),
                ).captured.single
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{
          'method': 'trusted_device_credential',
          'token': 'good-cred',
        });
      },
    );

    test(
      'Method hello presents unpaired while pairing confirmation is pending',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            hostVersion: '0.5.0',
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await service.hello();

        final JsonMap sentPayload =
            verify(
                  () => requestService.sendAndAwait(
                    messageType: any(named: 'messageType'),
                    payload: captureAny(named: 'payload'),
                    expectedType: any(named: 'expectedType'),
                    policy: any(named: 'policy'),
                  ),
                ).captured.single
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
        verify(
          () => sessionAdmissionService.admitSession(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.unpaired,
            currentHost: DovahLinkHost(
              hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
              hostName: 'Soneka-Desktop',
              endpoint: Uri.parse('ws://127.0.0.1:58231/'),
            ),
          ),
        ).called(1);
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
      },
    );

    test(
      'Method hello throws malformed_message and disconnects without admitting a session when '
      'hello_ack carries no sessionId',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.helloAck,
            sessionId: null,
            payload: <String, dynamic>{
              'hostVersion': '0.5.0',
              'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
              'hostName': 'Soneka-Desktop',
              'clientIdentityKind': 'unpaired',
            },
            clientId: 'client-1',
          ),
        );

        await expectLater(
          service.hello(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello throws malformed_message and disconnects when hello_ack fails to decode',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.helloAck,
            payload: <String, dynamic>{},
          ),
        );

        await expectLater(
          service.hello(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello throws malformed_message and disconnects when hostVersion is empty',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.helloAck,
            payload: <String, dynamic>{
              'hostVersion': '',
              'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
              'hostName': 'Soneka-Desktop',
              'clientIdentityKind': 'unpaired',
            },
          ),
        );

        await expectLater(
          service.hello(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello throws malformed_message and disconnects for an unknown clientIdentityKind',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.helloAck,
            payload: <String, dynamic>{
              'hostVersion': '0.5.0',
              'hostId': '81869993-955c-4ba3-a7d0-d35ca86078ea',
              'hostName': 'Soneka-Desktop',
              'clientIdentityKind': 'not-a-real-kind',
            },
          ),
        );

        await expectLater(
          service.hello(),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException error) => error.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method hello disconnects and rethrows a connection failure from sendAndAwait',
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
          service.hello(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(
          () => sessionAdmissionService.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
            currentHost: any(named: 'currentHost'),
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );
  });

  group('Method helloLastKnownHost behaves correctly', () {
    test(
      'Method helloLastKnownHost uses the relationship retained for recovery',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final DovahLinkHostId knownHostId = DovahLinkHostId(hostId);
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-a',
          ),
        );
        when(() => sessionService.currentKnownHostId).thenReturn(knownHostId);
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: hostId,
            kind: ClientIdentityKind.paired,
          ),
        );

        final HelloResult result = await service.helloLastKnownHost();

        expect(result.trustState, DovahLinkTrustState.trusted);
        final JsonMap sentHello =
            verify(
                  () => requestService.sendAndAwait(
                    messageType: ProtocolMessageType.hello,
                    payload: captureAny(named: 'payload'),
                    expectedType: ProtocolMessageType.helloAck,
                    policy: any(named: 'policy'),
                  ),
                ).captured.single
                as JsonMap;
        expect(sentHello['auth'], <String, dynamic>{
          'method': 'trusted_device_credential',
          'token': 'credential-a',
        });
        verify(() => sessionService.associateKnownHost(knownHostId)).called(1);
      },
    );

    test(
      'Method helloLastKnownHost does not infer a Known Host from a candidate claim',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        PersistedClientState persisted = Fixtures.buildPersistedClientState(
          clientId: 'client-1',
        );
        when(() => storage.load()).thenAnswer((_) async => persisted);
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: hostId,
            kind: ClientIdentityKind.unpaired,
          ),
        );
        await service.authenticateCandidate(Uri.parse('ws://127.0.0.1:58231/'));

        persisted = Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'existing-known-host-credential',
        );
        when(() => sessionService.currentKnownHostId).thenReturn(null);
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: hostId,
            kind: ClientIdentityKind.unpaired,
          ),
        );
        await service.helloLastKnownHost();

        final List<JsonMap> sentHellos = verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: captureAny(named: 'payload'),
            expectedType: ProtocolMessageType.helloAck,
            policy: any(named: 'policy'),
          ),
        ).captured.cast<JsonMap>();
        expect(sentHellos.last['auth'], <String, dynamic>{
          'method': 'unpaired',
        });
        verifyNever(() => sessionService.associateKnownHost(any()));
      },
    );
  });

  group('Method authenticate behaves correctly', () {
    test(
      'Method authenticateKnownHost cannot resume another Host recovery',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        when(() => storage.load()).thenAnswer(
          (_) async => PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostAId: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostAId),
                credential: 'credential-a',
              ),
              hostBId: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostBId),
                credential: 'credential-b',
              ),
            },
            pendingPairingRecovery: const PendingPairingRecovery(
              hostId: hostAId,
              state: PairingRecoveryState.confirming,
            ),
          ),
        );

        await expectLater(
          service.authenticateKnownHost(DovahLinkHostId(hostBId)),
          throwsA(
            isA<DovahLinkHostIdentityMismatchException>().having(
              (error) => error.knownHostId,
              'knownHostId',
              hostAId,
            ),
          ),
        );
        verifyNever(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        );
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
      'Method authenticateKnownHost keeps endpoint and credential selection Host-scoped',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        final Uri endpointA = Uri.parse('ws://127.0.0.1:58231/');
        final Uri endpointB = Uri.parse('ws://127.0.0.1:58232/');
        PersistedClientState current = PersistedClientState(
          clientId: 'client-1',
          knownHosts: <String, PersistedKnownHost>{
            hostAId: PersistedKnownHost(
              host: DovahLinkHost(
                hostId: hostAId,
                hostName: 'HOST-A',
                endpoint: endpointA,
              ),
              credential: 'credential-a',
            ),
            hostBId: PersistedKnownHost(
              host: DovahLinkHost(
                hostId: hostBId,
                hostName: 'HOST-B',
                endpoint: endpointB,
              ),
              credential: 'credential-b',
            ),
          },
        );
        final List<Uri> connectedEndpoints = [];
        final List<JsonMap> helloPayloads = [];
        int helloCount = 0;
        when(() => storage.load()).thenAnswer((_) async => current);
        when(() => storage.updateState(any())).thenAnswer((invocation) async {
          final PersistedClientState Function(PersistedClientState) update =
              invocation.positionalArguments.single
                  as PersistedClientState Function(PersistedClientState);
          current = update(current);
        });
        when(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).thenAnswer((invocation) async {
          connectedEndpoints.add(invocation.positionalArguments.single as Uri);
        });
        when(() => sessionService.currentEndpoint).thenAnswer(
          (_) => connectedEndpoints.isEmpty ? null : connectedEndpoints.last,
        );
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((invocation) async {
          helloPayloads.add(invocation.namedArguments[#payload] as JsonMap);
          helloCount++;
          return buildHelloAckEnvelope(
            hostId: helloCount == 1 ? hostAId : hostBId,
            hostName: helloCount == 1 ? 'RENAMED-HOST-A' : 'HOST-B',
            kind: ClientIdentityKind.paired,
          );
        });

        await service.authenticateKnownHost(DovahLinkHostId(hostAId));
        expect(current.knownHosts[hostAId]?.host.hostName, 'RENAMED-HOST-A');
        expect(current.knownHosts[hostBId]?.host.hostName, 'HOST-B');
        expect(current.knownHosts[hostBId]?.credential, 'credential-b');
        await service.authenticateKnownHost(
          DovahLinkHostId(hostBId.toUpperCase()),
        );

        expect(connectedEndpoints, <Uri>[endpointA, endpointB]);
        expect(current.knownHosts[hostAId]?.credential, 'credential-a');
        expect(
          helloPayloads.map((payload) => (payload['auth'] as JsonMap)['token']),
          <String>['credential-a', 'credential-b'],
        );
      },
    );

    test(
      'Method authenticateKnownHost rejects an unknown Host ID before connecting',
      () async {
        await expectLater(
          service.authenticateKnownHost(
            DovahLinkHostId('81f6cc90-3a88-40c7-8351-104d4a36c971'),
          ),
          throwsA(isA<DovahLinkKnownHostNotFoundException>()),
        );

        verifyNever(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        );
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
      'Method authenticate returns the cached result without re-sending hello when already connected and trusted',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-a',
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            hostVersion: '0.5.0',
            hostId: hostId,
            kind: ClientIdentityKind.paired,
          ),
        );
        await service.authenticateKnownHost(DovahLinkHostId(hostId));

        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);

        final HelloResult result = await service.authenticateKnownHost(
          DovahLinkHostId(hostId),
        );

        expect(result.hostVersion, '0.5.0');
        expect(result.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
        expect(result.hostName, 'Soneka-Desktop');
        expect(result.trustState, DovahLinkTrustState.trusted);
        verify(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).called(1);
        verify(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).called(1);
      },
    );

    test(
      'Method authenticate sends hello when connected but unpaired',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.unpaired);
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        final HelloResult result = await service.authenticateCandidate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verifyInOrder([
          () => sessionService.disconnect(orphanRetrySafeOperations: false),
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ]);
        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.helloAck,
            policy: any(named: 'policy'),
          ),
        ).called(1);
        expect(result.hostVersion, '0.5.0');
        expect(result.trustState, DovahLinkTrustState.unpaired);
      },
    );

    test(
      'Method authenticate sends hello when trusted but no host version is cached',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-a',
          ),
        );
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.paired,
          ),
        );

        final HelloResult result = await service.authenticateKnownHost(
          DovahLinkHostId(hostId),
        );

        verifyInOrder([
          () => sessionService.disconnect(orphanRetrySafeOperations: false),
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ]);
        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.helloAck,
            policy: any(named: 'policy'),
          ),
        ).called(1);
        expect(result.hostVersion, '0.5.0');
        expect(result.trustState, DovahLinkTrustState.trusted);
      },
    );

    test(
      'Method authenticate connects before sending hello when not already connected and trusted',
      () async {
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        final HelloResult result = await service.authenticateCandidate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verify(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).called(1);
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
        expect(result.hostVersion, '0.5.0');
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
        verifyNever(() => sessionService.associateKnownHost(any()));
      },
    );

    test(
      'Method authenticate propagates a connect() failure without ever sending hello',
      () async {
        when(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).thenThrow(const DovahLinkConnectionException('unreachable'));

        await expectLater(
          service.authenticateCandidate(Uri.parse('ws://127.0.0.1:1/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method authenticateKnownHost marks offline only when transport connection fails',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-a',
          ),
        );
        when(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).thenThrow(const DovahLinkConnectionException('unreachable'));

        await expectLater(
          service.authenticateKnownHost(DovahLinkHostId(hostId)),
          throwsA(isA<DovahLinkConnectionException>()),
        );

        verify(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(hostId),
            DovahLinkHostAvailability.offline,
          ),
        ).called(1);
        verifyNever(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId(hostId),
            DovahLinkHostAvailability.online,
          ),
        );
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
      'Method authenticateKnownHost does not mark offline when disconnect cancels a failed connect',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-a',
          ),
        );
        when(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).thenAnswer((_) async {
          service.cancelPendingAuthentication();
          throw const DovahLinkConnectionException('connect cancelled');
        });

        await expectLater(
          service.authenticateKnownHost(DovahLinkHostId(hostId)),
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException error) => error.message,
              'message',
              'Authentication was cancelled by disconnect.',
            ),
          ),
        );

        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method authenticateCandidate leaves Known Host availability unchanged for a matching Host ID claim',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'known-host-credential',
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: hostId,
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await service.authenticateCandidate(Uri.parse('ws://127.0.0.1:58232/'));

        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method authenticateKnownHost leaves availability unchanged for unsupported Host versions',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'credential-a',
          ),
        );
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            hostId: hostId,
            hostVersion: '0.6.0',
            kind: ClientIdentityKind.paired,
          ),
        );

        await expectLater(
          service.authenticateKnownHost(DovahLinkHostId(hostId)),
          throwsA(isA<DovahLinkCompatibilityException>()),
        );

        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
      },
    );

    test(
      'Method authenticate propagates a disconnect() failure from the reconnect guard without '
      'ever connecting or sending hello',
      () async {
        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.unpaired);
        when(
          () => sessionService.disconnect(orphanRetrySafeOperations: false),
        ).thenThrow(const DovahLinkConnectionException('close failed'));

        await expectLater(
          service.authenticateCandidate(Uri.parse('ws://127.0.0.1:1/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        );
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
      'Method authenticate propagates the retry attempt\'s own rejection after credential-rejection recovery',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'nope',
            retryable: false,
          ),
        );

        await expectLater(
          service.authenticateKnownHost(
            DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
          ),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.revoked,
            ),
          ),
        );
        verify(() => storage.updateState(any())).called(1);
        expect(updatedState?.knownHosts.values.single.credential, isNull);
        expect(updatedState?.pendingPairingRecovery, isNull);
        verify(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).called(2);
        verifyNever(
          () => hostAvailabilityService.setAvailability(any(), any()),
        );
        verify(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).called(2);
        // Both hello() attempts failed and each preserves any operation a prior ordinary
        // transport loss orphaned, rather than treating its own failure as final.
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(2);
      },
    );

    test(
      'Method authenticate propagates a retry connection failure after credential recovery',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        int connectCallCount = 0;
        when(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).thenAnswer((_) async {
          connectCallCount++;
          if (connectCallCount == 2) {
            throw const DovahLinkConnectionException('retry unreachable');
          }
        });
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.revoked,
            message: 'nope',
            retryable: false,
          ),
        );

        await expectLater(
          service.authenticateKnownHost(
            DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
          ),
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException e) => e.message,
              'message',
              'retry unreachable',
            ),
          ),
        );
        verify(() => storage.updateState(any())).called(1);
        expect(updatedState?.knownHosts.values.single.credential, isNull);
        expect(updatedState?.pendingPairingRecovery, isNull);
        verify(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).called(2);
        verify(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
            DovahLinkHostAvailability.offline,
          ),
        ).called(1);
        verify(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).called(1);
        // Only the first hello() attempt ran (and failed); the retry's own connect() threw before
        // a second hello() could run, so hello()'s disconnect()-on-failure only fires once.
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );

    test(
      'Method authenticate recovers from a revoked credential rejection by forgetting it and retrying as unpaired',
      () async {
        // The retry's hello() must read the state produced by the preceding update.
        PersistedClientState persisted = Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'stale-cred',
          pairingRequired: true,
        );
        when(() => storage.load()).thenAnswer((_) async => persisted);
        when(() => storage.updateState(any())).thenAnswer((invocation) async {
          final PersistedClientState Function(PersistedClientState) update =
              invocation.positionalArguments.single
                  as PersistedClientState Function(PersistedClientState);
          persisted = update(persisted);
          updatedState = persisted;
        });
        int callCount = 0;
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((_) async {
          callCount++;
          if (callCount == 1) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.revoked,
              message: 'nope',
              retryable: false,
            );
          }
          return buildHelloAckEnvelope(
            sessionId: 'session-2',
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.unpaired,
          );
        });

        final HelloResult result = await service.authenticateKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );

        expect(
          result.recoveredFromRejectedCredential,
          CredentialRejectionReason.revoked,
        );
        expect(result.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
        expect(result.hostName, 'Soneka-Desktop');
        expect(result.trustState, DovahLinkTrustState.unpaired);
        verify(
          () => sessionService.associateKnownHost(
            DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
          ),
        ).called(1);
        verify(() => storage.updateState(any())).called(1);
        expect(updatedState?.knownHosts.values.single.credential, isNull);
        expect(updatedState?.knownHosts.values.single.pairingRequired, isTrue);
        expect(updatedState?.pendingPairingRecovery, isNull);
        verify(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).called(2);
        final List<Object?> sentPayloads = verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: captureAny(named: 'payload'),
            expectedType: ProtocolMessageType.helloAck,
            policy: any(named: 'policy'),
          ),
        ).captured;
        expect(sentPayloads, hasLength(2));
        expect((sentPayloads.last as JsonMap)['auth'], <String, dynamic>{
          'method': 'unpaired',
        });
        verify(
          () => hostAvailabilityService.setAvailability(
            DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
            DovahLinkHostAvailability.online,
          ),
        ).called(1);
      },
    );

    test(
      'Method authenticate recovers from an unrecognized-credential rejection the same way',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
            pairingRequired: true,
          ),
        );
        int callCount = 0;
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((_) async {
          callCount++;
          if (callCount == 1) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.unauthenticated,
              message: 'nope',
              retryable: false,
            );
          }
          return buildHelloAckEnvelope(
            sessionId: 'session-2',
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.unpaired,
          );
        });

        final HelloResult result = await service.authenticateKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );

        expect(
          result.recoveredFromRejectedCredential,
          CredentialRejectionReason.unrecognized,
        );
        expect(result.trustState, DovahLinkTrustState.unpaired);
        verify(() => storage.updateState(any())).called(1);
        expect(updatedState?.knownHosts.values.single.credential, isNull);
        expect(updatedState?.knownHosts.values.single.pairingRequired, isTrue);
        expect(updatedState?.pendingPairingRecovery, isNull);
        verify(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).called(2);
      },
    );

    test(
      'Method authenticate recovers from a blocked credential rejection the same way',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
            pairingRequired: true,
          ),
        );
        int callCount = 0;
        when(
          () => requestService.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).thenAnswer((_) async {
          callCount++;
          if (callCount == 1) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.blocked,
              message: 'nope',
              retryable: false,
            );
          }
          return buildHelloAckEnvelope(
            sessionId: 'session-2',
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.unpaired,
          );
        });

        final HelloResult result = await service.authenticateKnownHost(
          DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
        );

        expect(
          result.recoveredFromRejectedCredential,
          CredentialRejectionReason.blocked,
        );
        expect(result.trustState, DovahLinkTrustState.unpaired);
        verify(() => storage.updateState(any())).called(1);
        expect(updatedState?.knownHosts.values.single.credential, isNull);
        expect(updatedState?.knownHosts.values.single.pairingRequired, isFalse);
        expect(updatedState?.pendingPairingRecovery, isNull);
        verify(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).called(2);
      },
    );

    test(
      'Method authenticate does not recover from a non-recoverable protocol rejection',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'good-cred',
          ),
        );
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
            message: 'no',
            retryable: true,
          ),
        );

        await expectLater(
          service.authenticateKnownHost(
            DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
          ),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.rateLimited,
            ),
          ),
        );
        verify(
          () => sessionService.connect(
            any(),
            knownHostId: any(named: 'knownHostId'),
          ),
        ).called(1);
        verifyNever(() => storage.updateState(any()));
      },
    );
  });

  group('Method forgetCredential behaves correctly', () {
    test(
      'Method forgetCredential removes only the named Host credential',
      () async {
        const String hostAId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        const String hostBId = '81f6cc90-3a88-40c7-8351-104d4a36c971';
        when(() => storage.load()).thenAnswer(
          (_) async => PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              hostAId: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostAId),
                credential: 'credential-a',
              ),
              hostBId: PersistedKnownHost(
                host: Fixtures.buildDovahLinkHost(hostId: hostBId),
                credential: 'credential-b',
              ),
            },
          ),
        );

        await service.forgetCredential(DovahLinkHostId(hostAId));

        expect(updatedState?.knownHosts[hostAId]?.credential, isNull);
        expect(updatedState?.knownHosts[hostBId]?.credential, 'credential-b');
        expect(updatedState?.knownHosts.length, 2);
      },
    );

    test(
      'Method forgetCredential clears credential and recovery state while preserving identity',
      () async {
        final DovahLinkHost knownHost = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'KNOWN-HOST',
          endpoint: Uri.parse('ws://127.0.0.1:58231/'),
        );
        when(() => storage.load()).thenAnswer(
          (_) async => PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              knownHost.hostId: PersistedKnownHost(
                host: knownHost,
                credential: 'cred',
              ),
            },
            pendingPairingRecovery: PendingPairingRecovery(
              hostId: knownHost.hostId,
              state: PairingRecoveryState.confirming,
            ),
          ),
        );

        await service.forgetCredential(DovahLinkHostId(knownHost.hostId));

        verify(() => storage.updateState(any())).called(1);
        expect(
          updatedState,
          PersistedClientState(
            clientId: 'client-1',
            knownHosts: <String, PersistedKnownHost>{
              knownHost.hostId: PersistedKnownHost(host: knownHost),
            },
          ),
        );
      },
    );
  });

  group('Property clientId behaves correctly', () {
    test('Property clientId reads through to the cache', () {
      when(() => clientIdCache.clientId).thenReturn('cached-client');

      expect(service.clientId, 'cached-client');
    });

    test('Property clientId is null before the cache holds a value', () {
      when(() => clientIdCache.clientId).thenReturn(null);

      expect(service.clientId, isNull);
    });
  });
}
