import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/domain/repositories/Iconnection.repository.dart';
import 'package:dovahlink_client/features/connection/domain/usecases/params/connect.params.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Negotiates one authenticated bridge session through [IConnectionRepository].
class ConnectUseCase
    extends UseCase<Either<Failure, ConnectionSessionEntity>, ConnectParams> {
  /// Creates a use case backed by [IConnectionRepository].
  ConnectUseCase(this._repository);

  /// Repository this use case delegates to.
  final IConnectionRepository _repository;

  /// See [UseCase.call].
  @override
  Future<Either<Failure, ConnectionSessionEntity>> call(ConnectParams params) {
    return _repository.connect(
      token: params.token,
      clientId: params.clientId,
      supportedProtocolVersions: params.supportedProtocolVersions,
    );
  }
}
