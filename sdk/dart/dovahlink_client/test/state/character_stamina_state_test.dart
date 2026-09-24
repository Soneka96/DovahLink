import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_stamina_state.dart';

/// Runs [CharacterStaminaState.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the current stamina value', () {
      final CharacterStaminaState state = CharacterStaminaState.fromJson(
        <String, dynamic>{'value': 32.25},
      );

      expect(state.value, 32.25);
    });

    test('Method fromJson converts an integer stamina value to double', () {
      final CharacterStaminaState state = CharacterStaminaState.fromJson(
        <String, dynamic>{'value': 32},
      );

      expect(state.value, 32.0);
    });

    test('Method fromJson preserves an unavailable stamina value', () {
      final CharacterStaminaState state = CharacterStaminaState.fromJson(
        <String, dynamic>{'value': null},
      );

      expect(state.value, isNull);
    });

    test('Method fromJson rejects a missing stamina value', () {
      expect(
        () => CharacterStaminaState.fromJson(<String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a non-numeric stamina value', () {
      expect(
        () =>
            CharacterStaminaState.fromJson(<String, dynamic>{'value': '32.25'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
