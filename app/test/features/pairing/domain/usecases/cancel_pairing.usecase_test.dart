import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/cancel_pairing.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Mocks the pairing repository for [CancelPairingUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [CancelPairingUseCase] forwarding behavior.
void main() {
  late MockIPairingRepository mockRepository;
  late CancelPairingUseCase useCase;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = CancelPairingUseCase(mockRepository);
  });

  group('Usecase CancelPairingUseCase returns the correct value', () {
    test('returns Right when repository succeeds', () async {
      when(
        () => mockRepository.cancelPairing(),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await useCase(NoParams());

      expect(result, const Right(unit));
      verify(() => mockRepository.cancelPairing()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left with NetworkFailure when repository fails', () async {
      const NetworkFailure failure = NetworkFailure('failed');
      when(
        () => mockRepository.cancelPairing(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await useCase(NoParams());

      expect(result, const Left(failure));
      verify(() => mockRepository.cancelPairing()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left with PairingFailure when repository fails', () async {
      const PairingFailure failure = PairingFailure('expired');
      when(
        () => mockRepository.cancelPairing(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await useCase(NoParams());

      expect(result, const Left(failure));
      verify(() => mockRepository.cancelPairing()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
