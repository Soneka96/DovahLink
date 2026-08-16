import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/confirm_pairing_code.usecase.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/confirm_pairing_code.params.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Mocks the pairing repository for [ConfirmPairingCodeUseCase] tests.
class MockIPairingRepository extends Mock implements IPairingRepository {}

/// Exercises [ConfirmPairingCodeUseCase] forwarding behavior.
void main() {
  late MockIPairingRepository mockRepository;
  late ConfirmPairingCodeUseCase useCase;

  setUp(() {
    mockRepository = MockIPairingRepository();
    useCase = ConfirmPairingCodeUseCase(mockRepository);
  });

  group('Usecase ConfirmPairingCodeUseCase returns the correct value', () {
    test('returns Right and forwards code and displayName', () async {
      when(
        () => mockRepository.confirmPairingCode(
          code: '123456',
          displayName: 'Desktop',
        ),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await useCase(
        const ConfirmPairingCodeParams(code: '123456', displayName: 'Desktop'),
      );

      expect(result, const Right(unit));
      verify(
        () => mockRepository.confirmPairingCode(
          code: '123456',
          displayName: 'Desktop',
        ),
      ).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Right and forwards null displayName when absent', () async {
      when(
        () => mockRepository.confirmPairingCode(
          code: '123456',
          displayName: null,
        ),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await useCase(
        const ConfirmPairingCodeParams(code: '123456'),
      );

      expect(result, const Right(unit));
      verify(
        () => mockRepository.confirmPairingCode(
          code: '123456',
          displayName: null,
        ),
      ).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left when repository fails', () async {
      const PairingFailure failure = PairingFailure('invalid');
      when(
        () => mockRepository.confirmPairingCode(
          code: '000000',
          displayName: null,
        ),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await useCase(
        const ConfirmPairingCodeParams(code: '000000'),
      );

      expect(result, const Left(failure));
      verify(
        () => mockRepository.confirmPairingCode(
          code: '000000',
          displayName: null,
        ),
      ).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
