import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';

/// Runs [CharacterLevelState.fromJson] and [decodeCharacterLevel] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes an integer-valued numeric level', () {
      final CharacterLevelState state = CharacterLevelState.fromJson(
        <String, dynamic>{'value': 10.0},
      );

      expect(state.value, 10);
    });

    test('Method fromJson accepts both canonical level bounds', () {
      final CharacterLevelState minimum = CharacterLevelState.fromJson(
        <String, dynamic>{'value': 0},
      );
      final CharacterLevelState maximum = CharacterLevelState.fromJson(
        <String, dynamic>{'value': 65535},
      );

      expect(minimum.value, 0);
      expect(maximum.value, 65535);
    });

    test('Method fromJson preserves an unavailable level', () {
      final CharacterLevelState state = CharacterLevelState.fromJson(
        <String, dynamic>{'value': null},
      );

      expect(state.value, isNull);
    });

    test('Method fromJson rejects a missing level value', () {
      expect(
        () => CharacterLevelState.fromJson(<String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a fractional level', () {
      expect(
        () => CharacterLevelState.fromJson(<String, dynamic>{'value': 10.5}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a level outside the canonical range', () {
      expect(
        () => CharacterLevelState.fromJson(<String, dynamic>{'value': 65536}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a non-numeric level', () {
      expect(
        () => CharacterLevelState.fromJson(<String, dynamic>{'value': '10'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects non-finite numeric levels', () {
      expect(
        () => CharacterLevelState.fromJson(<String, dynamic>{
          'value': double.nan,
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
      expect(
        () => CharacterLevelState.fromJson(<String, dynamic>{
          'value': double.infinity,
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });

  group('Method decodeCharacterLevel behaves correctly', () {
    test('Method decodeCharacterLevel rejects a level below zero', () {
      expect(
        () => decodeCharacterLevel(-1),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method decodeCharacterLevel rejects non-finite levels', () {
      expect(
        () => decodeCharacterLevel(double.nan),
        throwsA(isA<ProtocolFormatException>()),
      );
      expect(
        () => decodeCharacterLevel(double.infinity),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
