import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/confirm_pairing_code.params.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Submits a pairing code through [IPairingRepository].
class ConfirmPairingCodeUseCase
    extends UseCase<Either<Failure, Unit>, ConfirmPairingCodeParams> {
  /// Creates a use case backed by [IPairingRepository].
  ConfirmPairingCodeUseCase(this._repository);

  /// Repository this use case delegates to.
  final IPairingRepository _repository;

  /// See [UseCase.call].
  @override
  Future<Either<Failure, Unit>> call(ConfirmPairingCodeParams params) {
    return _repository.confirmPairingCode(
      code: params.code,
      displayName: params.displayName,
    );
  }
}
