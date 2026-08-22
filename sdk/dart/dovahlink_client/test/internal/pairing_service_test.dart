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
import '../fixtures/protocol/envelope.fixture.dart';

/// Mock request manager used to isolate pairing service tests.
class MockRequestManager extends Mock implements RequestManager {}

/// Mock session trust writer used to isolate pairing service tests.
class MockSessionTrustWriter extends Mock implements SessionTrustWriter {}

/// Mock message receiver used to isolate pairing service tests.
class MockMessageReceiver extends Mock implements MessageReceiver {}

/// Builds a decoded `pairing_outcome` reply envelope carrying [outcome], with every other field
/// present-but-empty per that message's wire contract.
Envelope buildPairingOutcomeEnvelope({
  PairingOutcome outcome = PairingOutcome.credentialIssued,
  String? credential,
  int? retryAfterSeconds,
}) => buildEnvelope(
  messageType: ProtocolMessageType.pairingOutcome,
  payload: <String, dynamic>{
    'outcome': _wirePairingOutcome(outcome),
    'credential': credential,
    'shortId': null,
    'displayName': null,
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
};

/// Stubs `sendAndAwait` to answer with [envelope], matching any call.
void stubSendAndAwait(RequestManager requestManager, Envelope envelope) {
  when(
    () => requestManager.sendAndAwait(
      messageType: any(named: 'messageType'),
      payload: any(named: 'payload'),
      expectedType: any(named: 'expectedType'),
      policy: any(named: 'policy'),
    ),
  ).thenAnswer((_) async => envelope);
}

/// Runs pairing service behavior tests.
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

  group('Method requestPairing behaves correctly', () {
    test(
      'Method requestPairing ensures receiving and decodes the pairing_status reply',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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

        verifyInOrder([
          () => messageReceiver.ensureReceiving(),
          () => requestManager.sendAndAwait(
            messageType: ProtocolMessageType.pairingRequest,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.pairingStatus,
            policy: const RequestPolicy(
              retrySafe: true,
              requiredTrustState: DovahLinkTrustState.unpaired,
              timeoutClass: TimeoutClass.short,
            ),
          ),
        ]);
        expect(status.availability, PairingAvailability.available);
        expect(status.expiresInSeconds, 60);
      },
    );

    test(
      'Method requestPairing decodes otherDevicePairing with a null expiresInSeconds',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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
      },
    );

    test(
      'Method requestPairing decodes unavailable with a null expiresInSeconds',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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
          requestManager,
          buildEnvelope(
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
          requestManager,
          buildEnvelope(
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
          requestManager,
          buildEnvelope(
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

    test(
      'Method requestPairing translates an impossible pairing_status expiry into malformed_message',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );

    test(
      'Method requestPairing translates an omitted required expiry into malformed_message',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );

    test(
      'Method requestPairing propagates a connection failure without changing trust state',
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
          service.requestPairing(),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );
  });

  group('Method requestPairingRenotify behaves correctly', () {
    test(
      'Method requestPairingRenotify decodes a renotified outcome',
      () async {
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.renotified),
        );

        final PairingRenotifyResult result = await service
            .requestPairingRenotify();

        verify(() => messageReceiver.ensureReceiving()).called(1);
        verify(
          () => requestManager.sendAndAwait(
            messageType: ProtocolMessageType.pairingRenotify,
            payload: const <String, dynamic>{},
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: const RequestPolicy(
              retrySafe: true,
              requiredTrustState: DovahLinkTrustState.unpaired,
              timeoutClass: TimeoutClass.short,
            ),
          ),
        ).called(1);
        expect(result.status, PairingRenotifyStatus.renotified);
        expect(result.retryAfterSeconds, isNull);
      },
    );

    test(
      'Method requestPairingRenotify decodes a renotify_cooldown outcome with retryAfterSeconds',
      () async {
        stubSendAndAwait(
          requestManager,
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
          requestManager,
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
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.credentialIssued),
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
                  contains('pairing_renotify'),
                ),
          ),
        );
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );

    test(
      'Method requestPairingRenotify throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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

    test(
      'Method requestPairingRenotify propagates a protocol failure without marking trust',
      () async {
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
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );
  });

  group('Method cancelPairing behaves correctly', () {
    test('Method cancelPairing decodes a cancelled outcome', () async {
      stubSendAndAwait(
        requestManager,
        buildPairingOutcomeEnvelope(outcome: PairingOutcome.cancelled),
      );

      final PairingCancelOutcome result = await service.cancelPairing();

      verify(() => messageReceiver.ensureReceiving()).called(1);
      verify(
        () => requestManager.sendAndAwait(
          messageType: ProtocolMessageType.pairingCancel,
          payload: const <String, dynamic>{},
          expectedType: ProtocolMessageType.pairingOutcome,
          policy: const RequestPolicy(
            retrySafe: true,
            requiredTrustState: DovahLinkTrustState.unpaired,
            timeoutClass: TimeoutClass.short,
          ),
        ),
      ).called(1);
      expect(result.status, PairingCancelStatus.cancelled);
    });

    test('Method cancelPairing decodes an already_idle outcome', () async {
      stubSendAndAwait(
        requestManager,
        buildPairingOutcomeEnvelope(outcome: PairingOutcome.alreadyIdle),
      );

      final PairingCancelOutcome result = await service.cancelPairing();

      expect(result.status, PairingCancelStatus.alreadyIdle);
    });

    test(
      'Method cancelPairing throws malformed_message for an outcome not valid for this exchange',
      () async {
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.renotified),
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
                  contains('pairing_cancel'),
                ),
          ),
        );
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );

    test(
      'Method cancelPairing throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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

    test(
      'Method cancelPairing propagates a connection failure without changing storage',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        when(
          () => requestManager.sendAndAwait(
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
        final PersistedClientState state = await storage.load();
        expect(state.credential, 'cred');
        expect(state.recoveryState, PairingRecoveryState.confirming);
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );
  });

  group('Method confirmPairingCode behaves correctly', () {
    test(
      'Method confirmPairingCode persists the issued credential with a CONFIRMING recovery state',
      () async {
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
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
      'Method confirmPairingCode throws malformed_message when credential_issued carries no credential',
      () async {
        stubSendAndAwait(
          requestManager,
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
      },
    );

    test(
      'Method confirmPairingCode throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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

    test(
      'Method confirmPairingCode sends the code and displayName in the outgoing payload',
      () async {
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
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
                    policy: const RequestPolicy(
                      retrySafe: false,
                      requiredTrustState: DovahLinkTrustState.unpaired,
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
      'Method confirmPairingCode throws DovahLinkPairingException for a rejected code without persisting',
      () async {
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.expired),
        );

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

  group('Method acknowledgeTrustedCredential behaves correctly', () {
    test(
      'Method acknowledgeTrustedCredential marks the session trusted and clears the recovery state on a trusted outcome',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.trusted),
        );

        await service.acknowledgeTrustedCredential('cred');

        verify(() => messageReceiver.ensureReceiving()).called(1);
        verify(
          () => requestManager.sendAndAwait(
            messageType: ProtocolMessageType.pairingAck,
            payload: <String, dynamic>{'credential': 'cred'},
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: const RequestPolicy(
              retrySafe: true,
              requiredTrustState: DovahLinkTrustState.unpaired,
              timeoutClass: TimeoutClass.short,
            ),
          ),
        ).called(1);
        verify(() => sessionTrustWriter.markTrusted()).called(1);
        final PersistedClientState state = await storage.load();
        expect(state.recoveryState, PairingRecoveryState.none);
        expect(state.credential, 'cred');
      },
    );

    test(
      'Method acknowledgeTrustedCredential also marks the session trusted on an already_trusted outcome',
      () async {
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.alreadyTrusted),
        );

        await service.acknowledgeTrustedCredential('cred');

        verify(() => sessionTrustWriter.markTrusted()).called(1);
      },
    );

    test(
      'Method acknowledgeTrustedCredential never marks the session trusted and throws '
      'DovahLinkPairingException for a rejected acknowledgement',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.pendingNotFound),
        );

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
      'Method acknowledgeTrustedCredential never marks the session trusted for a plain outcome rejection (not pendingNotFound)',
      () async {
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.expired),
        );

        await expectLater(
          service.acknowledgeTrustedCredential('cred'),
          throwsA(isA<DovahLinkPairingException>()),
        );
        verifyNever(() => sessionTrustWriter.markTrusted());
      },
    );

    test(
      'Method acknowledgeTrustedCredential throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          requestManager,
          buildEnvelope(
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

  group('Method recoverPendingPairing behaves correctly', () {
    test(
      'Method recoverPendingPairing is a no-op returning unpaired when no confirmation is outstanding',
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
      'Method recoverPendingPairing is a no-op returning unpaired when confirming but no credential is stored',
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
      'Method recoverPendingPairing retries the stored credential and returns trusted on success',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.trusted),
        );

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.trusted);
        verify(
          () => requestManager.sendAndAwait(
            messageType: ProtocolMessageType.pairingAck,
            payload: <String, dynamic>{'credential': 'stored-cred'},
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: const RequestPolicy(
              retrySafe: true,
              requiredTrustState: DovahLinkTrustState.unpaired,
              timeoutClass: TimeoutClass.short,
            ),
          ),
        ).called(1);
        verify(() => sessionTrustWriter.markTrusted()).called(1);
      },
    );

    test(
      'Method recoverPendingPairing discards the credential and resets to unpaired when the bridge '
      'reports pending_not_found',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.pendingNotFound),
        );

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        final PersistedClientState state = await storage.load();
        expect(state.credential, isNull);
        expect(state.clientId, 'client-1');
      },
    );

    test(
      'Method recoverPendingPairing leaves the CONFIRMING state untouched and rethrows for any other failure',
      () async {
        await storage.save(
          const PersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(
          requestManager,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.expired),
        );

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
