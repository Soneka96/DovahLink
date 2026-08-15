import 'package:dovahlink_client/features/connection/domain/entities/character_snapshot.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/character_state.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/connection_session.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/resource_value.entity.dart';

/// A reusable negotiated session for connection tests.
const ConnectionSessionEntity testSession = ConnectionSessionEntity(
  sessionId: 'session-1',
);

/// A reusable character snapshot for connection tests.
final CharacterSnapshotEntity testSnapshot = CharacterSnapshotEntity(
  session: testSession,
  revision: 4,
  occurredAt: DateTime.utc(2026, 8, 11, 12),
  character: const CharacterStateEntity(
    level: 12,
    health: ResourceValueEntity(current: 180, maximum: 220),
    magicka: ResourceValueEntity(current: 90, maximum: 120),
    stamina: ResourceValueEntity(current: 140, maximum: 160),
  ),
);
