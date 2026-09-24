import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_handshake.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/features/pairing/domain/usecases/params/authenticate.params.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Connects to the Host named by [AuthenticateParams] and authenticates through
/// [IPairingRepository].
class AuthenticateUseCase
    extends UseCase<Either<Failure, PairingHandshake>, AuthenticateParams> {
  /// Repository this use case delegates to.
  final IPairingRepository _repository;

  /// Creates a use case backed by [IPairingRepository].
  AuthenticateUseCase(this._repository);

  /// See [UseCase.call].
  @override
  Future<Either<Failure, PairingHandshake>> call(AuthenticateParams params) {
    return _repository.authenticate(hostUri: params.hostUri);
  }
}
