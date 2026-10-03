import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_vital.dart';
import 'package:dovahlink_client_sdk/src/state/character_vitals_state.dart';

/// Loads the state data from one canonical Vitals fixture.
/// @param fileName The Vitals fixture filename.
/// @return The Snapshot's typed state data object.
JsonMap _readVitalsFixtureData(String fileName) {
  final String text = File(
    '../../../protocol/fixtures/state/$fileName',
  ).readAsStringSync();
  final JsonMap envelope = jsonDecode(text) as JsonMap;
  final JsonMap payload = envelope['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Tests [CharacterVitalsState.fromJson] availability and malformed-data handling.
void main() {
  group('Behavior resource availability behaves correctly', () {
    test(
      'Behavior resource availability rejects partial direct construction',
      () {
        expect(
          () => CharacterVitalsState(
            health: const CharacterVital(current: 1.0, max: 2.0),
            magicka: null,
            stamina: const CharacterVital(current: 1.0, max: 2.0),
          ),
          throwsArgumentError,
        );
      },
    );
  });

  group('Factory fromJson behaves correctly', () {
    test('Factory fromJson decodes the available canonical Vitals fixture', () {
      final CharacterVitalsState state = CharacterVitalsState.fromJson(
        _readVitalsFixtureData('state-snapshot-character-vitals.json'),
      );

      expect(state.health?.current, 327.0);
      expect(state.health?.max, 410.0);
      expect(state.magicka?.current, 180.0);
      expect(state.magicka?.max, 250.0);
      expect(state.stamina?.current, 120.0);
      expect(state.stamina?.max, 190.0);
    });

    test('Factory fromJson decodes whole-domain unavailability', () {
      final CharacterVitalsState state = CharacterVitalsState.fromJson(
        _readVitalsFixtureData(
          'state-snapshot-character-vitals-unavailable.json',
        ),
      );

      expect(state.isUnavailable, isTrue);
      expect(state.health, isNull);
      expect(state.magicka, isNull);
      expect(state.stamina, isNull);
    });

    test(
      'Factory fromJson rejects missing, nonobject, and partial Vitals data',
      () {
        for (final JsonMap malformed in <JsonMap>[
          const <String, dynamic>{},
          const <String, dynamic>{'value': 'not an object'},
          const <String, dynamic>{
            'value': <String, dynamic>{
              'health': <String, dynamic>{'current': 327.0, 'max': 410.0},
              'magicka': <String, dynamic>{'current': 180.0, 'max': 250.0},
            },
          },
          const <String, dynamic>{
            'value': <String, dynamic>{
              'health': null,
              'magicka': <String, dynamic>{'current': 180.0, 'max': 250.0},
              'stamina': <String, dynamic>{'current': 120.0, 'max': 190.0},
            },
          },
        ]) {
          expect(
            () => CharacterVitalsState.fromJson(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );
  });
}
