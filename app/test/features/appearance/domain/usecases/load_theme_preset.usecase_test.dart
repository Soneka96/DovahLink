import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/load_theme_preset.usecase.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';

/// Mocks the appearance repository for [LoadThemePresetUseCase] tests.
class MockIAppearanceRepository extends Mock implements IAppearanceRepository {}

/// Exercises [LoadThemePresetUseCase] success and failure forwarding.
void main() {
  late MockIAppearanceRepository mockRepository;
  late LoadThemePresetUseCase useCase;

  setUp(() {
    mockRepository = MockIAppearanceRepository();
    useCase = LoadThemePresetUseCase(mockRepository);
  });

  group('Method call behaves correctly', () {
    test(
      'Method call returns Right with the repository\'s preset on success',
      () async {
        when(
          () => mockRepository.loadPreset(),
        ).thenAnswer((_) async => const Right(DovahThemePreset.frostbound));

        final Either<Failure, DovahThemePreset> result = await useCase(
          NoParams(),
        );

        expect(result, const Right(DovahThemePreset.frostbound));
        verify(() => mockRepository.loadPreset()).called(1);
        verifyNoMoreInteractions(mockRepository);
      },
    );

    test('Method call returns Left when the repository fails', () async {
      const DatabaseFailure failure = DatabaseFailure('unavailable');
      when(
        () => mockRepository.loadPreset(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, DovahThemePreset> result = await useCase(
        NoParams(),
      );

      expect(result, const Left(failure));
      verify(() => mockRepository.loadPreset()).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
