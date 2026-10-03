import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_vital.dart';

/// Tests [CharacterVital.fromJson] decoding of one typed resource.
void main() {
  group('Factory fromJson behaves correctly', () {
    test('Factory fromJson decodes current and maximum values', () {
      final CharacterVital vital = CharacterVital.fromJson(
        const <String, dynamic>{'current': 327.0, 'max': 410.0},
      );

      expect(vital.current, 327.0);
      expect(vital.max, 410.0);
    });

    test(
      'Factory fromJson preserves finite values outside presentation bounds',
      () {
        final CharacterVital vital = CharacterVital.fromJson(
          const <String, dynamic>{'current': -1.0, 'max': 0.0},
        );

        expect(vital.current, -1.0);
        expect(vital.max, 0.0);
      },
    );

    test('Factory fromJson rejects a missing or nonnumeric field', () {
      for (final JsonMap malformed in <JsonMap>[
        const <String, dynamic>{'max': 410.0},
        const <String, dynamic>{'current': 327.0},
        const <String, dynamic>{'current': '327', 'max': 410.0},
      ]) {
        expect(
          () => CharacterVital.fromJson(malformed),
          throwsA(isA<ProtocolFormatException>()),
        );
      }
    });
  });
}
