import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/features/appearance/data/datasources/appearance_local.datasource.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Exercises [AppearanceLocalDataSource] against the real [SharedPreferences] test store. The
/// `PlatformException`/unexpected-exception catch branches are intentionally untested: the
/// plugin's test-mode in-memory store (`SharedPreferences.setMockInitialValues`) has no supported
/// way to inject a platform failure, unlike a mockable remote client.
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
  });

  group('Method savePreset behaves correctly', () {
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
