import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/message_receiver.dart';
import 'package:dovahlink_client_sdk/src/internal/request_sender.dart';
import 'package:dovahlink_client_sdk/src/internal/session_connector.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../fixtures/protocol/envelope.fixture.dart';

/// Mock request manager used to isolate authentication service tests.
class MockRequestSender extends Mock implements RequestSender {}

/// Mock session connector used to isolate authentication service tests.
class MockSessionConnector extends Mock implements SessionConnector {}

/// Mock message receiver used to isolate authentication service tests.
class MockMessageReceiver extends Mock implements MessageReceiver {}

/// Builds a decoded `hello_ack` reply envelope from the shared envelope fixture.
Envelope buildHelloAckEnvelope({
  String? sessionId = 'session-1',
  String bridgeVersion = '1.0',
  ClientIdentityKind kind = ClientIdentityKind.unpaired,
  String? clientId = 'client-1',
}) => buildEnvelope(
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
void stubSendAndAwait(RequestSender requestSender, Envelope envelope) {
  when(
    () => requestSender.sendAndAwait(
      messageType: any(named: 'messageType'),
      payload: any(named: 'payload'),
      expectedType: any(named: 'expectedType'),
      policy: any(named: 'policy'),
    ),
  ).thenAnswer((_) async => envelope);
}

/// Runs authentication service behavior tests.
void main() {
  late MockRequestSender requestSender;
  late InMemoryClientStorage storage;
  late MockSessionConnector sessionConnector;
  late MockMessageReceiver messageReceiver;
  late AuthenticationService service;

  setUpAll(() {
    registerFallbackValue(ProtocolMessageType.hello);
    registerFallbackValue(
      const RequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    registerFallbackValue(Uri.parse('ws://127.0.0.1:0/'));
    registerFallbackValue(DovahLinkTrustState.unpaired);
  });

  setUp(() {
    requestSender = MockRequestSender();
    storage = InMemoryClientStorage();
    sessionConnector = MockSessionConnector();
    messageReceiver = MockMessageReceiver();
    when(() => sessionConnector.connect(any())).thenAnswer((_) async {});
    when(() => sessionConnector.disconnect()).thenAnswer((_) async {});
    when(
      () => sessionConnector.connectionState,
    ).thenReturn(DovahLinkConnectionState.disconnected);
    when(() => sessionConnector.currentTrustState).thenReturn(null);
    when(() => messageReceiver.ensureReceiving()).thenReturn(null);
    service = AuthenticationService(
      requestSender: requestSender,
      storage: storage,
      sessionConnector: sessionConnector,
      messageReceiver: messageReceiver,
    );
  });

  group('Method hello behaves correctly', () {
    test(
      'Method hello ensures receiving before sending and admits the decoded session',
      () async {
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.2.3',
            kind: ClientIdentityKind.paired,
          ),
        );

        final HelloResult result = await service.hello();

        verifyInOrder([
          () => messageReceiver.ensureReceiving(),
          () => requestSender.sendAndAwait(
            messageType: ProtocolMessageType.hello,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.helloAck,
            policy: any(named: 'policy'),
          ),
          () => sessionConnector.admitSession(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.trusted,
          ),
        ]);
        verifyNever(() => sessionConnector.disconnect());
        expect(result.bridgeVersion, '1.2.3');
        expect(result.trustState, DovahLinkTrustState.trusted);
      },
    );

    test(
      'Method hello resolves and persists a fresh clientId on first use',
      () async {
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        final HelloResult result = await service.hello();

        verify(() => messageReceiver.ensureReceiving()).called(1);
        verify(
          () => sessionConnector.admitSession(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.unpaired,
          ),
        ).called(1);
        verifyNever(() => sessionConnector.disconnect());
        expect(result.trustState, DovahLinkTrustState.unpaired);
        expect(service.clientId, isNotNull);
        final PersistedClientState state = await storage.load();
        expect(state.clientId, service.clientId);
      },
    );

    test(
      'Method hello reuses an already-persisted clientId instead of generating a new one',
      () async {
        await storage.save(
          const PersistedClientState(clientId: 'existing-client'),
        );
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await service.hello();

        expect(service.clientId, 'existing-client');
      },
    );

    test(
      'Method hello presents the stored credential as trusted_device_credential for an ordinary reconnect',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'good-cred',
          ),
        );
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.paired,
          ),
        );

        await service.hello();

        final JsonMap sentPayload =
            verify(
                  () => requestSender.sendAndAwait(
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
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await service.hello();

        verify(() => messageReceiver.ensureReceiving()).called(1);
        final JsonMap sentPayload =
            verify(
                  () => requestSender.sendAndAwait(
                    messageType: any(named: 'messageType'),
                    payload: captureAny(named: 'payload'),
                    expectedType: any(named: 'expectedType'),
                    policy: any(named: 'policy'),
                  ),
                ).captured.single
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
        verify(
          () => sessionConnector.admitSession(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.unpaired,
          ),
        ).called(1);
        verifyNever(() => sessionConnector.disconnect());
      },
    );

    test(
      'Method hello throws malformed_message and disconnects without admitting a session when '
      'hello_ack carries no sessionId',
      () async {
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            sessionId: null,
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.unpaired,
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
          () => sessionConnector.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
          ),
        );
        verify(() => sessionConnector.disconnect()).called(1);
      },
    );

    test(
      'Method hello throws malformed_message and disconnects when hello_ack fails to decode',
      () async {
        stubSendAndAwait(
          requestSender,
          buildEnvelope(
            messageType: ProtocolMessageType.helloAck,
            messageId: 'reply-1',
            sessionId: 'session-1',
            correlationId: 'req-1',
            payload: <String, dynamic>{},
            bridgeInstanceId: 'bridge-1',
            playContextId: null,
            clientId: null,
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
          () => sessionConnector.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
          ),
        );
        verify(() => sessionConnector.disconnect()).called(1);
      },
    );

    test(
      'Method hello throws malformed_message and disconnects when bridgeVersion is empty',
      () async {
        stubSendAndAwait(
          requestSender,
          buildEnvelope(
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
          () => sessionConnector.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
          ),
        );
        verify(() => sessionConnector.disconnect()).called(1);
      },
    );

    test(
      'Method hello throws malformed_message and disconnects for an unknown clientIdentityKind',
      () async {
        stubSendAndAwait(
          requestSender,
          buildEnvelope(
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
          () => sessionConnector.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
          ),
        );
        verify(() => sessionConnector.disconnect()).called(1);
      },
    );

    test(
      'Method hello disconnects and rethrows a connection failure from sendAndAwait',
      () async {
        when(
          () => requestSender.sendAndAwait(
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
          () => sessionConnector.admitSession(
            sessionId: any(named: 'sessionId'),
            trustState: any(named: 'trustState'),
          ),
        );
        verify(() => sessionConnector.disconnect()).called(1);
      },
    );
  });

  group('Method authenticate behaves correctly', () {
    test(
      'Method authenticate returns the cached result without re-sending hello when already connected and trusted',
      () async {
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.paired,
          ),
        );
        await service.hello();

        when(
          () => sessionConnector.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionConnector.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        expect(result.bridgeVersion, '1.0');
        expect(result.trustState, DovahLinkTrustState.trusted);
        verifyNever(() => sessionConnector.connect(any()));
        verify(
          () => requestSender.sendAndAwait(
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
          () => sessionConnector.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionConnector.currentTrustState,
        ).thenReturn(DovahLinkTrustState.unpaired);
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            bridgeVersion: '2.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verify(() => sessionConnector.connect(any())).called(1);
        verify(
          () => requestSender.sendAndAwait(
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
          () => sessionConnector.connectionState,
        ).thenReturn(DovahLinkConnectionState.connected);
        when(
          () => sessionConnector.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        stubSendAndAwait(
          requestSender,
          buildHelloAckEnvelope(
            bridgeVersion: '2.1',
            kind: ClientIdentityKind.paired,
          ),
        );

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verify(() => sessionConnector.connect(any())).called(1);
        verify(
          () => requestSender.sendAndAwait(
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
          requestSender,
          buildHelloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '2.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        verify(() => sessionConnector.connect(any())).called(1);
        expect(result.bridgeVersion, '2.0');
      },
    );

    test(
      'Method authenticate propagates a connect() failure without ever sending hello',
      () async {
        when(
          () => sessionConnector.connect(any()),
        ).thenThrow(const DovahLinkConnectionException('unreachable'));

        await expectLater(
          service.authenticate(Uri.parse('ws://127.0.0.1:1/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(
          () => requestSender.sendAndAwait(
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
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        when(
          () => requestSender.sendAndAwait(
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
        final PersistedClientState state = await storage.load();
        expect(state.credential, isNull);
        verify(() => sessionConnector.connect(any())).called(2);
        verify(
          () => requestSender.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).called(2);
      },
    );

    test(
      'Method authenticate propagates a retry connection failure after credential recovery',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        int connectCallCount = 0;
        when(() => sessionConnector.connect(any())).thenAnswer((_) async {
          connectCallCount++;
          if (connectCallCount == 2) {
            throw const DovahLinkConnectionException('retry unreachable');
          }
        });
        when(
          () => requestSender.sendAndAwait(
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
        final PersistedClientState state = await storage.load();
        expect(state.credential, isNull);
        verify(() => sessionConnector.connect(any())).called(2);
        verify(
          () => requestSender.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).called(1);
      },
    );

    test(
      'Method authenticate recovers from a revoked credential rejection by forgetting it and retrying as unpaired',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        int callCount = 0;
        when(
          () => requestSender.sendAndAwait(
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
        final PersistedClientState state = await storage.load();
        expect(state.credential, isNull);
        expect(state.clientId, 'client-1');
        verify(() => sessionConnector.connect(any())).called(2);
        final List<Object?> sentPayloads = verify(
          () => requestSender.sendAndAwait(
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
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        int callCount = 0;
        when(
          () => requestSender.sendAndAwait(
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
        final PersistedClientState state = await storage.load();
        expect(state.credential, isNull);
        expect(state.clientId, 'client-1');
        verify(() => sessionConnector.connect(any())).called(2);
        final List<Object?> sentPayloads = verify(
          () => requestSender.sendAndAwait(
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
      'Method authenticate does not recover from a non-recoverable protocol rejection',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'good-cred',
          ),
        );
        when(
          () => requestSender.sendAndAwait(
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
        verify(() => sessionConnector.connect(any())).called(1);
        final PersistedClientState state = await storage.load();
        expect(state.credential, 'good-cred');
      },
    );
  });

  group('Method forgetCredential behaves correctly', () {
    test(
      'Method forgetCredential clears credential and recovery state while preserving clientId',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );

        await service.forgetCredential();

        final PersistedClientState state = await storage.load();
        expect(state.clientId, 'client-1');
        expect(state.credential, isNull);
        expect(state.recoveryState, PairingRecoveryState.none);
      },
    );
  });
}
