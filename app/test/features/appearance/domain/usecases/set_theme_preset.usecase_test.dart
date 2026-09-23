import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/params/set_theme_preset.params.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/set_theme_preset.usecase.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Mocks the appearance repository for [SetThemePresetUseCase] tests.
class MockIAppearanceRepository extends Mock implements IAppearanceRepository {}

/// Exercises [SetThemePresetUseCase] success and failure forwarding.
void main() {
  late MockIAppearanceRepository mockRepository;
  late SetThemePresetUseCase useCase;

  setUp(() {
    mockRepository = MockIAppearanceRepository();
    useCase = SetThemePresetUseCase(mockRepository);
  });

  group('Method call behaves correctly', () {
    test('Method call returns Right when the repository succeeds', () async {
      when(
        () => mockRepository.savePreset(DovahThemePreset.hearth),
      ).thenAnswer((_) async => const Right(unit));

      final Either<Failure, Unit> result = await useCase(
        const SetThemePresetParams(preset: DovahThemePreset.hearth),
      );

      expect(result, const Right(unit));
      verify(
        () => mockRepository.savePreset(DovahThemePreset.hearth),
      ).called(1);
      verifyNoMoreInteractions(mockRepository);
    });

    test('Method call returns Left when the repository fails', () async {
      const DatabaseFailure failure = DatabaseFailure('unavailable');
      when(
        () => mockRepository.savePreset(DovahThemePreset.hearth),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await useCase(
        const SetThemePresetParams(preset: DovahThemePreset.hearth),
      );

      expect(result, const Left(failure));
      verify(
        () => mockRepository.savePreset(DovahThemePreset.hearth),
      ).called(1);
      verifyNoMoreInteractions(mockRepository);
    });
  });
}
