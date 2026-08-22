import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_client_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/message_receiver.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing_service.dart';
import 'package:dovahlink_client_sdk/src/internal/request_manager.dart';
import 'package:dovahlink_client_sdk/src/internal/session_trust_writer.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/persistence/in_memory_client_storage.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

class MockRequestManager extends Mock implements RequestManager {}

class MockSessionTrustWriter extends Mock implements SessionTrustWriter {}

class MockMessageReceiver extends Mock implements MessageReceiver {}

/// Builds a decoded `pairing_outcome` reply envelope carrying [outcome], with every other field
/// present-but-empty per that message's wire contract.
Envelope _outcomeEnvelope(
  PairingOutcome outcome, {
  String? credential,
  int? retryAfterSeconds,
}) => Envelope(
  messageType: ProtocolMessageType.pairingOutcome,
  messageId: 'reply-1',
  sessionId: 'session-1',
  correlationId: 'req-1',
  payload: <String, dynamic>{
    'outcome': _wireOutcome(outcome),
    'credential': credential,
    'shortId': null,
    'displayName': null,
    'retryAfterSeconds': retryAfterSeconds,
  },
  bridgeInstanceId: 'bridge-1',
  playContextId: null,
  clientId: null,
);

String _wireOutcome(PairingOutcome outcome) => switch (outcome) {
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
};

void main() {
  late MockRequestManager requestManager;
  late InMemoryClientStorage storage;
  late MockSessionTrustWriter sessionTrustWriter;
  late MockMessageReceiver messageReceiver;
  late PairingService service;

  setUpAll(() {
    registerFallbackValue(ProtocolMessageType.pairingRequest);
    registerFallbackValue(
      const RequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      ),
    );
  });

  setUp(() {
    requestManager = MockRequestManager();
    storage = InMemoryClientStorage();
    sessionTrustWriter = MockSessionTrustWriter();
    messageReceiver = MockMessageReceiver();
    when(() => messageReceiver.ensureReceiving()).thenReturn(null);
    when(() => sessionTrustWriter.markTrusted()).thenReturn(null);
    service = PairingService(
      requestManager: requestManager,
      storage: storage,
      sessionTrustWriter: sessionTrustWriter,
      messageReceiver: messageReceiver,
    );
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

  group('requestPairing', () {
    test('ensures receiving and decodes the pairing_status reply', () async {
      stubSendAndAwait(
        const Envelope(
          messageType: ProtocolMessageType.pairingStatus,
          messageId: 'reply-1',
          sessionId: 'session-1',
          correlationId: 'req-1',
          payload: <String, dynamic>{
            'state': 'available',
            'expiresInSeconds': 60,
          },
          bridgeInstanceId: 'bridge-1',
          playContextId: null,
          clientId: null,
        ),
      );

      final PairingChallengeStatus status = await service.requestPairing();

      verify(() => messageReceiver.ensureReceiving()).called(1);
      verify(
        () => requestManager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRequest,
          payload: any(named: 'payload'),
          expectedType: ProtocolMessageType.pairingStatus,
          policy: any(named: 'policy'),
        ),
      ).called(1);
      expect(status.availability, PairingAvailability.available);
      expect(status.expiresInSeconds, 60);
    });

    test('decodes otherDevicePairing with a null expiresInSeconds', () async {
      stubSendAndAwait(
        const Envelope(
          messageType: ProtocolMessageType.pairingStatus,
          messageId: 'reply-1',
          sessionId: 'session-1',
          correlationId: 'req-1',
          payload: <String, dynamic>{'state': 'other_device_pairing'},
          bridgeInstanceId: 'bridge-1',
          playContextId: null,
          clientId: null,
        ),
      );

      final PairingChallengeStatus status = await service.requestPairing();

      expect(status.availability, PairingAvailability.otherDevicePairing);
      expect(status.expiresInSeconds, isNull);
    });

    test(
      'throws malformed_message when pairing_status fails to decode',
      () async {
        stubSendAndAwait(
          const Envelope(
            messageType: ProtocolMessageType.pairingStatus,
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
  });

  group('requestPairingRenotify', () {
    test('decodes a renotified outcome', () async {
      stubSendAndAwait(_outcomeEnvelope(PairingOutcome.renotified));

      final PairingRenotifyResult result = await service
          .requestPairingRenotify();

      verify(() => messageReceiver.ensureReceiving()).called(1);
      verify(
        () => requestManager.sendAndAwait(
          messageType: ProtocolMessageType.pairingRenotify,
          payload: any(named: 'payload'),
          expectedType: ProtocolMessageType.pairingOutcome,
          policy: any(named: 'policy'),
        ),
      ).called(1);
      expect(result.status, PairingRenotifyStatus.renotified);
    });

    test(
      'decodes a renotify_cooldown outcome with retryAfterSeconds',
      () async {
        stubSendAndAwait(
          _outcomeEnvelope(
            PairingOutcome.renotifyCooldown,
            retryAfterSeconds: 5,
          ),
        );

        final PairingRenotifyResult result = await service
            .requestPairingRenotify();

        expect(result.status, PairingRenotifyStatus.cooldown);
        expect(result.retryAfterSeconds, 5);
      },
    );

    test('decodes an already_idle outcome', () async {
      stubSendAndAwait(_outcomeEnvelope(PairingOutcome.alreadyIdle));

      final PairingRenotifyResult result = await service
          .requestPairingRenotify();

      expect(result.status, PairingRenotifyStatus.alreadyIdle);
    });

    test(
      'throws malformed_message for an outcome not valid for this exchange',
      () async {
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.credentialIssued));

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
      'throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          const Envelope(
            messageType: ProtocolMessageType.pairingOutcome,
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
  });

  group('cancelPairing', () {
    test('decodes a cancelled outcome', () async {
      stubSendAndAwait(_outcomeEnvelope(PairingOutcome.cancelled));

      final PairingCancelOutcome result = await service.cancelPairing();

      verify(() => messageReceiver.ensureReceiving()).called(1);
      verify(
        () => requestManager.sendAndAwait(
          messageType: ProtocolMessageType.pairingCancel,
          payload: any(named: 'payload'),
          expectedType: ProtocolMessageType.pairingOutcome,
          policy: any(named: 'policy'),
        ),
      ).called(1);
      expect(result.status, PairingCancelStatus.cancelled);
    });

    test('decodes an already_idle outcome', () async {
      stubSendAndAwait(_outcomeEnvelope(PairingOutcome.alreadyIdle));

      final PairingCancelOutcome result = await service.cancelPairing();

      expect(result.status, PairingCancelStatus.alreadyIdle);
    });

    test(
      'throws malformed_message for an outcome not valid for this exchange',
      () async {
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.renotified));

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
      'throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          const Envelope(
            messageType: ProtocolMessageType.pairingOutcome,
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
  });

  group('confirmPairingCode', () {
    test(
      'persists the issued credential with a CONFIRMING recovery state',
      () async {
        stubSendAndAwait(
          _outcomeEnvelope(
            PairingOutcome.credentialIssued,
            credential: 'new-cred',
          ),
        );

        final String credential = await service.confirmPairingCode(
          code: '123456',
        );

        verify(() => messageReceiver.ensureReceiving()).called(1);
        expect(credential, 'new-cred');
        final PersistedClientState state = await storage.load();
        expect(state.credential, 'new-cred');
        expect(state.recoveryState, PairingRecoveryState.confirming);
      },
    );

    test(
      'throws malformed_message when credential_issued carries no credential',
      () async {
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.credentialIssued));

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
      },
    );

    test(
      'throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          const Envelope(
            messageType: ProtocolMessageType.pairingOutcome,
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
          service.confirmPairingCode(code: '123456'),
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

    test('sends the code and displayName in the outgoing payload', () async {
      stubSendAndAwait(
        _outcomeEnvelope(
          PairingOutcome.credentialIssued,
          credential: 'new-cred',
        ),
      );

      await service.confirmPairingCode(code: '123456', displayName: 'My PC');

      final JsonMap sentPayload =
          verify(
                () => requestManager.sendAndAwait(
                  messageType: ProtocolMessageType.pairingConfirm,
                  payload: captureAny(named: 'payload'),
                  expectedType: ProtocolMessageType.pairingOutcome,
                  policy: any(named: 'policy'),
                ),
              ).captured.single
              as JsonMap;
      expect(sentPayload['code'], '123456');
      expect(sentPayload['displayName'], 'My PC');
    });

    test(
      'throws DovahLinkPairingException for a rejected code, without persisting',
      () async {
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.expired));

        await expectLater(
          service.confirmPairingCode(code: '123456'),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException e) => e.outcome,
              'outcome',
              PairingOutcome.expired,
            ),
          ),
        );
        final PersistedClientState state = await storage.load();
        expect(state.credential, isNull);
      },
    );
  });

  group('acknowledgeTrustedCredential', () {
    test(
      'marks the session trusted and clears the recovery state on a trusted outcome',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.trusted));

        await service.acknowledgeTrustedCredential('cred');

        verify(() => messageReceiver.ensureReceiving()).called(1);
        verify(
          () => requestManager.sendAndAwait(
            messageType: ProtocolMessageType.pairingAck,
            payload: any(named: 'payload'),
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: any(named: 'policy'),
          ),
        ).called(1);
        verify(() => sessionTrustWriter.markTrusted()).called(1);
        final PersistedClientState state = await storage.load();
        expect(state.recoveryState, PairingRecoveryState.none);
        expect(state.credential, 'cred');
      },
    );

    test(
      'also marks the session trusted on an already_trusted outcome',
      () async {
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.alreadyTrusted));

        await service.acknowledgeTrustedCredential('cred');

        verify(() => sessionTrustWriter.markTrusted()).called(1);
      },
    );

    test(
      'never marks the session trusted, and throws DovahLinkPairingException, for a '
      'rejected acknowledgement',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.pendingNotFound));

        await expectLater(
          service.acknowledgeTrustedCredential('cred'),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException e) => e.outcome,
              'outcome',
              PairingOutcome.pendingNotFound,
            ),
          ),
        );
        verifyNever(() => sessionTrustWriter.markTrusted());
        final PersistedClientState state = await storage.load();
        expect(state.recoveryState, PairingRecoveryState.confirming);
      },
    );

    test(
      'never marks the session trusted for a plain outcome rejection (not pendingNotFound)',
      () async {
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.expired));

        await expectLater(
          service.acknowledgeTrustedCredential('cred'),
          throwsA(isA<DovahLinkPairingException>()),
        );
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );

    test(
      'throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          const Envelope(
            messageType: ProtocolMessageType.pairingOutcome,
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
          service.acknowledgeTrustedCredential('cred'),
          throwsA(
            isA<DovahLinkProtocolException>().having(
              (DovahLinkProtocolException e) => e.code,
              'code',
              ProtocolErrorCode.malformedMessage,
            ),
          ),
        );
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );
  });

  group('recoverPendingPairing', () {
    test(
      'is a no-op returning unpaired when no confirmation is outstanding',
      () async {
        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        verifyNever(
          () => requestManager.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(() => messageReceiver.ensureReceiving());
      },
    );

    test(
      'is a no-op returning unpaired when confirming but no credential is stored',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        verifyNever(
          () => requestManager.sendAndAwait(
            messageType: any(named: 'messageType'),
            payload: any(named: 'payload'),
            expectedType: any(named: 'expectedType'),
            policy: any(named: 'policy'),
          ),
        );
        verifyNever(() => messageReceiver.ensureReceiving());
      },
    );

    test(
      'retries the stored credential and returns trusted on success',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.trusted));

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.trusted);
        verify(() => sessionTrustWriter.markTrusted()).called(1);
      },
    );

    test(
      'discards the credential and resets to unpaired when the bridge reports '
      'pending_not_found',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.pendingNotFound));

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        final PersistedClientState state = await storage.load();
        expect(state.credential, isNull);
        expect(state.clientId, 'client-1');
      },
    );

    test(
      'leaves the CONFIRMING state untouched and rethrows for any other failure',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(_outcomeEnvelope(PairingOutcome.expired));

        await expectLater(
          service.recoverPendingPairing(),
          throwsA(isA<DovahLinkPairingException>()),
        );
        final PersistedClientState state = await storage.load();
        expect(state.credential, 'stored-cred');
        expect(state.recoveryState, PairingRecoveryState.confirming);
      },
    );
  });
}
