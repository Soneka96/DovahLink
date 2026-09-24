import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Connects and authenticates through [IPairingRepository].
class AuthenticateUseCase
    extends UseCase<Either<Failure, PairingHandshake>, NoParams> {
  /// Repository this use case delegates to.
  final IPairingRepository _repository;

  /// Creates a use case backed by [IPairingRepository].
  AuthenticateUseCase(this._repository);

  /// See [UseCase.call].
  @override
  Future<Either<Failure, PairingHandshake>> call(NoParams params) {
    return _repository.authenticate();
  }
}
