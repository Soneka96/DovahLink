import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/connection/domain/entities/character_snapshot.entity.dart';
import 'package:dovahlink_client/features/connection/domain/repositories/Iconnection.repository.dart';
import 'package:dovahlink_client/features/connection/domain/usecases/params/request_character_snapshot.params.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Requests a fresh character baseline through [IConnectionRepository].
class RequestCharacterSnapshotUseCase
    extends
        UseCase<
          Either<Failure, CharacterSnapshotEntity>,
          RequestCharacterSnapshotParams
        > {
  /// Creates a use case backed by [IConnectionRepository].
  RequestCharacterSnapshotUseCase(this._repository);

  /// Repository this use case delegates to.
  final IConnectionRepository _repository;

  @override
  Future<Either<Failure, CharacterSnapshotEntity>> call(
    RequestCharacterSnapshotParams params,
  ) {
    return _repository.requestCharacterSnapshot(
      knownRevision: params.knownRevision,
    );
  }
}
