import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_xp_state.dart';

/// Reads the `data` object from a canonical state fixture.
/// @param fileName The fixture file name under `state/`.
/// @return The canonical state-area value object.
JsonMap _readStateValueFixture(String fileName) {
  final File file = File('../../../protocol/fixtures/state/$fileName');
  final JsonMap fixture = jsonDecode(file.readAsStringSync()) as JsonMap;
  final JsonMap payload = fixture['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Runs [CharacterXpState.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the current experience value', () {
      final CharacterXpState state = CharacterXpState.fromJson(
        <String, dynamic>{'value': 1280.0},
      );

      expect(state.value, 1280.0);
    });

    test(
      'Method fromJson decodes canonical available and unavailable state',
      () {
        final CharacterXpState available = CharacterXpState.fromJson(
          _readStateValueFixture('state-snapshot.json'),
        );
        final CharacterXpState unavailable = CharacterXpState.fromJson(
          _readStateValueFixture('state-snapshot-unavailable.json'),
        );

        expect(available.value, 12.0);
        expect(unavailable.value, isNull);
      },
    );

    test('Method fromJson converts an integer experience value to double', () {
      final CharacterXpState state = CharacterXpState.fromJson(
        <String, dynamic>{'value': 1280},
      );

      expect(state.value, 1280.0);
    });

    test('Method fromJson preserves an unavailable experience value', () {
      final CharacterXpState state = CharacterXpState.fromJson(
        <String, dynamic>{'value': null},
      );

      expect(state.value, isNull);
    });

    test('Method fromJson rejects a missing experience value', () {
      expect(
        () => CharacterXpState.fromJson(<String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a non-numeric experience value', () {
      expect(
        () => CharacterXpState.fromJson(<String, dynamic>{'value': '1280'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
