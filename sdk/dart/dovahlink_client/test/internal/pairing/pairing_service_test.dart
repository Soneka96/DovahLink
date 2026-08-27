import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/pairing/pairing_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_trust_service.dart';
import 'package:dovahlink_client_sdk/src/pairing_cancel_outcome.dart';
import 'package:dovahlink_client_sdk/src/pairing_challenge_status.dart';
import 'package:dovahlink_client_sdk/src/pairing_renotify_result.dart';
import 'package:dovahlink_client_sdk/src/persistence/client_storage.dart';
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

/// Mock client storage -- its own persistence mechanics are covered by its own implementation's
/// test file; this file only proves [PairingService] reads and writes the right state, and
/// never touches it on a rejected outcome.
class MockClientStorage extends Mock implements IClientStorage {}

/// Builds a decoded `pairing_outcome` reply envelope carrying [outcome], with every other field
/// present-but-empty per that message's wire contract.
Envelope buildPairingOutcomeEnvelope({
  PairingOutcome outcome = PairingOutcome.alreadyIdle,
  String? credential,
  String? shortId,
  String? displayName,
  int? retryAfterSeconds,
}) => Fixtures.buildEnvelope(
  messageType: ProtocolMessageType.pairingOutcome,
  payload: <String, dynamic>{
    'outcome': _wirePairingOutcome(outcome),
    'credential': credential,
    'shortId': shortId,
    'displayName': displayName,
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
void verifyNoStorageCalls(MockClientStorage storage) {
  verifyNever(() => storage.load());
  verifyNever(() => storage.save(any()));
}

/// Runs pairing service behavior tests.
void main() {
  late MockRequestService requestService;
  late MockSessionTrustService sessionTrustService;
  late MockClientStorage storage;
  late PairingService service;

  setUpAll(() {
    registerFallbackValue(ProtocolMessageType.pairingRequest);
    registerFallbackValue(
      Fixtures.buildRequestPolicy(
        retrySafe: false,
        requiredTrustState: null,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    registerFallbackValue(Fixtures.buildPersistedClientState());
  });

  setUp(() {
    requestService = MockRequestService();
    sessionTrustService = MockSessionTrustService();
    storage = MockClientStorage();
    when(() => sessionTrustService.markTrusted()).thenReturn(null);
    when(() => storage.load()).thenAnswer(
      (_) async => Fixtures.buildPersistedClientState(clientId: 'client-1'),
    );
    when(() => storage.save(any())).thenAnswer((_) async {});
    service = PairingService(
      sessionTrustService: sessionTrustService,
      requestService: requestService,
      storage: storage,
    );
  });

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
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.renotified),
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
        expect(result.retryAfterSeconds, isNull);
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
      'Method confirmPairingCode persists the issued credential with a CONFIRMING recovery state',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.credentialIssued,
            credential: 'new-cred',
          ),
        );

        final String credential = await service.confirmPairingCode(
          code: '123456',
        );

        expect(credential, 'new-cred');
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(
              clientId: 'client-1',
              credential: 'new-cred',
              recoveryState: PairingRecoveryState.confirming,
            ),
          ),
        ).called(1);
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
            };
        for (final MapEntry<PairingOutcome, int?> entry
            in rejectedOutcomes.entries) {
          stubSendAndAwait(
            requestService,
            buildPairingOutcomeEnvelope(
              outcome: entry.key,
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

  group('Method acknowledgeTrustedCredential behaves correctly', () {
    test(
      'Method acknowledgeTrustedCredential marks the session trusted and clears the recovery state on a trusted outcome',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
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

        await service.acknowledgeTrustedCredential('cred');

        verify(
          () => requestService.sendAndAwait(
            messageType: ProtocolMessageType.pairingAck,
            payload: <String, dynamic>{'credential': 'cred'},
            expectedType: ProtocolMessageType.pairingOutcome,
            policy: Fixtures.buildRequestPolicy(),
          ),
        ).called(1);
        verify(() => sessionTrustService.markTrusted()).called(1);
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(
              clientId: 'client-1',
              credential: 'cred',
            ),
          ),
        ).called(1);
      },
    );

    test(
      'Method acknowledgeTrustedCredential also marks the session trusted on an already_trusted outcome',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'cred',
            recoveryState: PairingRecoveryState.confirming,
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

        await service.acknowledgeTrustedCredential('cred');

        verify(() => sessionTrustService.markTrusted()).called(1);
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(
              clientId: 'client-1',
              credential: 'cred',
            ),
          ),
        ).called(1);
      },
    );

    test(
      'Method acknowledgeTrustedCredential never marks the session trusted and throws '
      'DovahLinkPairingException for a rejected acknowledgement',
      () async {
        stubSendAndAwait(
          requestService,
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
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential exposes pairing_invalidated without marking trust',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(
            outcome: PairingOutcome.pairingInvalidated,
          ),
        );

        await expectLater(
          service.acknowledgeTrustedCredential('cred'),
          throwsA(
            isA<DovahLinkPairingException>().having(
              (DovahLinkPairingException e) => e.outcome,
              'outcome',
              PairingOutcome.pairingInvalidated,
            ),
          ),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential throws malformed_message for an outcome from another exchange',
      () async {
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.expired),
        );

        await expectLater(
          service.acknowledgeTrustedCredential('cred'),
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
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential throws malformed_message when pairing_outcome fails to decode',
      () async {
        stubSendAndAwait(
          requestService,
          Fixtures.buildEnvelope(
            messageType: ProtocolMessageType.pairingOutcome,
            payload: <String, dynamic>{},
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
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStorageCalls(storage);
      },
    );

    test(
      'Method acknowledgeTrustedCredential propagates a connection failure without marking trust or '
      'touching storage',
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
          service.acknowledgeTrustedCredential('cred'),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        verifyNever(() => sessionTrustService.markTrusted());
        verifyNoStorageCalls(storage);
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
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
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
      },
    );

    test(
      'Method recoverPendingPairing discards the credential and resets to unpaired when the bridge '
      'reports pending_not_found',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
        stubSendAndAwait(
          requestService,
          buildPairingOutcomeEnvelope(outcome: PairingOutcome.pendingNotFound),
        );

        final DovahLinkTrustState result = await service
            .recoverPendingPairing();

        expect(result, DovahLinkTrustState.unpaired);
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(clientId: 'client-1'),
          ),
        ).called(1);
      },
    );

    test(
      'Method recoverPendingPairing discards the credential and resets to unpaired when the bridge '
      'reports pairing_invalidated',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
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
        verify(
          () => storage.save(
            Fixtures.buildPersistedClientState(clientId: 'client-1'),
          ),
        ).called(1);
      },
    );

    test(
      'Method recoverPendingPairing leaves the CONFIRMING state untouched and rethrows for any other failure',
      () async {
        when(() => storage.load()).thenAnswer(
          (_) async => Fixtures.buildPersistedClientState(
            clientId: 'client-1',
            credential: 'stored-cred',
            recoveryState: PairingRecoveryState.confirming,
          ),
        );
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
        verifyNever(() => storage.save(any()));
      },
    );
  });
}
