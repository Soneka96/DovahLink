import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_health_max_state.dart';

/// Reads the `data` value object from a canonical state fixture.
/// @param fileName The fixture file under `protocol/fixtures/state/`.
/// @return The typed JSON map for the state-area value.
JsonMap _readHealthMaxFixture(String fileName) {
  final File file = File('../../../protocol/fixtures/state/$fileName');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  final JsonMap payload = fixture['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Runs [CharacterHealthMaxState.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the canonical maximum Health Snapshot', () {
      final CharacterHealthMaxState state = CharacterHealthMaxState.fromJson(
        _readHealthMaxFixture('state-snapshot-character-health-max.json'),
      );

      expect(state.value, 410.0);
    });

    test('Method fromJson preserves the canonical unavailable value', () {
      final CharacterHealthMaxState state = CharacterHealthMaxState.fromJson(
        _readHealthMaxFixture(
          'state-snapshot-character-health-max-unavailable.json',
        ),
      );

      expect(state.value, isNull);
    });

    test('Method fromJson preserves negative and zero raw values', () {
      expect(
        CharacterHealthMaxState.fromJson(const <String, dynamic>{
          'value': -12.0,
        }).value,
        -12.0,
      );
      expect(
        CharacterHealthMaxState.fromJson(const <String, dynamic>{
          'value': 0,
        }).value,
        0.0,
      );
    });

    test('Method fromJson rejects a missing maximum Health value', () {
      expect(
        () => CharacterHealthMaxState.fromJson(const <String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a non-numeric maximum Health value', () {
      expect(
        () => CharacterHealthMaxState.fromJson(const <String, dynamic>{
          'value': '410',
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
