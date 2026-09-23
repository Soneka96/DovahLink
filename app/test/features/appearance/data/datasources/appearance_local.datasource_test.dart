import 'package:flutter/services.dart' show PlatformException;
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/appearance/data/datasources/appearance_local.datasource.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// A controllable preferences store for appearance persistence tests.
class MockSharedPreferencesAsync extends Mock
    implements SharedPreferencesAsync {}

/// Exercises [AppearanceLocalDataSource] against isolated preference doubles.
void main() {
  /// Builds an isolated data source with an in-memory preference value.
  AppearanceLocalDataSource buildDataSource({
    Map<String, Object> initialValues = const {},
  }) {
    final MockSharedPreferencesAsync preferences = MockSharedPreferencesAsync();
    String? storedPreset =
        initialValues['dovahlink.appearance.themePreset'] as String?;
    when(
      () => preferences.getString('dovahlink.appearance.themePreset'),
    ).thenAnswer((_) async => storedPreset);
    when(
      () => preferences.setString('dovahlink.appearance.themePreset', any()),
    ).thenAnswer((invocation) async {
      storedPreset = invocation.positionalArguments[1] as String;
    });
    return AppearanceLocalDataSource(preferences);
  }

  group('Method loadPreset behaves correctly', () {
    test(
      'Method loadPreset returns defaultThemePreset when nothing is stored',
      () async {
        final AppearanceLocalDataSource dataSource = buildDataSource();

        final Either<Failure, DovahThemePreset> result = await dataSource
            .loadPreset();

        expect(result, isA<Either<Failure, DovahThemePreset>>());
        expect(
          result,
          const Right<Failure, DovahThemePreset>(defaultThemePreset),
        );
      },
    );

    test(
      'Method loadPreset returns the stored preset when a valid name is stored',
      () async {
        final AppearanceLocalDataSource dataSource = buildDataSource(
          initialValues: {'dovahlink.appearance.themePreset': 'frostbound'},
        );

        final Either<Failure, DovahThemePreset> result = await dataSource
            .loadPreset();

        expect(
          result,
          const Right<Failure, DovahThemePreset>(DovahThemePreset.frostbound),
        );
      },
    );

    test(
      'Method loadPreset returns defaultThemePreset when the stored value is not a known preset',
      () async {
        final AppearanceLocalDataSource dataSource = buildDataSource(
          initialValues: {
            'dovahlink.appearance.themePreset':
                'a-preset-that-no-longer-exists',
          },
        );

        final Either<Failure, DovahThemePreset> result = await dataSource
            .loadPreset();

        expect(
          result,
          const Right<Failure, DovahThemePreset>(defaultThemePreset),
        );
      },
    );

    test(
      'Method loadPreset returns DatabaseFailure when async storage throws PlatformException',
      () async {
        final MockSharedPreferencesAsync preferences =
            MockSharedPreferencesAsync();
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenAnswer(
          (_) => Future<String?>.error(
            PlatformException(
              code: 'read-failed',
              message: 'Storage unavailable.',
            ),
          ),
        );
        final AppearanceLocalDataSource dataSource = AppearanceLocalDataSource(
          preferences,
        );

        final Either<Failure, DovahThemePreset> result = await dataSource
            .loadPreset();

        expect(
          result,
          const Left<Failure, DovahThemePreset>(
            DatabaseFailure('Storage unavailable.'),
          ),
        );
      },
    );

    test(
      'Method loadPreset uses a fallback message when PlatformException has no message',
      () async {
        final MockSharedPreferencesAsync preferences =
            MockSharedPreferencesAsync();
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenAnswer(
          (_) => Future<String?>.error(PlatformException(code: 'read-failed')),
        );
        final AppearanceLocalDataSource dataSource = AppearanceLocalDataSource(
          preferences,
        );

        final Either<Failure, DovahThemePreset> result = await dataSource
            .loadPreset();

        expect(
          result,
          const Left<Failure, DovahThemePreset>(
            DatabaseFailure('Could not read the saved appearance.'),
          ),
        );
      },
    );

    test(
      'Method loadPreset returns the generic DatabaseFailure for unexpected errors',
      () async {
        final MockSharedPreferencesAsync preferences =
            MockSharedPreferencesAsync();
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenAnswer((_) => Future<String?>.error(StateError('unexpected')));
        final AppearanceLocalDataSource dataSource = AppearanceLocalDataSource(
          preferences,
        );

        final Either<Failure, DovahThemePreset> result = await dataSource
            .loadPreset();

        expect(
          result,
          const Left<Failure, DovahThemePreset>(
            DatabaseFailure(
              'The appearance setting could not be read or saved.',
            ),
          ),
        );
      },
    );
  });

  group('Method savePreset behaves correctly', () {
    test(
      'Method savePreset returns success when the asynchronous write completes',
      () async {
        final MockSharedPreferencesAsync preferences =
            MockSharedPreferencesAsync();
        when(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).thenAnswer((_) async {});
        final AppearanceLocalDataSource dataSource = AppearanceLocalDataSource(
          preferences,
        );

        final Either<Failure, Unit> result = await dataSource.savePreset(
          DovahThemePreset.hearth,
        );

        expect(result, const Right<Failure, Unit>(unit));
        verify(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).called(1);
      },
    );

    test(
      'Method savePreset returns DatabaseFailure when async storage throws PlatformException',
      () async {
        final MockSharedPreferencesAsync preferences =
            MockSharedPreferencesAsync();
        when(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).thenAnswer(
          (_) => Future<void>.error(
            PlatformException(
              code: 'write-failed',
              message: 'Storage unavailable.',
            ),
          ),
        );
        final AppearanceLocalDataSource dataSource = AppearanceLocalDataSource(
          preferences,
        );

        final Either<Failure, Unit> result = await dataSource.savePreset(
          DovahThemePreset.hearth,
        );

        expect(
          result,
          const Left<Failure, Unit>(DatabaseFailure('Storage unavailable.')),
        );
      },
    );

    test(
      'Method savePreset uses a fallback message when PlatformException has no message',
      () async {
        final MockSharedPreferencesAsync preferences =
            MockSharedPreferencesAsync();
        when(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).thenAnswer(
          (_) => Future<void>.error(PlatformException(code: 'write-failed')),
        );
        final AppearanceLocalDataSource dataSource = AppearanceLocalDataSource(
          preferences,
        );

        final Either<Failure, Unit> result = await dataSource.savePreset(
          DovahThemePreset.hearth,
        );

        expect(
          result,
          const Left<Failure, Unit>(
            DatabaseFailure('Could not save the appearance.'),
          ),
        );
      },
    );

    test(
      'Method savePreset returns the generic DatabaseFailure for unexpected errors',
      () async {
        final MockSharedPreferencesAsync preferences =
            MockSharedPreferencesAsync();
        when(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).thenAnswer((_) => Future<void>.error(StateError('unexpected')));
        final AppearanceLocalDataSource dataSource = AppearanceLocalDataSource(
          preferences,
        );

        final Either<Failure, Unit> result = await dataSource.savePreset(
          DovahThemePreset.hearth,
        );

        expect(
          result,
          const Left<Failure, Unit>(
            DatabaseFailure(
              'The appearance setting could not be read or saved.',
            ),
          ),
        );
      },
    );

    test(
      'Method savePreset persists the preset so a later loadPreset returns it',
      () async {
        final AppearanceLocalDataSource dataSource = buildDataSource();

        final Either<Failure, Unit> saveResult = await dataSource.savePreset(
          DovahThemePreset.hearth,
        );
        final Either<Failure, DovahThemePreset> loadResult = await dataSource
            .loadPreset();

        expect(saveResult, const Right<Failure, Unit>(unit));
        expect(
          loadResult,
          const Right<Failure, DovahThemePreset>(DovahThemePreset.hearth),
        );
      },
    );
  });
}
