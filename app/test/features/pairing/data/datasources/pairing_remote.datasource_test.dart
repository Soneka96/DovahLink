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
    test('returns a trusted handshake without recovering pending pairing', () async {
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
    });

    test('recovers an interrupted pairing when hello admits unpaired', () async {
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
    });

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

      expect(result, const Left<Failure, PairingHandshakeEntity>(NetworkFailure('socket failed')));
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

      expect(result, const Left<Failure, PairingHandshakeEntity>(NetworkFailure('bad reply')));
    });

    test('maps a storage failure to DatabaseFailure', () async {
      when(
        () => mockClient.authenticate(any()),
      ).thenThrow(const DovahLinkStorageException('corrupt store'));

      final Either<Failure, PairingHandshakeEntity> result = await dataSource
          .authenticate();

      expect(result, const Left<Failure, PairingHandshakeEntity>(DatabaseFailure('corrupt store')));
    });

    test('maps a pairing failure from recovery to a user-safe PairingFailure', () async {
      when(() => mockClient.authenticate(any())).thenAnswer(
        (_) async => const HelloResult(
          bridgeVersion: '1.2.3',
          trustState: DovahLinkTrustState.unpaired,
        ),
      );
      when(
        () => mockClient.recoverPendingPairing(),
      ).thenThrow(const DovahLinkPairingException('expired'));

      final Either<Failure, PairingHandshakeEntity> result = await dataSource
          .authenticate();

      expect(
        result,
        const Left<Failure, PairingHandshakeEntity>(
          PairingFailure('That pairing code has expired. Request a new one.'),
        ),
      );
    });

    test('maps an unexpected exception to a user-safe PairingFailure', () async {
      when(() => mockClient.authenticate(any())).thenThrow(StateError('boom'));

      final Either<Failure, PairingHandshakeEntity> result = await dataSource
          .authenticate();

      expect(
        result,
        const Left<Failure, PairingHandshakeEntity>(
          PairingFailure('Pairing could not be completed. Please try again.'),
        ),
      );
    });
  });

  group('PairingRemoteDataSourceImpl.requestPairingCode', () {
    test('returns Right when a fresh code is shown', () async {
      when(
        () => mockClient.requestPairing(),
      ).thenAnswer((_) async => PairingAvailability.available);

      final Either<Failure, Unit> result = await dataSource
          .requestPairingCode();

      expect(result, const Right<Failure, Unit>(unit));
    });

    test('returns Right when a challenge is already in progress', () async {
      when(
        () => mockClient.requestPairing(),
      ).thenAnswer((_) async => PairingAvailability.inProgress);

      final Either<Failure, Unit> result = await dataSource
          .requestPairingCode();

      expect(result, const Right<Failure, Unit>(unit));
    });

    test('returns a PairingFailure when pairing is unavailable', () async {
      when(
        () => mockClient.requestPairing(),
      ).thenAnswer((_) async => PairingAvailability.unavailable);

      final Either<Failure, Unit> result = await dataSource
          .requestPairingCode();

      expect(
        result,
        const Left<Failure, Unit>(
          PairingFailure(
            'Pairing is not available right now. Try again in a moment.',
          ),
        ),
      );
    });

    test('maps a connection failure to NetworkFailure', () async {
      when(
        () => mockClient.requestPairing(),
      ).thenThrow(const DovahLinkConnectionException('socket failed'));

      final Either<Failure, Unit> result = await dataSource
          .requestPairingCode();

      expect(result, const Left<Failure, Unit>(NetworkFailure('socket failed')));
    });

    test('maps a protocol failure to NetworkFailure', () async {
      when(() => mockClient.requestPairing()).thenThrow(
        const DovahLinkProtocolException(
          code: 'malformed_message',
          message: 'bad reply',
          retryable: false,
        ),
      );

      final Either<Failure, Unit> result = await dataSource
          .requestPairingCode();

      expect(result, const Left<Failure, Unit>(NetworkFailure('bad reply')));
    });

    test('maps an unexpected exception to a user-safe PairingFailure', () async {
      when(() => mockClient.requestPairing()).thenThrow(StateError('boom'));

      final Either<Failure, Unit> result = await dataSource
          .requestPairingCode();

      expect(
        result,
        const Left<Failure, Unit>(
          PairingFailure('Pairing could not be completed. Please try again.'),
        ),
      );
    });
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
      ).thenThrow(const DovahLinkPairingException('expired'));

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

    test('maps an invalid code to a user-safe PairingFailure', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenThrow(const DovahLinkPairingException('invalid'));

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '000000',
      );

      expect(
        result,
        const Left<Failure, Unit>(
          PairingFailure("That code isn't correct. Check Skyrim and try again."),
        ),
      );
    });

    test('maps a rate-limited code to a user-safe PairingFailure', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenThrow(const DovahLinkPairingException('rate_limited'));

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '000000',
      );

      expect(
        result,
        const Left<Failure, Unit>(
          PairingFailure('Too many attempts. Wait a moment before trying again.'),
        ),
      );
    });

    test('maps an unrecognized outcome from acknowledgement to a generic PairingFailure', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenAnswer((_) async => 'credential-1');
      when(
        () => mockClient.acknowledgeTrustedCredential('credential-1'),
      ).thenThrow(const DovahLinkPairingException('pending_not_found'));

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '123456',
      );

      expect(
        result,
        const Left<Failure, Unit>(
          PairingFailure('Pairing could not be completed. Please try again.'),
        ),
      );
    });

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

      expect(result, const Left<Failure, Unit>(NetworkFailure('socket failed')));
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

      expect(result, const Left<Failure, Unit>(DatabaseFailure('corrupt store')));
    });

    test('maps an unexpected exception to a user-safe PairingFailure', () async {
      when(
        () => mockClient.confirmPairingCode(
          code: any(named: 'code'),
          displayName: any(named: 'displayName'),
        ),
      ).thenThrow(StateError('boom'));

      final Either<Failure, Unit> result = await dataSource.confirmPairingCode(
        code: '123456',
      );

      expect(
        result,
        const Left<Failure, Unit>(
          PairingFailure('Pairing could not be completed. Please try again.'),
        ),
      );
      verifyNever(() => mockClient.acknowledgeTrustedCredential(any()));
    });
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

      expect(result, const Left<Failure, Unit>(NetworkFailure('socket failed')));
    });

    test('maps an unexpected exception to a user-safe PairingFailure', () async {
      when(() => mockClient.disconnect()).thenThrow(StateError('boom'));

      final Either<Failure, Unit> result = await dataSource.disconnect();

      expect(
        result,
        const Left<Failure, Unit>(
          PairingFailure('Pairing could not be completed. Please try again.'),
        ),
      );
    });
  });
}
