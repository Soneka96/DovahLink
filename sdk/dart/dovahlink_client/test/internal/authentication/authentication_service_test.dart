import 'dart:async';

import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_cache.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
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

/// Mocks persistence so these tests can verify which state [AuthenticationService] reads and
/// writes.
class MockClientStorage extends Mock implements IClientStorage {}

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
  late MockClientStorage storage;
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
    registerFallbackValue(Fixtures.buildPersistedClientState());
  });

  setUp(() {
    sessionService = MockSessionService();
    sessionAdmissionService = MockSessionAdmissionService();
    requestService = MockRequestService();
    storage = MockClientStorage();
    clientIdResolver = MockClientIdResolver();
    clientIdCache = MockClientIdCache();
    when(() => sessionService.connect(any())).thenAnswer((_) async {});
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
      () => sessionAdmissionService.admitSession(
        sessionId: any(named: 'sessionId'),
        trustState: any(named: 'trustState'),
      ),
    ).thenAnswer((_) {});
    when(() => storage.load()).thenAnswer(
      (_) async => Fixtures.buildPersistedClientState(clientId: 'client-1'),
    );
    when(() => storage.save(any())).thenAnswer((_) async {});
    when(
      () => clientIdResolver.resolve(any()),
    ).thenAnswer((_) async => 'client-1');
    service = AuthenticationService(
      sessionService: sessionService,
      sessionAdmissionService: sessionAdmissionService,
      requestService: requestService,
      storage: storage,
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
          kind: ClientIdentityKind.paired,
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
          trustState: DovahLinkTrustState.trusted,
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
      expect(result.trustState, DovahLinkTrustState.trusted);
    });

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
      'Method hello presents the stored credential as trusted_device_credential for an ordinary reconnect',
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
        expect(sentPayload['auth'], <String, dynamic>{
          'method': 'trusted_device_credential',
          'token': 'good-cred',
        });
      },
    );

    test(
      'Method hello presents unpaired, not the stored credential, while a pairing confirmation is '
      'outstanding',
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
          ),
        );
        verify(
          () => sessionService.disconnect(orphanRetrySafeOperations: true),
        ).called(1);
      },
    );
  });

  group('Method authenticate behaves correctly', () {
    test(
      'Method authenticate returns the cached result without re-sending hello when already connected and trusted',
      () async {
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            hostVersion: '0.5.0',
            kind: ClientIdentityKind.paired,
          ),
        );
        await service.hello();

        when(
          () => sessionService.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionService.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        expect(result.hostVersion, '0.5.0');
        expect(result.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
        expect(result.hostName, 'Soneka-Desktop');
        expect(result.trustState, DovahLinkTrustState.trusted);
        verifyNever(() => sessionService.connect(any()));
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

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verifyInOrder([
          () => sessionService.disconnect(orphanRetrySafeOperations: false),
          () => sessionService.connect(any()),
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

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verifyInOrder([
          () => sessionService.disconnect(orphanRetrySafeOperations: false),
          () => sessionService.connect(any()),
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

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verify(() => sessionService.connect(any())).called(1);
        verifyNever(
          () => sessionService.disconnect(
            orphanRetrySafeOperations: any(named: 'orphanRetrySafeOperations'),
          ),
        );
        expect(result.hostVersion, '0.5.0');
      },
    );

    test(
      'Method authenticate propagates a connect() failure without ever sending hello',
      () async {
        when(
          () => sessionService.connect(any()),
        ).thenThrow(const DovahLinkConnectionException('unreachable'));

        await expectLater(
          service.authenticate(Uri.parse('ws://127.0.0.1:1/')),
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
          service.authenticate(Uri.parse('ws://127.0.0.1:1/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => sessionService.connect(any()));
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
          service.authenticate(Uri.parse('ws://127.0.0.1:1/')),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.revoked,
            ),
          ),
        );
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(clientId: 'client-1'),
          ),
        ).called(1);
        verify(() => sessionService.connect(any())).called(2);
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
        when(() => sessionService.connect(any())).thenAnswer((_) async {
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
          service.authenticate(Uri.parse('ws://127.0.0.1:1/')),
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException e) => e.message,
              'message',
              'retry unreachable',
            ),
          ),
        );
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(clientId: 'client-1'),
          ),
        ).called(1);
        verify(() => sessionService.connect(any())).called(2);
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
        // storage.load() must reflect forgetCredential()'s own storage.save() before the retry
        // attempt's hello() re-reads it, so the retry actually presents unpaired -- a static stub
        // would keep returning the stale credential regardless of the intervening save().
        PersistedClientState persisted = Fixtures.buildPersistedClientState(
          clientId: 'client-1',
          credential: 'stale-cred',
        );
        when(() => storage.load()).thenAnswer((_) async => persisted);
        when(() => storage.save(any())).thenAnswer((
          Invocation invocation,
        ) async {
          persisted =
              invocation.positionalArguments.single as PersistedClientState;
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

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        expect(
          result.recoveredFromRejectedCredential,
          CredentialRejectionReason.revoked,
        );
        expect(result.hostId, '81869993-955c-4ba3-a7d0-d35ca86078ea');
        expect(result.hostName, 'Soneka-Desktop');
        expect(result.trustState, DovahLinkTrustState.unpaired);
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(clientId: 'client-1'),
          ),
        ).called(1);
        verify(() => sessionService.connect(any())).called(2);
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
      },
    );

    test(
      'Method authenticate recovers from an unrecognized-credential rejection the same way',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
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

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        expect(
          result.recoveredFromRejectedCredential,
          CredentialRejectionReason.unrecognized,
        );
        expect(result.trustState, DovahLinkTrustState.unpaired);
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(clientId: 'client-1'),
          ),
        ).called(1);
        verify(() => sessionService.connect(any())).called(2);
      },
    );

    test(
      'Method authenticate recovers from a blocked credential rejection the same way',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
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

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        expect(
          result.recoveredFromRejectedCredential,
          CredentialRejectionReason.blocked,
        );
        expect(result.trustState, DovahLinkTrustState.unpaired);
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(clientId: 'client-1'),
          ),
        ).called(1);
        verify(() => sessionService.connect(any())).called(2);
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
          service.authenticate(Uri.parse('ws://127.0.0.1:1/')),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.rateLimited,
            ),
          ),
        );
        verify(() => sessionService.connect(any())).called(1);
        verifyNever(() => storage.save(any()));
      },
    );
  });

  group('Method forgetCredential behaves correctly', () {
    test(
      'Method forgetCredential clears credential and recovery state while preserving clientId',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );

        await service.forgetCredential();

        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(clientId: 'client-1'),
          ),
        ).called(1);
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
