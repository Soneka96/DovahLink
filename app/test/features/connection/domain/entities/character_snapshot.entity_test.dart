import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/character_snapshot.entity.dart';
import 'package:dovahlink_client/features/connection/domain/entities/character_state.entity.dart';

import '../../fixtures/connection.fixture.dart';

/// Exercises character-snapshot entity value preservation.
void main() {
  group('CharacterSnapshotEntity', () {
    test(
      'keeps session, revision, timestamp, and complete character state',
      () {
        expect(testSnapshot.session.sessionId, 'session-1');
        expect(testSnapshot.revision, 4);
        expect(testSnapshot.occurredAt, DateTime.utc(2026, 8, 11, 12));
        expect(testSnapshot.character.level, 12);
        expect(testSnapshot.character.health?.current, 180);
        expect(testSnapshot.character.health?.maximum, 220);
        expect(testSnapshot.character.magicka?.current, 90);
        expect(testSnapshot.character.stamina?.maximum, 160);
      },
    );

    test('preserves explicitly unavailable character values', () {
      final CharacterSnapshotEntity snapshot = CharacterSnapshotEntity(
        session: testSession,
        revision: 5,
        occurredAt: DateTime.utc(2026, 8, 11, 12, 1),
        character: const CharacterStateEntity(
          level: null,
          health: null,
          magicka: null,
          stamina: null,
        ),
      );

      expect(snapshot.character.level, isNull);
      expect(snapshot.character.health, isNull);
      expect(snapshot.character.magicka, isNull);
      expect(snapshot.character.stamina, isNull);
    });
  });
}
