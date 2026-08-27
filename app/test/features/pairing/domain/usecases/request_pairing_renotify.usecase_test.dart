import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing_renotify.usecase.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Mocks the pairing repository for [RequestPairingRenotifyUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [RequestPairingRenotifyUseCase] forwarding behavior.
void main() {
  late MockIPairingRepository mockRepository;
  late RequestPairingRenotifyUseCase useCase;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = RequestPairingRenotifyUseCase(mockRepository);
  });

  group('Usecase RequestPairingRenotifyUseCase returns the correct value', () {
    test('returns Right with null when renotify succeeds', () async {
      when(
        () => mockRepository.requestPairingRenotify(),
      ).thenAnswer((_) async => const Right(null));

      final Either<Failure, int?> result = await useCase(NoParams());

      expect(result, const Right(null));
      verify(() => mockRepository.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Right with cooldown seconds when in cooldown', () async {
      when(
        () => mockRepository.requestPairingRenotify(),
      ).thenAnswer((_) async => const Right(5));

      final Either<Failure, int?> result = await useCase(NoParams());

      expect(result, const Right(5));
      verify(() => mockRepository.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left with PairingFailure when repository fails', () async {
      const PairingFailure failure = PairingFailure('no challenge active');
      when(
        () => mockRepository.requestPairingRenotify(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, int?> result = await useCase(NoParams());

      expect(result, const Left(failure));
      verify(() => mockRepository.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left with NetworkFailure when repository fails', () async {
      const NetworkFailure failure = NetworkFailure('connection lost');
      when(
        () => mockRepository.requestPairingRenotify(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, int?> result = await useCase(NoParams());

      expect(result, const Left(failure));
      verify(() => mockRepository.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
