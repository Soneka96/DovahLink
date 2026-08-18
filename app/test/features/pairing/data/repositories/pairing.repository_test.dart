import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/data/repositories/pairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

import '../../fixtures/pairing.fixture.dart';

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
    test('is usable as IPairingRepository', () {
      expect(repository, isA<IPairingRepository>());
    });
  });

  group('PairingRepositoryImpl.authenticate', () {
    test('returns Right when the data source succeeds', () async {
      final PairingHandshakeEntity handshake = buildPairingHandshakeEntity();
      when(
        () => mockDataSource.authenticate(),
      ).thenAnswer((_) async => Right(handshake));

      final Either<Failure, PairingHandshakeEntity> result = await repository
          .authenticate();

      expect(result, Right<Failure, PairingHandshakeEntity>(handshake));
      verify(() => mockDataSource.authenticate()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });

    test('returns Left when the data source fails', () async {
      const NetworkFailure failure = NetworkFailure('failed');
      when(
        () => mockDataSource.authenticate(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, PairingHandshakeEntity> result = await repository
          .authenticate();

      expect(result, const Left<Failure, PairingHandshakeEntity>(failure));
      verify(() => mockDataSource.authenticate()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });
  });

  group('PairingRepositoryImpl.requestPairingCode', () {
    test('returns Right when the data source succeeds', () async {
      when(
        () => mockDataSource.requestPairingCode(),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await repository
          .requestPairingCode();

      expect(result, const Right<Failure, Unit>(unit));
      verify(() => mockDataSource.requestPairingCode()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });

    test('returns Left when the data source fails', () async {
      const PairingFailure failure = PairingFailure('unavailable');
      when(
        () => mockDataSource.requestPairingCode(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await repository
          .requestPairingCode();

      expect(result, const Left<Failure, Unit>(failure));
      verify(() => mockDataSource.requestPairingCode()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });
  });

  group('PairingRepositoryImpl.confirmPairingCode', () {
    test('returns Right when the data source succeeds', () async {
      when(
        () => mockDataSource.confirmPairingCode(
          code: '123456',
          displayName: 'Desktop',
        ),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await repository.confirmPairingCode(
        code: '123456',
        displayName: 'Desktop',
      );

      expect(result, const Right<Failure, Unit>(unit));
      verify(
        () => mockDataSource.confirmPairingCode(
          code: '123456',
          displayName: 'Desktop',
        ),
      ).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });

    test('returns Left when the data source fails', () async {
      const PairingFailure failure = PairingFailure('invalid');
      when(
        () => mockDataSource.confirmPairingCode(
          code: '000000',
          displayName: null,
        ),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await repository.confirmPairingCode(
        code: '000000',
      );

      expect(result, const Left<Failure, Unit>(failure));
      verify(
        () => mockDataSource.confirmPairingCode(
          code: '000000',
          displayName: null,
        ),
      ).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });
  });

  group('PairingRepositoryImpl.disconnect', () {
    test('returns Right when the data source succeeds', () async {
      when(
        () => mockDataSource.disconnect(),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await repository.disconnect();

      expect(result, const Right<Failure, Unit>(unit));
      verify(() => mockDataSource.disconnect()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });

    test('returns Left when the data source fails', () async {
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

  group('PairingRepositoryImpl.requestPairingRenotify', () {
    test('returns Right with null when renotify succeeds', () async {
      when(
        () => mockDataSource.requestPairingRenotify(),
      ).thenAnswer((_) async => const Right(null));

      final Either<Failure, int?> result = await repository
          .requestPairingRenotify();

      expect(result, const Right<Failure, int?>(null));
      verify(() => mockDataSource.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });

    test('returns Right with cooldown seconds when in cooldown', () async {
      when(
        () => mockDataSource.requestPairingRenotify(),
      ).thenAnswer((_) async => const Right(5));

      final Either<Failure, int?> result = await repository
          .requestPairingRenotify();

      expect(result, const Right<Failure, int?>(5));
      verify(() => mockDataSource.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });

    test('returns Left when the data source fails', () async {
      const PairingFailure failure = PairingFailure('no challenge active');
      when(
        () => mockDataSource.requestPairingRenotify(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, int?> result = await repository
          .requestPairingRenotify();

      expect(result, const Left<Failure, int?>(failure));
      verify(() => mockDataSource.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });
  });

  group('PairingRepositoryImpl.cancelPairing', () {
    test('returns Right when the data source succeeds', () async {
      when(
        () => mockDataSource.cancelPairing(),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await repository.cancelPairing();

      expect(result, const Right<Failure, Unit>(unit));
      verify(() => mockDataSource.cancelPairing()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });

    test('returns Left when the data source fails', () async {
      const NetworkFailure failure = NetworkFailure('failed');
      when(
        () => mockDataSource.cancelPairing(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await repository.cancelPairing();

      expect(result, const Left<Failure, Unit>(failure));
      verify(() => mockDataSource.cancelPairing()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });
  });
}
