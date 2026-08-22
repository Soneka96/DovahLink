import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_client_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/message_receiver.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/internal/session_connector.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

class MockRequestManager extends Mock implements RequestManager {}

class MockSessionConnector extends Mock implements SessionConnector {}

class MockSessionContext extends Mock implements SessionContext {}

class MockMessageReceiver extends Mock implements MessageReceiver {}

/// Builds a decoded `hello_ack` reply envelope.
Envelope _helloAckEnvelope({
  required String? sessionId,
  required String bridgeVersion,
  required ClientIdentityKind kind,
}) => Envelope(
  messageType: ProtocolMessageType.helloAck,
  messageId: 'reply-1',
  sessionId: sessionId,
  correlationId: 'req-1',
  payload: <String, dynamic>{
    'bridgeVersion': bridgeVersion,
    'clientIdentityKind': kind == ClientIdentityKind.paired
        ? 'paired'
        : 'unpaired',
  },
  bridgeInstanceId: 'bridge-1',
  playContextId: null,
  clientId: null,
);

void main() {
  late MockRequestManager requestManager;
  late InMemoryClientStorage storage;
  late MockSessionConnector sessionConnector;
  late MockSessionContext sessionContext;
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
    requestManager = MockRequestManager();
    storage = InMemoryClientStorage();
    sessionConnector = MockSessionConnector();
    sessionContext = MockSessionContext();
    messageReceiver = MockMessageReceiver();
    when(() => sessionConnector.connect(any())).thenAnswer((_) async {});
    when(() => sessionConnector.disconnect()).thenAnswer((_) async {});
    when(
      () => sessionConnector.connectionState,
    ).thenReturn(DovahLinkConnectionState.disconnected);
    when(() => sessionContext.currentTrustState).thenReturn(null);
    when(() => messageReceiver.ensureReceiving()).thenReturn(null);
    service = AuthenticationService(
      requestManager: requestManager,
      storage: storage,
      sessionConnector: sessionConnector,
      sessionContext: sessionContext,
      messageReceiver: messageReceiver,
    );
  });

  group('CredentialRejectionReason', () {
    test('maps the recoverable typed protocol errors', () {
      expect(
        CredentialRejectionReason.fromProtocolErrorCode(
          ProtocolErrorCode.revoked,
        ),
        CredentialRejectionReason.revoked,
      );
      expect(
        CredentialRejectionReason.fromProtocolErrorCode(
          ProtocolErrorCode.unauthenticated,
        ),
        CredentialRejectionReason.unrecognized,
      );
    });

    test('does not map non-recoverable typed protocol errors', () {
      expect(
        CredentialRejectionReason.fromProtocolErrorCode(
          ProtocolErrorCode.rateLimited,
        ),
        isNull,
      );
    });
  });

  /// Stubs `sendAndAwait` to answer with [envelope], matching any call.
  void stubSendAndAwait(Envelope envelope) {
    when(
      () => requestManager.sendAndAwait(
        messageType: any(named: 'messageType'),
        payload: any(named: 'payload'),
        expectedType: any(named: 'expectedType'),
        policy: any(named: 'policy'),
      ),
    ).thenAnswer((_) async => envelope);
  }

  group('hello', () {
    test(
      'ensures receiving before sending, and admits the decoded session',
      () async {
        stubSendAndAwait(
          _helloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.2.3',
            kind: ClientIdentityKind.paired,
          ),
        );

        final HelloResult result = await service.hello();

        verify(() => messageReceiver.ensureReceiving()).called(1);
        verify(
          () => sessionConnector.admitSession(
            sessionId: 'session-1',
            trustState: DovahLinkTrustState.trusted,
          ),
        ).called(1);
        expect(result.bridgeVersion, '1.2.3');
        expect(result.trustState, DovahLinkTrustState.trusted);
      },
    );

    test('resolves and persists a fresh clientId on first use', () async {
      stubSendAndAwait(
        _helloAckEnvelope(
          sessionId: 'session-1',
          bridgeVersion: '1.0',
          kind: ClientIdentityKind.unpaired,
        ),
      );

      await service.hello();

      verify(() => messageReceiver.ensureReceiving()).called(1);
      expect(service.clientId, isNotNull);
      final PersistedClientState state = await storage.load();
      expect(state.clientId, service.clientId);
    });

    test(
      'reuses an already-persisted clientId instead of generating a new one',
      () async {
        await storage.save(
          const PersistedClientState(clientId: 'existing-client'),
        );
        stubSendAndAwait(
          _helloAckEnvelope(
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
      'presents the stored credential as trusted_device_credential for an ordinary reconnect',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'good-cred',
          ),
        );
        stubSendAndAwait(
          _helloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.paired,
          ),
        );

        await service.hello();

        final JsonMap sentPayload =
            verify(
                  () => requestManager.sendAndAwait(
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
      'presents unpaired, not the stored credential, while a pairing confirmation is '
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
          _helloAckEnvelope(
            sessionId: 'session-1',
            bridgeVersion: '1.0',
            kind: ClientIdentityKind.unpaired,
          ),
        );

        await service.hello();

        verify(() => messageReceiver.ensureReceiving()).called(1);
        final JsonMap sentPayload =
            verify(
                  () => requestManager.sendAndAwait(
                    messageType: any(named: 'messageType'),
                    payload: captureAny(named: 'payload'),
                    expectedType: any(named: 'expectedType'),
                    policy: any(named: 'policy'),
                  ),
                ).captured.single
                as JsonMap;
        expect(sentPayload['auth'], <String, dynamic>{'method': 'unpaired'});
      },
    );

    test(
      'throws malformed_message and disconnects, without admitting a session, when '
      'hello_ack carries no sessionId',
      () async {
        stubSendAndAwait(
          _helloAckEnvelope(
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
      'throws malformed_message and disconnects when hello_ack fails to decode',
      () async {
        stubSendAndAwait(
          const Envelope(
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
      'disconnects and rethrows a connection failure from sendAndAwait',
      () async {
        when(
          () => requestManager.sendAndAwait(
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

  group('authenticate', () {
    test(
      'returns the cached result without re-sending hello when already connected and trusted',
      () async {
        stubSendAndAwait(
          _helloAckEnvelope(
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
          () => sessionContext.currentTrustState,
        ).thenReturn(DovahLinkTrustState.trusted);

        final HelloResult result = await service.authenticate(
          Uri.parse('ws://127.0.0.1:1/'),
        );

        expect(result.bridgeVersion, '1.0');
        expect(result.trustState, DovahLinkTrustState.trusted);
        verifyNever(() => sessionConnector.connect(any()));
        verify(
          () => requestManager.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        ).called(1);
      },
    );

    test(
      'connects before sending hello when not already connected and trusted',
      () async {
        stubSendAndAwait(
          _helloAckEnvelope(
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

    test('propagates a connect() failure without ever sending hello', () async {
      when(
        () => sessionConnector.connect(any()),
      ).thenThrow(const DovahLinkConnectionException('unreachable'));

      await expectLater(
        service.authenticate(Uri.parse('ws://127.0.0.1:1/')),
        throwsA(isA<DovahLinkConnectionException>()),
      );
      verifyNever(
        () => requestManager.sendAndAwait(
          messageType: any(named: 'messageType'),
          payload: any(named: 'payload'),
          expectedType: any(named: 'expectedType'),
          policy: any(named: 'policy'),
        ),
      );
    });

    test(
      'propagates the retry attempt\'s own rejection after credential-rejection recovery',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        when(
          () => requestManager.sendAndAwait(
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
      },
    );

    test(
      'recovers from a revoked credential rejection by forgetting it and retrying as unpaired',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        int callCount = 0;
        when(
          () => requestManager.sendAndAwait(
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
          return _helloAckEnvelope(
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
        final PersistedClientState state = await storage.load();
        expect(state.credential, isNull);
        expect(state.clientId, 'client-1');
        verify(() => sessionConnector.connect(any())).called(2);
      },
    );

    test(
      'recovers from an unrecognized-credential rejection the same way',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stale-cred',
          ),
        );
        int callCount = 0;
        when(
          () => requestManager.sendAndAwait(
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
          return _helloAckEnvelope(
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
      },
    );

    test(
      'does not recover from a non-recoverable protocol rejection',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'good-cred',
          ),
        );
        when(
          () => requestManager.sendAndAwait(
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

  group('forgetCredential', () {
    test(
      'clears credential and recovery state while preserving clientId',
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
