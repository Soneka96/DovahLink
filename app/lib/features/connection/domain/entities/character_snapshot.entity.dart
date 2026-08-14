import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/connection/domain/entities/character_state.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';

/// A revisioned character state accepted for the active session.
class CharacterSnapshotEntity extends Equatable {
  /// Creates a character snapshot with its session and revision metadata.
  const CharacterSnapshotEntity({
    required this.session,
    required this.revision,
    required this.occurredAt,
    required this.character,
  });

  /// The session that produced this snapshot.
  final ConnectionSessionEntity session;

  /// The monotonically increasing revision for the character state area.
  final int revision;

  /// The bridge timestamp associated with the captured value.
  final DateTime occurredAt;

  /// The complete read-only character state.
  final CharacterStateEntity character;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [session, revision, occurredAt, character];
}
