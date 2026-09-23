import 'package:flutter/services.dart' show PlatformException;
import 'package:fpdart/fpdart.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Wraps [SharedPreferences] for the appearance feature's local persistence.
abstract interface class IAppearanceLocalDataSource {
  /// Returns the persisted theme preset, or [defaultThemePreset] when none is stored yet or the
  /// stored value does not match a known preset.
  Future<Either<Failure, DovahThemePreset>> loadPreset();

  /// Persists [preset] as the active theme preset.
  Future<Either<Failure, Unit>> savePreset(DovahThemePreset preset);
}

/// The [SharedPreferences] key the active theme preset is stored under.
const String _themePresetPreferenceKey = 'dovahlink.appearance.themePreset';

/// The user-safe [Failure] reported for any exception this data source's typed catches don't
/// recognize.
const DatabaseFailure _unexpectedAppearanceFailure = DatabaseFailure(
  'The appearance setting could not be read or saved.',
);

/// Persists the active theme preset by name in [SharedPreferences].
class AppearanceLocalDataSource implements IAppearanceLocalDataSource {
  /// Creates a data source backed by [_preferences].
  AppearanceLocalDataSource(this._preferences);

  /// The wrapped preferences store.
  final SharedPreferences _preferences;

  /// See [IAppearanceLocalDataSource.loadPreset]. A missing or unrecognized stored value
  /// resolves to [defaultThemePreset] rather than failing: this type always has a real preset to
  /// return, so there is no meaningful unavailable/invalid outcome to surface as a [Failure].
  @override
  Future<Either<Failure, DovahThemePreset>> loadPreset() async {
    try {
      final String? stored = _preferences.getString(_themePresetPreferenceKey);
      if (stored == null) {
        return const Right(defaultThemePreset);
      }
      final DovahThemePreset resolved = DovahThemePreset.values.firstWhere(
        (DovahThemePreset preset) => preset.name == stored,
        orElse: () => defaultThemePreset,
      );
      return Right(resolved);
    } on PlatformException catch (error) {
      return Left(
        DatabaseFailure(
          error.message ?? 'Could not read the saved appearance.',
        ),
      );
    } on Object {
      return const Left(_unexpectedAppearanceFailure);
    }
  }

  /// See [IAppearanceLocalDataSource.savePreset].
  @override
  Future<Either<Failure, Unit>> savePreset(DovahThemePreset preset) async {
    try {
      await _preferences.setString(_themePresetPreferenceKey, preset.name);
      return const Right(unit);
    } on PlatformException catch (error) {
      return Left(
        DatabaseFailure(error.message ?? 'Could not save the appearance.'),
      );
    } on Object {
      return const Left(_unexpectedAppearanceFailure);
    }
  }
}
