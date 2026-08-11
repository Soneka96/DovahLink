import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/connection/domain/entities/character_snapshot.entity.dart';
import 'package:dovahlink_client/features/connection/domain/repositories/Iconnection.repository.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import 'package:dovahlink_client/shared/usecase/no_params.dart';
import 'package:dovahlink_client/shared/usecase/usecase.dart';

/// Subscribes to character state through [IConnectionRepository].
class SubscribeToCharacterUseCase
    extends UseCase<Either<Failure, CharacterSnapshotEntity>, NoParams> {
  /// Creates a use case backed by [IConnectionRepository].
  SubscribeToCharacterUseCase(this._repository);

  /// Repository this use case delegates to.
  final IConnectionRepository _repository;

  @override
  Future<Either<Failure, CharacterSnapshotEntity>> call(NoParams params) {
    return _repository.subscribeToCharacter();
  }
}
