import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/appearance/data/datasources/appearance_local.datasource.dart';
import 'package:dovahlink_client/features/appearance/data/repositories/appearance.repository.dart';
import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Mocks the local data source for [AppearanceRepository] tests.
class MockAppearanceLocalDataSource extends Mock
    implements IAppearanceLocalDataSource {}

/// Exercises [AppearanceRepository]'s pass-through delegation.
void main() {
  late MockAppearanceLocalDataSource mockDataSource;
  late AppearanceRepository repository;

  setUp(() {
    mockDataSource = MockAppearanceLocalDataSource();
    repository = AppearanceRepository(mockDataSource);
  });

  group('AppearanceRepository', () {
    test('AppearanceRepository is usable as IAppearanceRepository', () {
      expect(repository, isA<IAppearanceRepository>());
    });
  });

  group('Method loadPreset behaves correctly', () {
    test(
      'Method loadPreset returns Right when the data source succeeds',
      () async {
        when(
          () => mockDataSource.loadPreset(),
        ).thenAnswer((_) async => const Right(DovahThemePreset.hearth));

        final Either<Failure, DovahThemePreset> result = await repository
            .loadPreset();

        expect(
          result,
          const Right<Failure, DovahThemePreset>(DovahThemePreset.hearth),
        );
        verify(() => mockDataSource.loadPreset()).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test('Method loadPreset returns Left when the data source fails', () async {
      const DatabaseFailure failure = DatabaseFailure('failed');
      when(
        () => mockDataSource.loadPreset(),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, DovahThemePreset> result = await repository
          .loadPreset();

      expect(result, const Left<Failure, DovahThemePreset>(failure));
      verify(() => mockDataSource.loadPreset()).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });
  });

  group('Method savePreset behaves correctly', () {
    test(
      'Method savePreset returns Right when the data source succeeds',
      () async {
        when(
          () => mockDataSource.savePreset(DovahThemePreset.hearth),
        ).thenAnswer((_) async => const Right(unit));

        final Either<Failure, Unit> result = await repository.savePreset(
          DovahThemePreset.hearth,
        );

        expect(result, const Right<Failure, Unit>(unit));
        verify(
          () => mockDataSource.savePreset(DovahThemePreset.hearth),
        ).called(1);
        verifyNoMoreInteractions(mockDataSource);
      },
    );

    test('Method savePreset returns Left when the data source fails', () async {
      const DatabaseFailure failure = DatabaseFailure('failed');
      when(
        () => mockDataSource.savePreset(DovahThemePreset.hearth),
      ).thenAnswer((_) async => const Left(failure));

      final Either<Failure, Unit> result = await repository.savePreset(
        DovahThemePreset.hearth,
      );

      expect(result, const Left<Failure, Unit>(failure));
      verify(
        () => mockDataSource.savePreset(DovahThemePreset.hearth),
      ).called(1);
      verifyNoMoreInteractions(mockDataSource);
    });
  });
}
