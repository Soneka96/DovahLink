import 'dart:async';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

import '../../../../fixtures/fixtures.dart';

/// Mocks the wrapped SDK client for [PairingRemoteDataSourceImpl] tests.
class MockDovahLinkClient extends Mock implements DovahLinkClient {}

/// Exercises [PairingRemoteDataSourceImpl]'s exception-to-[Failure] mapping.
void main() {
  late MockDovahLinkClient mockClient;
  late PairingRemoteDataSourceImpl dataSource;

  setUpAll(() {
    registerFallbackValue(Uri.parse('ws://127.0.0.1:58231/'));
  });

  setUp(() {
    mockClient = MockDovahLinkClient();
    dataSource = PairingRemoteDataSourceImpl(mockClient);
  });

  group('Method authenticate behaves correctly', () {
    test(
      'Method authenticate returns a trusted handshake without recovering pending pairing',
      () async {
        when(() => mockClient.authenticate(any())).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.2.3',
            trustState: DovahLinkTrustState.trusted,
          ),
        );

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          Right<Failure, PairingHandshakeEntity>(
            Fixtures.buildPairingHandshakeEntity(),
          ),
        );
        verify(() => mockClient.authenticate(defaultBridgeUri)).called(1);
        verifyNever(() => mockClient.recoverPendingPairing());
      },
    );

    test(
      'Method authenticate recovers an interrupted pairing when hello admits unpaired',
      () async {
        when(() => mockClient.authenticate(any())).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.2.3',
            trustState: DovahLinkTrustState.unpaired,
          ),
        );
        when(
          () => mockClient.recoverPendingPairing(),
        ).thenAnswer((_) async => DovahLinkTrustState.trusted);

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          Right<Failure, PairingHandshakeEntity>(
            Fixtures.buildPairingHandshakeEntity(),
          ),
        );
        verify(() => mockClient.recoverPendingPairing()).called(1);
      },
    );

    test(
      'Method authenticate reports still-unpaired when no pairing recovers',
      () async {
        when(() => mockClient.authenticate(any())).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.2.3',
            trustState: DovahLinkTrustState.unpaired,
          ),
        );
        when(
          () => mockClient.recoverPendingPairing(),
        ).thenAnswer((_) async => DovahLinkTrustState.unpaired);

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          Right<Failure, PairingHandshakeEntity>(
            Fixtures.buildPairingHandshakeEntity(trusted: false),
          ),
        );
      },
    );

    test(
      'Method authenticate carries the revoked-credential explanation through when the SDK recovered',
      () async {
        when(() => mockClient.authenticate(any())).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.2.3',
            trustState: DovahLinkTrustState.unpaired,
            recoveredFromRejectedCredential: CredentialRejectionReason.revoked,
          ),
        );
        when(
          () => mockClient.recoverPendingPairing(),
        ).thenAnswer((_) async => DovahLinkTrustState.unpaired);

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          Right<Failure, PairingHandshakeEntity>(
            Fixtures.buildPairingHandshakeEntity(
              trusted: false,
              credentialRejectedMessage:
                  "This device's trust was revoked. Requesting a new pairing code.",
            ),
          ),
        );
      },
    );

    test(
      'Method authenticate carries the blocked-credential explanation through when the SDK recovered',
      () async {
        when(() => mockClient.authenticate(any())).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.2.3',
            trustState: DovahLinkTrustState.unpaired,
            recoveredFromRejectedCredential: CredentialRejectionReason.blocked,
          ),
        );
        when(
          () => mockClient.recoverPendingPairing(),
        ).thenAnswer((_) async => DovahLinkTrustState.unpaired);

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          Right<Failure, PairingHandshakeEntity>(
            Fixtures.buildPairingHandshakeEntity(
              trusted: false,
              credentialRejectedMessage:
                  'This device is blocked by the bridge and cannot be paired again until an '
                  'administrator unblocks it.',
            ),
          ),
        );
      },
    );

    test(
      'Method authenticate carries the unrecognized-credential explanation through when the SDK recovered',
      () async {
        when(() => mockClient.authenticate(any())).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.2.3',
            trustState: DovahLinkTrustState.unpaired,
            recoveredFromRejectedCredential:
                CredentialRejectionReason.unrecognized,
          ),
        );
        when(
          () => mockClient.recoverPendingPairing(),
        ).thenAnswer((_) async => DovahLinkTrustState.unpaired);

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          Right<Failure, PairingHandshakeEntity>(
            Fixtures.buildPairingHandshakeEntity(
              trusted: false,
              credentialRejectedMessage:
                  "This device isn't recognized by this bridge. Requesting a new pairing code.",
            ),
          ),
        );
      },
    );

    test(
      'Method authenticate maps a connection failure to NetworkFailure when the session is not '
      'administratively invalidated',
      () async {
        when(
          () => mockClient.authenticate(any()),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));
        when(
          () => mockClient.connectionState,
        ).thenReturn(DovahLinkConnectionState.disconnected);

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          const Left<Failure, PairingHandshakeEntity>(
            NetworkFailure('socket failed'),
          ),
        );
      },
    );

    test(
      'Method authenticate maps a connection failure to SessionInvalidatedFailure when the '
      'client is administratively invalidated',
      () async {
        when(
          () => mockClient.authenticate(any()),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));
        when(
          () => mockClient.connectionState,
        ).thenReturn(DovahLinkConnectionState.administrativelyInvalidated);

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          const Left<Failure, PairingHandshakeEntity>(
            SessionInvalidatedFailure(
              'This device was disconnected by the bridge. Try again.',
            ),
          ),
        );
      },
    );

    test(
      'Method authenticate maps a connection failure from recoverPendingPairing to '
      'SessionInvalidatedFailure when the client is administratively '
      'invalidated',
      () async {
        when(() => mockClient.authenticate(any())).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.2.3',
            trustState: DovahLinkTrustState.unpaired,
          ),
        );
        when(
          () => mockClient.recoverPendingPairing(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));
        when(
          () => mockClient.connectionState,
        ).thenReturn(DovahLinkConnectionState.administrativelyInvalidated);

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          const Left<Failure, PairingHandshakeEntity>(
            SessionInvalidatedFailure(
              'This device was disconnected by the bridge. Try again.',
            ),
          ),
        );
      },
    );

    test(
      'Method authenticate maps a protocol failure to NetworkFailure',
      () async {
        when(() => mockClient.authenticate(any())).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          const Left<Failure, PairingHandshakeEntity>(
            NetworkFailure('bad reply'),
          ),
        );
      },
    );

    test(
      'Method authenticate maps a storage failure to DatabaseFailure',
      () async {
        when(
          () => mockClient.authenticate(any()),
        ).thenThrow(const DovahLinkStorageException('corrupt store'));

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          const Left<Failure, PairingHandshakeEntity>(
            DatabaseFailure('corrupt store'),
          ),
        );
      },
    );

    test(
      'Method authenticate maps a pairing failure from recovery to a user-safe PairingFailure',
      () async {
        when(() => mockClient.authenticate(any())).thenAnswer(
          (_) async => const HelloResult(
            bridgeVersion: '1.2.3',
            trustState: DovahLinkTrustState.unpaired,
          ),
        );
        when(
          () => mockClient.recoverPendingPairing(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          const Left<Failure, PairingHandshakeEntity>(
            PairingFailure('That pairing code has expired. Request a new one.'),
          ),
        );
      },
    );

    test(
      'Method authenticate maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(
          () => mockClient.authenticate(any()),
        ).thenThrow(StateError('boom'));

        final Either<Failure, PairingHandshakeEntity> result = await dataSource
            .authenticate();

        expect(
          result,
          const Left<Failure, PairingHandshakeEntity>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Method requestPairingCode behaves correctly', () {
    test(
      'Method requestPairingCode returns Right with expiresInSeconds when a fresh code is shown',
      () async {
        when(() => mockClient.requestPairing()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.available,
            expiresInSeconds: 30,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(result, const Right<Failure, int?>(30));
      },
    );

    test(
      'Method requestPairingCode returns Right with expiresInSeconds when a challenge is already in progress',
      () async {
        when(() => mockClient.requestPairing()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.inProgress,
            expiresInSeconds: 15,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(result, const Right<Failure, int?>(15));
      },
    );

    test(
      'Method requestPairingCode returns a PairingFailure revealing nothing when a different device owns the challenge',
      () async {
        when(() => mockClient.requestPairing()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.otherDevicePairing,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure(
              'Another device is already pairing. Try again in a moment.',
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingCode returns Right with null when available but the bridge does not report an expiry',
      () async {
        when(() => mockClient.requestPairing()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.available,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(result, const Right<Failure, int?>(null));
      },
    );

    test(
      'Method requestPairingCode returns a PairingFailure and ignores expiresInSeconds when pairing is unavailable',
      () async {
        when(() => mockClient.requestPairing()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.unavailable,
            expiresInSeconds: 30,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure(
              'Pairing is not available right now. Try again in a moment.',
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingCode maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockClient.requestPairing(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(
          result,
          const Left<Failure, int?>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method requestPairingCode maps a protocol failure to NetworkFailure',
      () async {
        when(() => mockClient.requestPairing()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(result, const Left<Failure, int?>(NetworkFailure('bad reply')));
      },
    );

    test(
      'Method requestPairingCode maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(() => mockClient.requestPairing()).thenThrow(StateError('boom'));

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Method confirmPairingCode behaves correctly', () {
    test(
      'Method confirmPairingCode confirms and acknowledges the credential in order',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenAnswer((_) async => 'credential-1');
        when(
          () => mockClient.acknowledgeTrustedCredential('credential-1'),
        ).thenAnswer((_) async {});

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456', displayName: 'Desktop');

        expect(result, const Right<Failure, Unit>(unit));
        verifyInOrder([
          () => mockClient.confirmPairingCode(
            code: '123456',
            displayName: 'Desktop',
          ),
          () => mockClient.acknowledgeTrustedCredential('credential-1'),
        ]);
      },
    );

    test(
      'Method confirmPairingCode maps an expired code to a user-safe PairingFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('That pairing code has expired. Request a new one.'),
          ),
        );
        verifyNever(() => mockClient.acknowledgeTrustedCredential(any()));
      },
    );

    test(
      'Method confirmPairingCode maps an invalid code to a retriable PairingRetriableFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.invalid));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '000000');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingRetriableFailure(
              "That code isn't correct. Check Skyrim and try again.",
            ),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a pacing-limited attempt to a retriable PairingRetriableFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(
          const DovahLinkPairingException(PairingOutcome.pacingLimited),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '000000');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingRetriableFailure('Slow down a little, then try again.'),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a hard-limit-reached code to a non-retriable PairingFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(
          const DovahLinkPairingException(PairingOutcome.hardLimitReached),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '000000');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure(
              'Too many wrong attempts. Request a new pairing code.',
            ),
          ),
        );
        expect(
          result.fold((f) => f, (_) => null),
          isNot(isA<PairingRetriableFailure>()),
        );
      },
    );

    test(
      'Method confirmPairingCode maps an unrecognized outcome from acknowledgement to a non-retriable PairingFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenAnswer((_) async => 'credential-1');
        when(
          () => mockClient.acknowledgeTrustedCredential('credential-1'),
        ).thenThrow(
          const DovahLinkPairingException(PairingOutcome.pendingNotFound),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure(
              'This pairing attempt is no longer recognized. Request a new code.',
            ),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a protocol failure to NetworkFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(result, const Left<Failure, Unit>(NetworkFailure('bad reply')));
      },
    );

    test(
      'Method confirmPairingCode maps a storage failure to DatabaseFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkStorageException('corrupt store'));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(DatabaseFailure('corrupt store')),
        );
      },
    );

    test(
      'Method confirmPairingCode maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(StateError('boom'));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
        verifyNever(() => mockClient.acknowledgeTrustedCredential(any()));
      },
    );
  });

  group('Method disconnect behaves correctly', () {
    test('Method disconnect returns Right on a clean disconnect', () async {
      when(() => mockClient.disconnect()).thenAnswer((_) async {});

      final Either<Failure, Unit> result = await dataSource.disconnect();

      expect(result, const Right<Failure, Unit>(unit));
    });

    test(
      'Method disconnect maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockClient.disconnect(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, Unit> result = await dataSource.disconnect();

        expect(
          result,
          const Left<Failure, Unit>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method disconnect maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(() => mockClient.disconnect()).thenThrow(StateError('boom'));

        final Either<Failure, Unit> result = await dataSource.disconnect();

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Method requestPairingRenotify behaves correctly', () {
    test(
      'Method requestPairingRenotify returns Right with null when the code was redisplayed',
      () async {
        when(() => mockClient.requestPairingRenotify()).thenAnswer(
          (_) async => const PairingRenotifyResult(
            status: PairingRenotifyStatus.renotified,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(result, const Right<Failure, int?>(null));
      },
    );

    test(
      'Method requestPairingRenotify returns Right with cooldown seconds when cooldown is active',
      () async {
        when(() => mockClient.requestPairingRenotify()).thenAnswer(
          (_) async => const PairingRenotifyResult(
            status: PairingRenotifyStatus.cooldown,
            retryAfterSeconds: 3,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(result, const Right<Failure, int?>(3));
      },
    );

    test(
      'Method requestPairingRenotify returns Left with PairingFailure when nothing is owned',
      () async {
        when(() => mockClient.requestPairingRenotify()).thenAnswer(
          (_) async => const PairingRenotifyResult(
            status: PairingRenotifyStatus.alreadyIdle,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('No pairing is currently active.'),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockClient.requestPairingRenotify(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps a protocol failure to NetworkFailure',
      () async {
        when(() => mockClient.requestPairingRenotify()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(result, const Left<Failure, int?>(NetworkFailure('bad reply')));
      },
    );

    test(
      'Method requestPairingRenotify maps an expired pairing outcome to a user-safe PairingFailure',
      () async {
        when(
          () => mockClient.requestPairingRenotify(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('That pairing code has expired. Request a new one.'),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps an invalid pairing outcome to a user-safe PairingFailure',
      () async {
        when(
          () => mockClient.requestPairingRenotify(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.invalid));

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure(
              "That code isn't correct. Check Skyrim and try again.",
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps a pairing outcome outside the explicit message set to a generic PairingFailure',
      () async {
        // credentialIssued is a real PairingOutcome value, just never a valid reply to
        // pairing_renotify -- exercises _pairingOutcomeMessage's defensive fallback arm the same
        // way an unrecognized wire value used to, before PairingOutcome became a closed enum.
        when(() => mockClient.requestPairingRenotify()).thenThrow(
          const DovahLinkPairingException(PairingOutcome.credentialIssued),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(
          () => mockClient.requestPairingRenotify(),
        ).thenThrow(StateError('boom'));

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Method cancelPairing behaves correctly', () {
    test(
      'Method cancelPairing returns Right when a challenge was cancelled',
      () async {
        when(() => mockClient.cancelPairing()).thenAnswer(
          (_) async =>
              const PairingCancelOutcome(status: PairingCancelStatus.cancelled),
        );

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(result, const Right<Failure, Unit>(unit));
      },
    );

    test(
      'Method cancelPairing returns Right when nothing was owned (already idle)',
      () async {
        when(() => mockClient.cancelPairing()).thenAnswer(
          (_) async => const PairingCancelOutcome(
            status: PairingCancelStatus.alreadyIdle,
          ),
        );

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(result, const Right<Failure, Unit>(unit));
      },
    );

    test(
      'Method cancelPairing maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockClient.cancelPairing(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(
          result,
          const Left<Failure, Unit>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method cancelPairing maps a protocol failure to NetworkFailure',
      () async {
        when(() => mockClient.cancelPairing()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(result, const Left<Failure, Unit>(NetworkFailure('bad reply')));
      },
    );

    test(
      'Method cancelPairing maps a pairing exception to a user-safe PairingFailure',
      () async {
        when(
          () => mockClient.cancelPairing(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('That pairing code has expired. Request a new one.'),
          ),
        );
      },
    );

    test(
      'Method cancelPairing maps a storage failure to DatabaseFailure',
      () async {
        when(
          () => mockClient.cancelPairing(),
        ).thenThrow(const DovahLinkStorageException('corrupt store'));

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(
          result,
          const Left<Failure, Unit>(DatabaseFailure('corrupt store')),
        );
      },
    );

    test(
      'Method cancelPairing maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(() => mockClient.cancelPairing()).thenThrow(StateError('boom'));

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Property connectionStatus behaves correctly', () {
    test(
      'Property connectionStatus emits lost when the client becomes reconnecting',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.lost),
        );
        connectionStates.add(DovahLinkConnectionState.reconnecting);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits lost when the client becomes reauthenticating',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.lost),
        );
        connectionStates.add(DovahLinkConnectionState.reauthenticating);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits lost when the client becomes disconnected',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.lost),
        );
        connectionStates.add(DovahLinkConnectionState.disconnected);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits restored when the client becomes connected',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.restored),
        );
        connectionStates.add(DovahLinkConnectionState.connected);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits invalidated when the client becomes administratively '
      'invalidated',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.invalidated),
        );
        connectionStates.add(
          DovahLinkConnectionState.administrativelyInvalidated,
        );

        await expectation;
      },
    );

    test(
      'Property connectionStatus does not emit for a connecting connectionState transition',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final List<PairingConnectionStatus> received =
            <PairingConnectionStatus>[];
        final StreamSubscription<PairingConnectionStatus> subscription =
            dataSource.connectionStatus.listen(received.add);
        addTearDown(subscription.cancel);

        connectionStates.add(DovahLinkConnectionState.connecting);
        await pumpEventQueue();

        expect(received, isEmpty);
      },
    );

    test(
      'Property connectionStatus emits a separate lost for each of two consecutive '
      'lost-mapped transitions, rather than coalescing them',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emitsInOrder(<Object>[
            PairingConnectionStatus.lost,
            PairingConnectionStatus.lost,
          ]),
        );
        connectionStates
          ..add(DovahLinkConnectionState.reconnecting)
          ..add(DovahLinkConnectionState.disconnected);

        await expectation;
      },
    );

    test(
      'Property connectionStatus does not report restored until a reauthenticating recovery '
      'attempt actually resolves to connected',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emitsInOrder(<Object>[
            PairingConnectionStatus.lost,
            PairingConnectionStatus.lost,
            PairingConnectionStatus.restored,
          ]),
        );
        connectionStates
          ..add(DovahLinkConnectionState.reconnecting)
          ..add(DovahLinkConnectionState.reauthenticating)
          ..add(DovahLinkConnectionState.connected);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits one status per transition in a mixed sequence',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockClient.connectionStateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emitsInOrder(<Object>[
            PairingConnectionStatus.lost,
            PairingConnectionStatus.restored,
            PairingConnectionStatus.lost,
            PairingConnectionStatus.invalidated,
          ]),
        );
        connectionStates
          ..add(DovahLinkConnectionState.reconnecting)
          ..add(DovahLinkConnectionState.connecting)
          ..add(DovahLinkConnectionState.connected)
          ..add(DovahLinkConnectionState.disconnected)
          ..add(DovahLinkConnectionState.administrativelyInvalidated);

        await expectation;
      },
    );
  });
}
