import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/domain/repositories/Iconnection.repository.dart';
import 'package:dovahlink_client/features/connection/domain/usecases/connect.usecase.dart';
import 'package:dovahlink_client/features/connection/domain/usecases/params/connect.params.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

import '../../fixtures/connection.fixture.dart';

/// Mocks the connection repository for [ConnectUseCase] tests.
class MockIConnectionRepository extends Mock implements IConnectionRepository {}

/// Exercises [ConnectUseCase] success and failure forwarding.
void main() {
  late MockIConnectionRepository mockRepository;
  late ConnectUseCase useCase;

  setUp(() {
    mockRepository = MockIConnectionRepository();
    useCase = ConnectUseCase(mockRepository);
  });

  group('Usecase ConnectUseCase returns the correct value', () {
    test('returns Right when repository succeeds', () async {
      when(
        () => mockRepository.connect(token: 'token-1', clientId: 'client-1'),
      ).thenAnswer((_) async => Right(buildConnectionSessionEntity()));

      final Either<Failure, ConnectionSessionEntity> result = await useCase(
        const ConnectParams(token: 'token-1', clientId: 'client-1'),
      );

      expect(result, Right(buildConnectionSessionEntity()));
      verify(
        () => mockRepository.connect(token: 'token-1', clientId: 'client-1'),
      ).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left when repository fails', () async {
      const NetworkFailure failure = NetworkFailure('failed');
      when(
        () => mockRepository.connect(token: 'token-1', clientId: 'client-1'),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, ConnectionSessionEntity> result = await useCase(
        const ConnectParams(token: 'token-1', clientId: 'client-1'),
      );

      expect(result, const Left(failure));
      verify(
        () => mockRepository.connect(token: 'token-1', clientId: 'client-1'),
      ).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
