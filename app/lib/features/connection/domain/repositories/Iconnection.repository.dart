import 'package:fpdart/fpdart.dart';

import 'package:dovahlink_client/features/connection/domain/entities/character_snapshot.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';

/// Domain boundary for negotiating and observing one bridge connection.
abstract interface class IConnectionRepository {
  /// Authenticates a new session with [token], establishing this
  /// installation's logical [clientId].
  Future<Either<Failure, ConnectionSessionEntity>> connect({
    required String token,
    required String clientId,
  });

  /// Subscribes to character state and returns its initial snapshot.
  Future<Either<Failure, CharacterSnapshotEntity>> subscribeToCharacter();

  /// Requests a fresh character snapshot after recovery or reconnect.
  Future<Either<Failure, CharacterSnapshotEntity>> requestCharacterSnapshot({
    int? knownRevision,
  });

  /// Emits accepted character snapshots and subsequent complete state events.
  Stream<CharacterSnapshotEntity> get characterSnapshots;

  /// Closes the current connection without changing game state.
  Future<Either<Failure, Unit>> disconnect();
}
