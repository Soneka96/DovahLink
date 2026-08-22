import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

import '../../fixtures/pairing.fixture.dart';

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

  group('PairingRemoteDataSourceImpl.authenticate', () {
    test(
      'returns a trusted handshake without recovering pending pairing',
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
          Right<Failure, PairingHandshakeEntity>(buildPairingHandshakeEntity()),
        );
        verify(() => mockClient.authenticate(defaultBridgeUri)).called(1);
        verifyNever(() => mockClient.recoverPendingPairing());
      },
    );

    test(
      'recovers an interrupted pairing when hello admits unpaired',
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
          Right<Failure, PairingHandshakeEntity>(buildPairingHandshakeEntity()),
        );
        verify(() => mockClient.recoverPendingPairing()).called(1);
      },
    );

    test('reports still-unpaired when no pairing recovers', () async {
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
          buildPairingHandshakeEntity(trusted: false),
        ),
      );
    });

    test(
      'carries the revoked-credential explanation through when the SDK recovered',
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
            buildPairingHandshakeEntity(
              trusted: false,
              credentialRejectedMessage:
                  "This device's trust was revoked. Requesting a new pairing code.",
            ),
          ),
        );
      },
    );

    test(
      'carries the unrecognized-credential explanation through when the SDK recovered',
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
            buildPairingHandshakeEntity(
              trusted: false,
              credentialRejectedMessage:
                  "This device isn't recognized by this bridge. Requesting a new pairing code.",
            ),
          ),
        );
      },
    );

    test('maps a connection failure to NetworkFailure', () async {
      when(
        () => mockClient.authenticate(any()),
      ).thenThrow(const DovahLinkConnectionException('socket failed'));

      final Either<Failure, PairingHandshakeEntity> result = await dataSource
          .authenticate();

      expect(
        result,
        const Left<Failure, PairingHandshakeEntity>(
          NetworkFailure('socket failed'),
        ),
      );
    });

    test('maps a protocol failure to NetworkFailure', () async {
      when(() => mockClient.authenticate(any())).thenThrow(
        const DovahLinkProtocolException(
          code: 'malformed_message',
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
    });

    test('maps a storage failure to DatabaseFailure', () async {
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
    });

    test(
      'maps a pairing failure from recovery to a user-safe PairingFailure',
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
      'maps an unexpected exception to a user-safe PairingFailure',
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

  group('PairingRemoteDataSourceImpl.requestPairingCode', () {
    test(
      'returns Right with expiresInSeconds when a fresh code is shown',
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
      'returns Right with expiresInSeconds when a challenge is already in progress',
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
      'returns a PairingFailure revealing nothing when a different device owns the challenge',
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
      'returns Right with null when available but the bridge does not report an expiry',
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
      'returns a PairingFailure and ignores expiresInSeconds when pairing is unavailable',
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

    test('maps a connection failure to NetworkFailure', () async {
      when(
        () => mockClient.requestPairing(),
      ).thenThrow(const DovahLinkConnectionException('socket failed'));

      final Either<Failure, int?> result = await dataSource
          .requestPairingCode();

      expect(
        result,
        const Left<Failure, int?>(NetworkFailure('socket failed')),
      );
    });

    test('maps a protocol failure to NetworkFailure', () async {
      when(() => mockClient.requestPairing()).thenThrow(
        const DovahLinkProtocolException(
          code: 'malformed_message',
          message: 'bad reply',
          retryable: false,
        ),
      );

      final Either<Failure, int?> result = await dataSource
          .requestPairingCode();

      expect(result, const Left<Failure, int?>(NetworkFailure('bad reply')));
    });

    test(
      'maps an unexpected exception to a user-safe PairingFailure',
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

  group('PairingRemoteDataSourceImpl.confirmPairingCode', () {
    test('confirms and acknowledges the credential in order', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenAnswer((_) async => 'credential-1');
      when(
        () => mockClient.acknowledgeTrustedCredential('credential-1'),
      ).thenAnswer((_) async {});

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '123456',
        displayName: 'Desktop',
      );

      expect(result, const Right<Failure, Unit>(unit));
      verifyInOrder([
        () => mockClient.confirmPairingCode(
          code: '123456',
          displayName: 'Desktop',
        ),
        () => mockClient.acknowledgeTrustedCredential('credential-1'),
      ]);
    });

    test('maps an expired code to a user-safe PairingFailure', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '123456',
      );

      expect(
        result,
        const Left<Failure, Unit>(
          PairingFailure('That pairing code has expired. Request a new one.'),
        ),
      );
      verifyNever(() => mockClient.acknowledgeTrustedCredential(any()));
    });

    test(
      'maps an invalid code to a retriable PairingRetriableFailure',
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
      'maps a pacing-limited attempt to a retriable PairingRetriableFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.pacingLimited));

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
      'maps a hard-limit-reached code to a non-retriable PairingFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.hardLimitReached));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '000000');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('Too many wrong attempts. Request a new pairing code.'),
          ),
        );
        expect(result.fold((f) => f, (_) => null), isNot(isA<PairingRetriableFailure>()));
      },
    );

    test(
      'maps an unrecognized outcome from acknowledgement to a non-retriable PairingFailure',
      () async {
        when(
          () => mockClient.confirmPairingCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenAnswer((_) async => 'credential-1');
        when(
          () => mockClient.acknowledgeTrustedCredential('credential-1'),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.pendingNotFound));

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

    test('maps a connection failure to NetworkFailure', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenThrow(const DovahLinkConnectionException('socket failed'));

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '123456',
      );

      expect(
        result,
        const Left<Failure, Unit>(NetworkFailure('socket failed')),
      );
    });

    test('maps a protocol failure to NetworkFailure', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenThrow(
        const DovahLinkProtocolException(
          code: 'malformed_message',
          message: 'bad reply',
          retryable: false,
        ),
      );

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '123456',
      );

      expect(result, const Left<Failure, Unit>(NetworkFailure('bad reply')));
    });

    test('maps a storage failure to DatabaseFailure', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenThrow(const DovahLinkStorageException('corrupt store'));

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '123456',
      );

      expect(
        result,
        const Left<Failure, Unit>(DatabaseFailure('corrupt store')),
      );
    });

    test(
      'maps an unexpected exception to a user-safe PairingFailure',
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

  group('PairingRemoteDataSourceImpl.disconnect', () {
    test('returns Right on a clean disconnect', () async {
      when(() => mockClient.disconnect()).thenAnswer((_) async {});

      final Either<Failure, Unit> result = await dataSource.disconnect();

      expect(result, const Right<Failure, Unit>(unit));
    });

    test('maps a connection failure to NetworkFailure', () async {
      when(
        () => mockClient.disconnect(),
      ).thenThrow(const DovahLinkConnectionException('socket failed'));

      final Either<Failure, Unit> result = await dataSource.disconnect();

      expect(
        result,
        const Left<Failure, Unit>(NetworkFailure('socket failed')),
      );
    });

    test(
      'maps an unexpected exception to a user-safe PairingFailure',
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

  group('PairingRemoteDataSourceImpl.requestPairingRenotify', () {
    test('returns Right with null when the code was redisplayed', () async {
      when(() => mockClient.requestPairingRenotify()).thenAnswer(
        (_) async => const PairingRenotifyResult(
          status: PairingRenotifyStatus.renotified,
        ),
      );

      final Either<Failure, int?> result = await dataSource
          .requestPairingRenotify();

      expect(result, const Right<Failure, int?>(null));
    });

    test(
      'returns Right with cooldown seconds when cooldown is active',
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

    test('returns Left with PairingFailure when nothing is owned', () async {
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
    });

    test('maps a connection failure to NetworkFailure', () async {
      when(
        () => mockClient.requestPairingRenotify(),
      ).thenThrow(const DovahLinkConnectionException('socket failed'));

      final Either<Failure, int?> result = await dataSource
          .requestPairingRenotify();

      expect(
        result,
        const Left<Failure, int?>(NetworkFailure('socket failed')),
      );
    });

    test('maps a protocol failure to NetworkFailure', () async {
      when(() => mockClient.requestPairingRenotify()).thenThrow(
        const DovahLinkProtocolException(
          code: 'malformed_message',
          message: 'bad reply',
          retryable: false,
        ),
      );

      final Either<Failure, int?> result = await dataSource
          .requestPairingRenotify();

      expect(result, const Left<Failure, int?>(NetworkFailure('bad reply')));
    });

    test(
      'maps an expired pairing outcome to a user-safe PairingFailure',
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
      'maps an invalid pairing outcome to a user-safe PairingFailure',
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
      'maps a pairing outcome outside the explicit message set to a generic PairingFailure',
      () async {
        // credentialIssued is a real PairingOutcome value, just never a valid reply to
        // pairing_renotify -- exercises _pairingOutcomeMessage's defensive fallback arm the same
        // way an unrecognized wire value used to, before PairingOutcome became a closed enum.
        when(
          () => mockClient.requestPairingRenotify(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.credentialIssued));

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
      'maps an unexpected exception to a user-safe PairingFailure',
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

  group('PairingRemoteDataSourceImpl.cancelPairing', () {
    test('returns Right when a challenge was cancelled', () async {
      when(() => mockClient.cancelPairing()).thenAnswer(
        (_) async =>
            const PairingCancelOutcome(status: PairingCancelStatus.cancelled),
      );

      final Either<Failure, Unit> result = await dataSource.cancelPairing();

      expect(result, const Right<Failure, Unit>(unit));
    });

    test('returns Right when nothing was owned (already idle)', () async {
      when(() => mockClient.cancelPairing()).thenAnswer(
        (_) async =>
            const PairingCancelOutcome(status: PairingCancelStatus.alreadyIdle),
      );

      final Either<Failure, Unit> result = await dataSource.cancelPairing();

      expect(result, const Right<Failure, Unit>(unit));
    });

    test('maps a connection failure to NetworkFailure', () async {
      when(
        () => mockClient.cancelPairing(),
      ).thenThrow(const DovahLinkConnectionException('socket failed'));

      final Either<Failure, Unit> result = await dataSource.cancelPairing();

      expect(
        result,
        const Left<Failure, Unit>(NetworkFailure('socket failed')),
      );
    });

    test('maps a protocol failure to NetworkFailure', () async {
      when(() => mockClient.cancelPairing()).thenThrow(
        const DovahLinkProtocolException(
          code: 'malformed_message',
          message: 'bad reply',
          retryable: false,
        ),
      );

      final Either<Failure, Unit> result = await dataSource.cancelPairing();

      expect(result, const Left<Failure, Unit>(NetworkFailure('bad reply')));
    });

    test('maps a pairing exception to a user-safe PairingFailure', () async {
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
    });

    test('maps a storage failure to DatabaseFailure', () async {
      when(
        () => mockClient.cancelPairing(),
      ).thenThrow(const DovahLinkStorageException('corrupt store'));

      final Either<Failure, Unit> result = await dataSource.cancelPairing();

      expect(
        result,
        const Left<Failure, Unit>(DatabaseFailure('corrupt store')),
      );
    });

    test(
      'maps an unexpected exception to a user-safe PairingFailure',
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
}
