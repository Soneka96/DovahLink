import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Mocks the pairing repository for [RequestPairingUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [RequestPairingUseCase] success and failure forwarding.
void main() {
  late MockIPairingRepository mockRepository;
  late RequestPairingUseCase useCase;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = RequestPairingUseCase(mockRepository);
  });

  group('Usecase RequestPairingUseCase returns the correct value', () {
    test('returns Right when repository succeeds', () async {
      when(
        () => mockRepository.requestPairingCode(),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await useCase(NoParams());

      expect(result, const Right(unit));
      verify(() => mockRepository.requestPairingCode()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left when repository fails', () async {
      const PairingFailure failure = PairingFailure('unavailable');
      when(
        () => mockRepository.requestPairingCode(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await useCase(NoParams());

      expect(result, const Left(failure));
      verify(() => mockRepository.requestPairingCode()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
