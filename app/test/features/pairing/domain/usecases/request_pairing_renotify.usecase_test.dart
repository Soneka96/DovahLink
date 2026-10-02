import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_renotify_result.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/request_pairing_renotify.usecase.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import '../../../../fixtures/fixtures.dart';

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
    test(
      'RequestPairingRenotifyUseCase returns the typed redisplay result',
      () async {
        when(
          () => mockRepository.requestPairingRenotify(),
        ).thenAnswer((_) async => Right(Fixtures.buildPairingRenotifyResult()));

        final Either<Failure, PairingRenotifyResult> result = await useCase(
          NoParams(),
        );

        expect(result, Right(Fixtures.buildPairingRenotifyResult()));
        verify(() => mockRepository.requestPairingRenotify()).called(1);
        verifyNoMoreInteractions(mockRepository);
      },
    );

    test(
      'RequestPairingRenotifyUseCase preserves cooldown as a distinct result',
      () async {
        when(() => mockRepository.requestPairingRenotify()).thenAnswer(
          (_) async => Right(
            Fixtures.buildPairingRenotifyResult(
              outcome: PairingRenotifyOutcome.cooldown,
              retryAfterSeconds: 3,
            ),
          ),
        );

        final Either<Failure, PairingRenotifyResult> result = await useCase(
          NoParams(),
        );

        expect(
          result,
          Right(
            Fixtures.buildPairingRenotifyResult(
              outcome: PairingRenotifyOutcome.cooldown,
              retryAfterSeconds: 3,
            ),
          ),
        );
        verify(() => mockRepository.requestPairingRenotify()).called(1);
        verifyNoMoreInteractions(mockRepository);
      },
    );

    test('returns Left with PairingFailure when repository fails', () async {
      const PairingFailure failure = PairingFailure('no challenge active');
      when(
        () => mockRepository.requestPairingRenotify(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, PairingRenotifyResult> result = await useCase(
        NoParams(),
      );

      expect(result, const Left(failure));
      verify(() => mockRepository.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('returns Left with NetworkFailure when repository fails', () async {
      const NetworkFailure failure = NetworkFailure('connection lost');
      when(
        () => mockRepository.requestPairingRenotify(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, PairingRenotifyResult> result = await useCase(
        NoParams(),
      );

      expect(result, const Left(failure));
      verify(() => mockRepository.requestPairingRenotify()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
