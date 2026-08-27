import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/authenticate.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

import '../../../../fixtures/fixtures.dart';

/// Mocks the pairing repository for [AuthenticateUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [AuthenticateUseCase] success and failure forwarding.
void main() {
  late MockIPairingRepository mockRepository;
  late AuthenticateUseCase useCase;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = AuthenticateUseCase(mockRepository);
  });

  group('Usecase AuthenticateUseCase returns the correct value', () {
    test('returns Right when repository succeeds', () async {
      final PairingHandshakeEntity handshake =
          Fixtures.buildPairingHandshakeEntity();
      when(
        () => mockRepository.authenticate(),
      ).thenAnswer((_) async => Right(handshake));

      final Either<Failure, PairingHandshakeEntity> result = await useCase(
        NoParams(),
      );

      expect(result, Right(handshake));
      verify(() => mockRepository.authenticate()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left when repository fails', () async {
      const NetworkFailure failure = NetworkFailure('failed');
      when(
        () => mockRepository.authenticate(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, PairingHandshakeEntity> result = await useCase(
        NoParams(),
      );

      expect(result, const Left(failure));
      verify(() => mockRepository.authenticate()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
