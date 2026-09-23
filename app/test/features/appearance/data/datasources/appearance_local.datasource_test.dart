import 'package:flutter/services.dart' show PlatformException;
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/appearance/data/datasources/appearance_local.datasource.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// A controllable preferences implementation for write-result behavior.
class MockSharedPreferences extends Mock implements SharedPreferences {}

/// Exercises [AppearanceLocalDataSource] against the real test store and mocked failure paths.
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  Future<AppearanceLocalDataSource> buildDataSource({
    Map<String, Object> initialValues = const {},
  }) async {
    SharedPreferences.setMockInitialValues(initialValues);
    final SharedPreferences preferences = await SharedPreferences.getInstance();
    return AppearanceLocalDataSource(preferences);
  }

  group('Method loadPreset behaves correctly', () {
    test(
      'Method loadPreset returns defaultThemePreset when nothing is stored',
      () async {
        final AppearanceLocalDataSource dataSource = await buildDataSource();

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
        final AppearanceLocalDataSource dataSource = await buildDataSource(
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
        final AppearanceLocalDataSource dataSource = await buildDataSource(
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
      'Method loadPreset returns DatabaseFailure when SharedPreferences throws PlatformException',
      () async {
        final MockSharedPreferences preferences = MockSharedPreferences();
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenThrow(
          PlatformException(
            code: 'read-failed',
            message: 'Storage unavailable.',
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
        final MockSharedPreferences preferences = MockSharedPreferences();
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenThrow(PlatformException(code: 'read-failed'));
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
        final MockSharedPreferences preferences = MockSharedPreferences();
        when(
          () => preferences.getString('dovahlink.appearance.themePreset'),
        ).thenThrow(StateError('unexpected'));
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
      'Method savePreset returns DatabaseFailure when SharedPreferences rejects the write',
      () async {
        final MockSharedPreferences preferences = MockSharedPreferences();
        when(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).thenAnswer((_) async => false);
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
        verify(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).called(1);
      },
    );

    test(
      'Method savePreset returns DatabaseFailure when SharedPreferences throws PlatformException',
      () async {
        final MockSharedPreferences preferences = MockSharedPreferences();
        when(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).thenAnswer(
          (_) => Future<bool>.error(
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
        final MockSharedPreferences preferences = MockSharedPreferences();
        when(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).thenAnswer(
          (_) => Future<bool>.error(PlatformException(code: 'write-failed')),
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
        final MockSharedPreferences preferences = MockSharedPreferences();
        when(
          () => preferences.setString(
            'dovahlink.appearance.themePreset',
            DovahThemePreset.hearth.name,
          ),
        ).thenAnswer((_) => Future<bool>.error(StateError('unexpected')));
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
        final AppearanceLocalDataSource dataSource = await buildDataSource();

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
