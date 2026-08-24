import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase_stream.dart';

/// Observes administrative session invalidation through [IPairingRepository].
class ObserveSessionInvalidationUseCase
    extends UseCaseStream<SessionInvalidatedFailure, NoParams> {
  /// Creates a use case backed by [IPairingRepository].
  ObserveSessionInvalidationUseCase(this._repository);

  /// Repository this use case delegates to.
  final IPairingRepository _repository;

  /// See [UseCaseStream.call].
  @override
  Stream<SessionInvalidatedFailure> call(NoParams params) {
    return _repository.sessionInvalidated;
  }
}
