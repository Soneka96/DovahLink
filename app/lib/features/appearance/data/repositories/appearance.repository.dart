import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/appearance/data/datasources/appearance_local.datasource.dart';
import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Implements [IAppearanceRepository] over [IAppearanceLocalDataSource].
class AppearanceRepository implements IAppearanceRepository {
  /// Creates a repository backed by [_localDataSource].
  AppearanceRepository(this._localDataSource);

  /// The local data source this repository delegates to.
  final IAppearanceLocalDataSource _localDataSource;

  /// See [IAppearanceRepository.loadPreset].
  @override
  Future<Either<Failure, DovahThemePreset>> loadPreset() =>
      _localDataSource.loadPreset();

  /// See [IAppearanceRepository.savePreset].
  @override
  Future<Either<Failure, Unit>> savePreset(DovahThemePreset preset) =>
      _localDataSource.savePreset(preset);
}
