import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/device_identity/domain/repositories/device_identity_repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Loads the resolved companion display name through [IDeviceIdentityRepository].
abstract interface class ILoadDeviceNameUseCase {
  /// Loads the resolved name.
  /// @param params Unused load parameters.
  /// @return The resolved name, or a persistence [Failure].
  Future<Either<Failure, String>> call(NoParams params);
}

/// Implements [ILoadDeviceNameUseCase] through the app-owned repository.
class LoadDeviceNameUseCase extends UseCase<Either<Failure, String>, NoParams>
    implements ILoadDeviceNameUseCase {
  /// The repository that owns local name and OS fallback access.
  final IDeviceIdentityRepository _repository;

  /// Creates a use case backed by [_repository].
  /// @param repository The owner of local name resolution.
  LoadDeviceNameUseCase(IDeviceIdentityRepository repository)
    : _repository = repository;

  /// Implements [UseCase.call].
  /// @param params Unused load parameters.
  /// @return The resolved name, or a persistence [Failure].
  @override
  Future<Either<Failure, String>> call(NoParams params) =>
      _repository.loadDisplayName();
}
