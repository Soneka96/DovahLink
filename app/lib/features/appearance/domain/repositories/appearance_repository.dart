import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Domain boundary for reading and persisting the active DovahLink theme preset.
abstract interface class IAppearanceRepository {
  /// Returns the persisted theme preset, or the default preset when none is stored yet or the
  /// stored value is no longer recognized.
  Future<Either<Failure, DovahThemePreset>> loadPreset();

  /// Persists [preset] as the active theme preset.
  Future<Either<Failure, Unit>> savePreset(DovahThemePreset preset);
}
