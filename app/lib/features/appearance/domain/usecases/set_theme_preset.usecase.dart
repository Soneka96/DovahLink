import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/appearance/domain/repositories/appearance_repository.dart';
import 'package:dovahlink_client/features/appearance/domain/usecases/params/set_theme_preset.params.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Persists the active theme preset through [IAppearanceRepository].
class SetThemePresetUseCase
    extends UseCase<Either<Failure, Unit>, SetThemePresetParams> {
  /// Creates a use case backed by [IAppearanceRepository].
  SetThemePresetUseCase(this._repository);

  /// Repository this use case delegates to.
  final IAppearanceRepository _repository;

  /// See [UseCase.call].
  @override
  Future<Either<Failure, Unit>> call(SetThemePresetParams params) {
    return _repository.savePreset(params.preset);
  }
}
