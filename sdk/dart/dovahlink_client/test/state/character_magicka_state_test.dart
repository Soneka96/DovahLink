import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_magicka_state.dart';

/// Runs [CharacterMagickaState.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the current magicka value', () {
      final CharacterMagickaState state = CharacterMagickaState.fromJson(
        <String, dynamic>{'value': 45},
      );

      expect(state.value, 45.0);
    });

    test('Method fromJson preserves an unavailable magicka value', () {
      final CharacterMagickaState state = CharacterMagickaState.fromJson(
        <String, dynamic>{'value': null},
      );

      expect(state.value, isNull);
    });

    test('Method fromJson rejects a missing magicka value', () {
      expect(
        () => CharacterMagickaState.fromJson(<String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a non-numeric magicka value', () {
      expect(
        () => CharacterMagickaState.fromJson(<String, dynamic>{'value': '45'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
