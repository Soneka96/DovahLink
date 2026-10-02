import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_renotify_result.entity.dart';
import 'package:dovahlink_client/features/pairing/domain/repositories/pairing_repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Requests redisplay of the active pairing code through
/// [IPairingRepository], preserving the Host's typed response.
class RequestPairingRenotifyUseCase
    extends UseCase<Either<Failure, PairingRenotifyResult>, NoParams> {
  /// Creates a use case backed by [IPairingRepository].
  RequestPairingRenotifyUseCase(this._repository);

  /// Repository this use case delegates to.
  final IPairingRepository _repository;

  /// See [UseCase.call].
  @override
  Future<Either<Failure, PairingRenotifyResult>> call(NoParams params) {
    return _repository.requestPairingRenotify();
  }
}
