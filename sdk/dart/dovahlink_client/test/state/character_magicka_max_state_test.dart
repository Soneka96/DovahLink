import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_magicka_max_state.dart';

/// Reads the `data` value object from a canonical state fixture.
/// @param fileName The fixture file under `protocol/fixtures/state/`.
/// @return The typed JSON map for the state-area value.
JsonMap _readMagickaMaxFixture(String fileName) {
  final File file = File('../../../protocol/fixtures/state/$fileName');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  final JsonMap payload = fixture['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Runs [CharacterMagickaMaxState.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the canonical maximum Magicka Snapshot', () {
      final CharacterMagickaMaxState state = CharacterMagickaMaxState.fromJson(
        _readMagickaMaxFixture('state-snapshot-character-magicka-max.json'),
      );

      expect(state.value, 210.0);
    });

    test('Method fromJson preserves the canonical unavailable value', () {
      final CharacterMagickaMaxState state = CharacterMagickaMaxState.fromJson(
        _readMagickaMaxFixture(
          'state-snapshot-character-magicka-max-unavailable.json',
        ),
      );

      expect(state.value, isNull);
    });

    test('Method fromJson preserves negative and zero raw values', () {
      expect(
        CharacterMagickaMaxState.fromJson(const <String, dynamic>{
          'value': -5.0,
        }).value,
        -5.0,
      );
      expect(
        CharacterMagickaMaxState.fromJson(const <String, dynamic>{
          'value': 0,
        }).value,
        0.0,
      );
    });

    test('Method fromJson rejects a missing maximum Magicka value', () {
      expect(
        () => CharacterMagickaMaxState.fromJson(const <String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a non-numeric maximum Magicka value', () {
      expect(
        () => CharacterMagickaMaxState.fromJson(const <String, dynamic>{
          'value': '210',
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
