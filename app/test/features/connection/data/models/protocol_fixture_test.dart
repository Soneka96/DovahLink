import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/data/models/character_state.model.dart';
import 'package:dovahlink_client/features/connection/data/models/json_map.dart';
import 'package:dovahlink_client/features/connection/data/models/protocol_envelope.model.dart';

/// Decodes canonical protocol fixtures through the client data models.
void main() {
  final Directory fixtureDirectory = Directory('../protocol/fixtures');

  group('canonical protocol fixture discovery', () {
    test('recursively decodes every shared JSON fixture', () {
      final List<File> fixtureFiles = _discoverFixtureFiles(fixtureDirectory);
      expect(fixtureFiles, isNotEmpty);

      for (final File fixture in fixtureFiles) {
        final String fixturePath = _relativeFixturePath(
          fixtureDirectory,
          fixture,
        );
        final JsonMap json = _readFixture(fixture);

        expect(
          () => ProtocolEnvelopeModel.fromJson(json),
          returnsNormally,
          reason: fixturePath,
        );
      }
    });
  });

  const List<String> characterFixturePaths = [
    'state/character/character-state-snapshot.json',
    'state/character/character-state-unavailable.json',
    'state/character/character-state-event.json',
  ];

  group('canonical character state fixtures', () {
    for (final String fixturePath in characterFixturePaths) {
      test('shared fixture $fixturePath decodes through the client models', () {
        final JsonMap json = _readFixture(
          File('${fixtureDirectory.path}/$fixturePath'),
        );
        final ProtocolEnvelopeModel envelope = ProtocolEnvelopeModel.fromJson(
          json,
        );
        final JsonMap payload = envelope.payload;
        final CharacterStateModel state = CharacterStateModel.fromJson(
          payload['data'] as JsonMap,
        );

        expect(
          envelope.messageType,
          fixturePath.endsWith('event.json') ? 'state_event' : 'state_snapshot',
        );
        expect(envelope.messageId, isNotEmpty);
        expect(envelope.sessionId, 'session-1');
        expect(payload['stateArea'], 'character');
        expect(payload['revision'], isA<int>());
        expect(payload['occurredAt'], isA<String>());
        expect(state.toJson(), payload['data']);
        expect(envelope.toJson(), json);

        if (fixturePath.endsWith('event.json')) {
          expect(payload['baseRevision'], 1);
          expect(payload['revision'], 2);
          expect(payload['revision'], (payload['baseRevision'] as int) + 1);
        }
        if (fixturePath.endsWith('unavailable.json')) {
          expect(state.level, isNull);
          expect(state.health, isNull);
          expect(state.magicka, isNull);
          expect(state.stamina, isNull);
        }
      });
    }
  });

}

/// Finds every JSON fixture below [fixtureDirectory] in deterministic order.
List<File> _discoverFixtureFiles(Directory fixtureDirectory) {
  final List<File> fixtureFiles =
      fixtureDirectory
          .listSync(recursive: true, followLinks: false)
          .whereType<File>()
          .where((File fixture) => fixture.path.endsWith('.json'))
          .toList()
        ..sort((File left, File right) => left.path.compareTo(right.path));
  return fixtureFiles;
}

/// Reads one canonical fixture as a JSON object.
JsonMap _readFixture(File fixture) =>
    jsonDecode(fixture.readAsStringSync()) as JsonMap;

/// Returns [fixture]'s slash-separated path below [fixtureDirectory].
String _relativeFixturePath(Directory fixtureDirectory, File fixture) {
  final String prefix = '${fixtureDirectory.path}${Platform.pathSeparator}';
  return fixture.path.substring(prefix.length).replaceAll('\\', '/');
}
