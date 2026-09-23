import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Loads the persisted theme preset through [IAppearanceRepository].
class LoadThemePresetUseCase
    extends UseCase<Either<Failure, DovahThemePreset>, NoParams> {
  /// Creates a use case backed by [IAppearanceRepository].
  LoadThemePresetUseCase(this._repository);

  /// Repository this use case delegates to.
  final IAppearanceRepository _repository;

  /// See [UseCase.call].
  @override
  Future<Either<Failure, DovahThemePreset>> call(NoParams params) {
    return _repository.loadPreset();
  }
}
