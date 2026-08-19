import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Starts, or queries the status of, a pairing challenge through
/// [IPairingRepository].
class RequestPairingUseCase extends UseCase<Either<Failure, int?>, NoParams> {
  /// Creates a use case backed by [IPairingRepository].
  RequestPairingUseCase(this._repository);

  /// Repository this use case delegates to.
  final IPairingRepository _repository;

  /// See [UseCase.call].
  @override
  Future<Either<Failure, int?>> call(NoParams params) {
    return _repository.requestPairingCode();
  }
}
