import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/connection/domain/repositories/Iconnection.repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Closes the current bridge session through [IConnectionRepository].
class DisconnectUseCase extends UseCase<Either<Failure, Unit>, NoParams> {
  /// Creates a use case backed by [IConnectionRepository].
  DisconnectUseCase(this._repository);

  /// Repository this use case delegates to.
  final IConnectionRepository _repository;

  /// See [UseCase.call].
  @override
  Future<Either<Failure, Unit>> call(NoParams params) {
    return _repository.disconnect();
  }
}
