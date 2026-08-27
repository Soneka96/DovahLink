import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/disconnect.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Mocks the pairing repository for [DisconnectUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [DisconnectUseCase] success and failure forwarding.
void main() {
  late MockIPairingRepository mockRepository;
  late DisconnectUseCase useCase;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = DisconnectUseCase(mockRepository);
  });

  group('Usecase DisconnectUseCase returns the correct value', () {
    test('returns Right when repository succeeds', () async {
      when(
        () => mockRepository.disconnect(),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await useCase(NoParams());

      expect(result, const Right(unit));
      verify(() => mockRepository.disconnect()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left when repository fails', () async {
      const NetworkFailure failure = NetworkFailure('failed');
      when(
        () => mockRepository.disconnect(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await useCase(NoParams());

      expect(result, const Left(failure));
      verify(() => mockRepository.disconnect()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
