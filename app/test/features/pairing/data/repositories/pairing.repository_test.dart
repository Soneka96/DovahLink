import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/data/repositories/pairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

import '../../../../fixtures/fixtures.dart';

/// Mocks the remote data source for [PairingRepositoryImpl] tests.
class MockPairingRemoteDataSource extends Mock
    implements PairingRemoteDataSource {}

/// Exercises [PairingRepositoryImpl]'s pass-through delegation.
void main() {
  late MockPairingRemoteDataSource mockDataSource;
  late PairingRepositoryImpl repository;

  setUp(() {
    mockDataSource = MockPairingRemoteDataSource();
    repository = PairingRepositoryImpl(mockDataSource);
  });

  group('PairingRepositoryImpl', () {
    test('PairingRepositoryImpl is usable as IPairingRepository', () {
      expect(repository, isA<IPairingRepository>());
    });
  });

  group('Method authenticate behaves correctly', () {
    test(
      'Method authenticate returns Right when the data source succeeds',
      () async {
        final PairingHandshakeEntity handshake =
            Fixtures.buildPairingHandshakeEntity();
        when(
          () => mockDataSource.authenticate(),
        ).thenAnswer((_) async => Right(handshake));

        final Either<Failure, PairingHandshakeEntity> result = await repository
            .authenticate();

        expect(result, Right<Failure, PairingHandshakeEntity>(handshake));
        verify(() => mockDataSource.authenticate()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test(
      'Method authenticate returns Left when the data source fails',
      () async {
        const NetworkFailure failure = NetworkFailure('failed');
        when(
          () => mockDataSource.authenticate(),
        ).thenAnswer((_) async => const Left(failure));

        final Either<Failure, PairingHandshakeEntity> result = await repository
            .authenticate();

        expect(result, const Left<Failure, PairingHandshakeEntity>(failure));
        verify(() => mockDataSource.authenticate()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );
  });

  group('Method requestPairingCode behaves correctly', () {
    test(
      'Method requestPairingCode returns Right with null when the data source reports no expiry',
      () async {
        when(
          () => mockDataSource.requestPairingCode(),
        ).thenAnswer((_) async => const Right(null));

        final Either<Failure, int?> result = await repository
            .requestPairingCode();

        expect(result, const Right<Failure, int?>(null));
        verify(() => mockDataSource.requestPairingCode()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test(
      'Method requestPairingCode returns Right with expiresInSeconds when the data source succeeds',
      () async {
        when(
          () => mockDataSource.requestPairingCode(),
        ).thenAnswer((_) async => const Right(30));

        final Either<Failure, int?> result = await repository
            .requestPairingCode();

        expect(result, const Right<Failure, int?>(30));
        verify(() => mockDataSource.requestPairingCode()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test(
      'Method requestPairingCode returns Left when the data source fails',
      () async {
        const PairingFailure failure = PairingFailure('unavailable');
        when(
          () => mockDataSource.requestPairingCode(),
        ).thenAnswer((_) async => const Left(failure));

        final Either<Failure, int?> result = await repository
            .requestPairingCode();

        expect(result, const Left<Failure, int?>(failure));
        verify(() => mockDataSource.requestPairingCode()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );
  });

  group('Method confirmPairingCode behaves correctly', () {
    test(
      'Method confirmPairingCode returns Right when the data source succeeds',
      () async {
        when(
          () => mockDataSource.confirmPairingCode(
            code: '123456',
            displayName: 'Desktop',
          ),
        ).thenAnswer((_) async => const Right(unit));

        final Either<Failure, Unit> result = await repository
            .confirmPairingCode(code: '123456', displayName: 'Desktop');

        expect(result, const Right<Failure, Unit>(unit));
        verify(
          () => mockDataSource.confirmPairingCode(
            code: '123456',
            displayName: 'Desktop',
          ),
        ).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test(
      'Method confirmPairingCode returns Left when the data source fails',
      () async {
        const PairingFailure failure = PairingFailure('invalid');
        when(
          () => mockDataSource.confirmPairingCode(
            code: '000000',
            displayName: null,
          ),
        ).thenAnswer((_) async => const Left(failure));

        final Either<Failure, Unit> result = await repository
            .confirmPairingCode(code: '000000');

        expect(result, const Left<Failure, Unit>(failure));
        verify(
          () => mockDataSource.confirmPairingCode(
            code: '000000',
            displayName: null,
          ),
        ).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );
  });

  group('Method disconnect behaves correctly', () {
    test(
      'Method disconnect returns Right when the data source succeeds',
      () async {
        when(
          () => mockDataSource.disconnect(),
        ).thenAnswer((_) async => const Right(unit));

        final Either<Failure, Unit> result = await repository.disconnect();

        expect(result, const Right<Failure, Unit>(unit));
        verify(() => mockDataSource.disconnect()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test('Method disconnect returns Left when the data source fails', () async {
      const NetworkFailure failure = NetworkFailure('failed');
      when(
        () => mockDataSource.disconnect(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await repository.disconnect();

      expect(result, const Left<Failure, Unit>(failure));
      verify(() => mockDataSource.disconnect()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });
  });

  group('Method requestPairingRenotify behaves correctly', () {
    test(
      'Method requestPairingRenotify returns Right with null when renotify succeeds',
      () async {
        when(
          () => mockDataSource.requestPairingRenotify(),
        ).thenAnswer((_) async => const Right(null));

        final Either<Failure, int?> result = await repository
            .requestPairingRenotify();

        expect(result, const Right<Failure, int?>(null));
        verify(() => mockDataSource.requestPairingRenotify()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test(
      'Method requestPairingRenotify returns Right with cooldown seconds when in cooldown',
      () async {
        when(
          () => mockDataSource.requestPairingRenotify(),
        ).thenAnswer((_) async => const Right(5));

        final Either<Failure, int?> result = await repository
            .requestPairingRenotify();

        expect(result, const Right<Failure, int?>(5));
        verify(() => mockDataSource.requestPairingRenotify()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test(
      'Method requestPairingRenotify returns Left when the data source fails',
      () async {
        const PairingFailure failure = PairingFailure('no challenge active');
        when(
          () => mockDataSource.requestPairingRenotify(),
        ).thenAnswer((_) async => const Left(failure));

        final Either<Failure, int?> result = await repository
            .requestPairingRenotify();

        expect(result, const Left<Failure, int?>(failure));
        verify(() => mockDataSource.requestPairingRenotify()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );
  });

  group('Method cancelPairing behaves correctly', () {
    test(
      'Method cancelPairing returns Right when the data source succeeds',
      () async {
        when(
          () => mockDataSource.cancelPairing(),
        ).thenAnswer((_) async => const Right(unit));

        final Either<Failure, Unit> result = await repository.cancelPairing();

        expect(result, const Right<Failure, Unit>(unit));
        verify(() => mockDataSource.cancelPairing()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test(
      'Method cancelPairing returns Left when the data source fails',
      () async {
        const NetworkFailure failure = NetworkFailure('failed');
        when(
          () => mockDataSource.cancelPairing(),
        ).thenAnswer((_) async => const Left(failure));

        final Either<Failure, Unit> result = await repository.cancelPairing();

        expect(result, const Left<Failure, Unit>(failure));
        verify(() => mockDataSource.cancelPairing()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );
  });

  group('Property connectionStatus behaves correctly', () {
    test(
      'Property connectionStatus delegates to the data source stream',
      () async {
        when(() => mockDataSource.connectionStatus).thenAnswer(
          (_) => Stream<PairingConnectionStatus>.value(
            PairingConnectionStatus.lost,
          ),
        );

        await expectLater(
          repository.connectionStatus,
          emits(PairingConnectionStatus.lost),
        );
        verify(() => mockDataSource.connectionStatus).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );
  });
}
