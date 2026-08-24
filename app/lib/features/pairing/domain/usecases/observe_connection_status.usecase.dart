import 'package:dovahlink_client/features/pairing/domain/repositories/Ipairing.repository.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase_stream.dart';

/// Observes the bridge connection's status while a session is active, per
/// [IPairingRepository.connectionStatus].
class ObserveConnectionStatusUseCase
    extends UseCaseStream<PairingConnectionStatus, NoParams> {
  /// Creates a use case backed by [IPairingRepository].
  ObserveConnectionStatusUseCase(this._repository);

  /// Repository this use case delegates to.
  final IPairingRepository _repository;

  /// See [UseCaseStream.call].
  @override
  Stream<PairingConnectionStatus> call(NoParams params) {
    return _repository.connectionStatus;
  }
}
