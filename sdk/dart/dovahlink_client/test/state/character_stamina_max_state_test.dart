import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_stamina_max_state.dart';

/// Reads the `data` value object from a canonical state fixture.
/// @param fileName The fixture file under `protocol/fixtures/state/`.
/// @return The typed JSON map for the state-area value.
JsonMap _readStaminaMaxFixture(String fileName) {
  final File file = File('../../../protocol/fixtures/state/$fileName');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  final JsonMap payload = fixture['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Runs [CharacterStaminaMaxState.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the canonical maximum Stamina Snapshot', () {
      final CharacterStaminaMaxState state = CharacterStaminaMaxState.fromJson(
        _readStaminaMaxFixture('state-snapshot-character-stamina-max.json'),
      );

      expect(state.value, 320.0);
    });

    test('Method fromJson preserves the canonical unavailable value', () {
      final CharacterStaminaMaxState state = CharacterStaminaMaxState.fromJson(
        _readStaminaMaxFixture(
          'state-snapshot-character-stamina-max-unavailable.json',
        ),
      );

      expect(state.value, isNull);
    });

    test('Method fromJson preserves negative and zero raw values', () {
      expect(
        CharacterStaminaMaxState.fromJson(const <String, dynamic>{
          'value': -5.0,
        }).value,
        -5.0,
      );
      expect(
        CharacterStaminaMaxState.fromJson(const <String, dynamic>{
          'value': 0,
        }).value,
        0.0,
      );
    });

    test('Method fromJson rejects a missing maximum Stamina value', () {
      expect(
        () => CharacterStaminaMaxState.fromJson(const <String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a non-numeric maximum Stamina value', () {
      expect(
        () => CharacterStaminaMaxState.fromJson(const <String, dynamic>{
          'value': '320',
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
