import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Cancels the active pairing challenge or pending credential through
/// [IPairingRepository].
class CancelPairingUseCase extends UseCase<Either<Failure, Unit>, NoParams> {
  /// Creates a use case backed by [IPairingRepository].
  CancelPairingUseCase(this._repository);

  /// Repository this use case delegates to.
  final IPairingRepository _repository;

  /// See [UseCase.call].
  @override
  Future<Either<Failure, Unit>> call(NoParams params) {
    return _repository.cancelPairing();
  }
}
