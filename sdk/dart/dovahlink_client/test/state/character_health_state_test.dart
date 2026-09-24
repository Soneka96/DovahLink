import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_health_state.dart';

/// Runs [CharacterHealthState.fromJson] behavior tests.
void main() {
  group('Method fromJson behaves correctly', () {
    test('Method fromJson decodes the current health value', () {
      final CharacterHealthState state = CharacterHealthState.fromJson(
        <String, dynamic>{'value': 87.5},
      );

      expect(state.value, 87.5);
    });

    test('Method fromJson converts an integer health value to double', () {
      final CharacterHealthState state = CharacterHealthState.fromJson(
        <String, dynamic>{'value': 87},
      );

      expect(state.value, 87.0);
    });

    test('Method fromJson preserves an unavailable health value', () {
      final CharacterHealthState state = CharacterHealthState.fromJson(
        <String, dynamic>{'value': null},
      );

      expect(state.value, isNull);
    });

    test('Method fromJson rejects a missing health value', () {
      expect(
        () => CharacterHealthState.fromJson(<String, dynamic>{}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Method fromJson rejects a non-numeric health value', () {
      expect(
        () => CharacterHealthState.fromJson(<String, dynamic>{'value': '87.5'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });
  });
}
