import 'package:dovahlink_client/features/connection/domain/entities/character_snapshot.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/character_state.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/resource_value.entity.dart';

/// Builds a reusable negotiated session for connection tests. Defaults to a
/// minimal session with no bridge/play-context identity; override individual
/// fields per test case.
ConnectionSessionEntity buildConnectionSessionEntity({
  String sessionId = 'session-1',
  String? bridgeInstanceId,
  String? playContextId,
}) => ConnectionSessionEntity(
  sessionId: sessionId,
  bridgeInstanceId: bridgeInstanceId,
  playContextId: playContextId,
);

/// Builds a reusable character snapshot for connection tests. Defaults to a
/// complete, fully-available character state on [buildConnectionSessionEntity]'s
/// default session; override individual fields per test case.
CharacterSnapshotEntity buildCharacterSnapshotEntity({
  ConnectionSessionEntity? session,
  int revision = 4,
  DateTime? occurredAt,
  CharacterStateEntity? character,
}) => CharacterSnapshotEntity(
  session: session ?? buildConnectionSessionEntity(),
  revision: revision,
  occurredAt: occurredAt ?? DateTime.utc(2026, 8, 11, 12),
  character:
      character ??
      const CharacterStateEntity(
        level: 12,
        health: ResourceValueEntity(current: 180, maximum: 220),
        magicka: ResourceValueEntity(current: 90, maximum: 120),
        stamina: ResourceValueEntity(current: 140, maximum: 160),
      ),
);
