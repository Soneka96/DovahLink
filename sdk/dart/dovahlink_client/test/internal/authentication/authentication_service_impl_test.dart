import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service_impl.dart';
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

/// Mock session service used to isolate authentication service tests, per
/// `ai/context/sdk/testing.md`'s "Service test boundaries".
class MockSessionService extends Mock implements ISessionService {}

/// Mock session admission service -- its own admit/retry logic is
/// `session_admission_service_test.dart`'s responsibility; this file only proves
/// [AuthenticationServiceImpl] calls it with the right arguments.
class MockSessionAdmissionService extends Mock
    implements ISessionAdmissionService {}

/// Mock request service used to isolate authentication service tests.
class MockRequestService extends Mock implements IRequestService {}

/// Mock client storage -- its own persistence mechanics are covered by its own implementation's
/// test file; this file only proves [AuthenticationServiceImpl] reads and writes the right state.
class MockClientStorage extends Mock implements IClientStorage {}

/// Mock client ID resolver -- its own generate/persist logic is
/// `client_id_resolver_test.dart`'s responsibility; this file only proves
/// [AuthenticationServiceImpl] uses its resolved value.
class MockClientIdResolver extends Mock implements ClientIdResolver {}

/// Builds a decoded `hello_ack` reply envelope from the shared envelope fixture.
Envelope buildHelloAckEnvelope({
  String? sessionId = 'session-1',
  String bridgeVersion = '1.0',
  ClientIdentityKind kind = ClientIdentityKind.unpaired,
  String? clientId = 'client-1',
}) => Fixtures.buildEnvelope(
  messageType: ProtocolMessageType.helloAck,
  sessionId: sessionId,
  clientId: clientId,
  payload: <String, dynamic>{
    'bridgeVersion': bridgeVersion,
    'clientIdentityKind': kind == ClientIdentityKind.paired
        ? 'paired'
        : 'unpaired',
  },
);

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

/// Runs authentication service behavior tests.
void main() {
  late MockSessionService sessionService;
  late MockSessionAdmissionService sessionAdmissionService;
  late MockRequestService requestService;
  late MockClientStorage storage;
  late MockClientIdResolver clientIdResolver;
  late AuthenticationServiceImpl service;

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
    service = AuthenticationServiceImpl(
      sessionService: sessionService,
      sessionAdmissionService: sessionAdmissionService,
      requestService: requestService,
      storage: storage,
      clientIdResolver: clientIdResolver,
    );
  });

  group('Method hello behaves correctly', () {
    test('Method hello sends hello and admits the decoded session', () async {
      stubSendAndAwait(
        requestService,
        buildHelloAckEnvelope(
          sessionId: 'session-1',
          bridgeVersion: '1.2.3',
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
      expect(result.bridgeVersion, '1.2.3');
      expect(result.trustState, DovahLinkTrustState.trusted);
    });

    test(
      'Method hello presents unpaired when no credential is stored',
      () async {
        stubSendAndAwait(
          requestService,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.0',
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
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await service.hello();

        verify(() => clientIdResolver.resolve(loaded)).called(1);
        expect(service.clientId, 'existing-client');
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
            bridgeVersion: '1.0',
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
            bridgeVersion: '1.0',
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
              'bridgeVersion': '1.0',
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
      'Method hello throws malformed_message and disconnects when bridgeVersion is empty',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.helloAck,
            payload: <String, dynamic>{
              'bridgeVersion': '',
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
              'bridgeVersion': '1.0',
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
            bridgeVersion: '1.0',
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

        expect(result.bridgeVersion, '1.0');
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
            bridgeVersion: '2.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verify(() => sessionService.connect(any())).called(1);
        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.helloAck,
            policy: any(named: 'policy'),
          ),
        ).called(1);
        expect(result.bridgeVersion, '2.0');
        expect(result.trustState, DovahLinkTrustState.unpaired);
      },
    );

    test(
      'Method authenticate sends hello when trusted but no bridge version is cached',
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
            bridgeVersion: '2.1',
            kind: ClientIdentityKind.paired,
          ),
        );

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verify(() => sessionService.connect(any())).called(1);
        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.helloAck,
            policy: any(named: 'policy'),
          ),
        ).called(1);
        expect(result.bridgeVersion, '2.1');
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
            bridgeVersion: '2.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verify(() => sessionService.connect(any())).called(1);
        expect(result.bridgeVersion, '2.0');
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
            bridgeVersion: '3.0',
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
            bridgeVersion: '3.0',
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
            bridgeVersion: '3.0',
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
}
