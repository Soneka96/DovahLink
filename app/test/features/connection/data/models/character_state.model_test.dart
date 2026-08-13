import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/data/models/character_state.model.dart';
import 'package:dovahlink_client/features/connection/domain/entities/character_state.entity.dart';

/// Exercises character-state model decoding, encoding, and validation.
void main() {
  group('CharacterStateModel', () {
    test('parses and serializes level and resource pools', () {
      const Map<String, dynamic> json = {
        'level': 12,
        'health': {'current': 180.0, 'maximum': 220.0},
        'magicka': {'current': 90.0, 'maximum': 120.0},
        'stamina': {'current': 140.0, 'maximum': 160.0},
      };

      final CharacterStateModel state = CharacterStateModel.fromJson(json);

      expect(state, isA<CharacterStateEntity>());
      expect(state.level, 12);
      expect(state.health?.current, 180.0);
      expect(state.health?.maximum, 220.0);
      expect(state.toJson(), json);
    });

    test('preserves unavailable values as null', () {
      const CharacterStateModel state = CharacterStateModel(
        level: null,
        health: null,
        magicka: null,
        stamina: null,
      );

      expect(CharacterStateModel.fromJson(state.toJson()).level, isNull);
      expect(CharacterStateModel.fromJson(state.toJson()).health, isNull);
      expect(CharacterStateModel.fromJson(state.toJson()).magicka, isNull);
      expect(CharacterStateModel.fromJson(state.toJson()).stamina, isNull);
    });

    test('accepts integer JSON numbers for resource values', () {
      final CharacterStateModel state = CharacterStateModel.fromJson({
        'level': 1,
        'health': {'current': 100, 'maximum': 100},
        'magicka': null,
        'stamina': null,
      });

      expect(state.health?.current, 100.0);
      expect(state.health?.maximum, 100.0);
    });

    test('rejects invalid resource values', () {
      expect(
        () => CharacterStateModel.fromJson({
          'level': 1,
          'health': {'current': 'unknown', 'maximum': 100},
          'magicka': null,
          'stamina': null,
        }),
        throwsA(isA<FormatException>()),
      );
      expect(
        () => CharacterStateModel.fromJson({
          'level': 1,
          'health': {'current': double.nan, 'maximum': 100},
          'magicka': null,
          'stamina': null,
        }),
        throwsA(isA<FormatException>()),
      );
      expect(
        () => CharacterStateModel.fromJson({
          'level': 1,
          'health': {'current': double.infinity, 'maximum': 100},
          'magicka': null,
          'stamina': null,
        }),
        throwsA(isA<FormatException>()),
      );
      expect(
        () => CharacterStateModel.fromJson({
          'level': 1,
          'health': null,
          'magicka': null,
          'stamina': <String, dynamic>{},
        }),
        throwsA(isA<FormatException>()),
      );
    });

    test('rejects missing fields instead of treating them as unavailable', () {
      expect(
        () => CharacterStateModel.fromJson({
          'health': null,
          'magicka': null,
          'stamina': null,
        }),
        throwsA(isA<FormatException>()),
      );

      for (final String field in ['health', 'magicka', 'stamina']) {
        final Map<String, dynamic> json = {
          'level': 1,
          'health': null,
          'magicka': null,
          'stamina': null,
        }..remove(field);
        expect(
          () => CharacterStateModel.fromJson(json),
          throwsA(isA<FormatException>()),
        );
      }
    });
  });
}
