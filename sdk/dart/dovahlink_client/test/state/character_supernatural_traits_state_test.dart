import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_supernatural_traits_state.dart';

/// Reads one canonical supernatural-traits fixture's state-area data object.
/// @param fileName The fixture file name.
/// @return The complete `data` object.
JsonMap _readSupernaturalTraitsFixtureData(String fileName) {
  final String text = File(
    '../../../protocol/fixtures/state/$fileName',
  ).readAsStringSync();
  final JsonMap envelope = jsonDecode(text) as JsonMap;
  final JsonMap payload = envelope['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Tests supernatural-traits decoding and independent field preservation.
void main() {
  group('Factory fromJson behaves correctly', () {
    test('Factory fromJson accepts all-false as an available value', () {
      final CharacterSupernaturalTraitsState traits =
          CharacterSupernaturalTraitsState.fromJson(<String, dynamic>{
            'isVampire': false,
            'hasVampireLordForm': false,
            'hasWerewolfForm': false,
          });

      expect(traits.isVampire, isFalse);
      expect(traits.hasVampireLordForm, isFalse);
      expect(traits.hasWerewolfForm, isFalse);
    });

    test('Factory fromJson preserves vampire status independently', () {
      final CharacterSupernaturalTraitsState traits =
          CharacterSupernaturalTraitsState.fromJson(<String, dynamic>{
            'isVampire': true,
            'hasVampireLordForm': false,
            'hasWerewolfForm': false,
          });

      expect(traits.isVampire, isTrue);
      expect(traits.hasVampireLordForm, isFalse);
      expect(traits.hasWerewolfForm, isFalse);
    });

    test(
      'Factory fromJson preserves Vampire Lord capability independently',
      () {
        final CharacterSupernaturalTraitsState traits =
            CharacterSupernaturalTraitsState.fromJson(<String, dynamic>{
              'isVampire': false,
              'hasVampireLordForm': true,
              'hasWerewolfForm': false,
            });

        expect(traits.isVampire, isFalse);
        expect(traits.hasVampireLordForm, isTrue);
        expect(traits.hasWerewolfForm, isFalse);
      },
    );

    test('Factory fromJson preserves Werewolf capability independently', () {
      final CharacterSupernaturalTraitsState traits =
          CharacterSupernaturalTraitsState.fromJson(<String, dynamic>{
            'isVampire': false,
            'hasVampireLordForm': false,
            'hasWerewolfForm': true,
          });

      expect(traits.isVampire, isFalse);
      expect(traits.hasVampireLordForm, isFalse);
      expect(traits.hasWerewolfForm, isTrue);
    });

    test(
      'Factory fromJson accepts all three independent predicates as true',
      () {
        final CharacterSupernaturalTraitsState traits =
            CharacterSupernaturalTraitsState.fromJson(<String, dynamic>{
              'isVampire': true,
              'hasVampireLordForm': true,
              'hasWerewolfForm': true,
            });

        expect(traits.isVampire, isTrue);
        expect(traits.hasVampireLordForm, isTrue);
        expect(traits.hasWerewolfForm, isTrue);
      },
    );

    test(
      'Factory fromJson rejects each missing field and wrong field types',
      () {
        for (final JsonMap malformed in <JsonMap>[
          <String, dynamic>{
            'hasVampireLordForm': false,
            'hasWerewolfForm': false,
          },
          <String, dynamic>{'isVampire': false, 'hasWerewolfForm': false},
          <String, dynamic>{'isVampire': false, 'hasVampireLordForm': false},
          <String, dynamic>{
            'isVampire': 'false',
            'hasVampireLordForm': false,
            'hasWerewolfForm': false,
          },
          <String, dynamic>{
            'isVampire': false,
            'hasVampireLordForm': 'false',
            'hasWerewolfForm': false,
          },
          <String, dynamic>{
            'isVampire': false,
            'hasVampireLordForm': false,
            'hasWerewolfForm': 1,
          },
        ]) {
          expect(
            () => CharacterSupernaturalTraitsState.fromJson(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );
  });

  group('Function decodeCharacterSupernaturalTraitsState behaves correctly', () {
    test(
      'Function decodeCharacterSupernaturalTraitsState decodes available all-false fixture',
      () {
        final CharacterSupernaturalTraitsState? traits =
            decodeCharacterSupernaturalTraitsState(
              _readSupernaturalTraitsFixtureData(
                'state-snapshot-character-supernatural-traits.json',
              ),
            );

        expect(traits, isNotNull);
        expect(traits!.isVampire, isFalse);
        expect(traits.hasVampireLordForm, isFalse);
        expect(traits.hasWerewolfForm, isFalse);
      },
    );

    test(
      'Function decodeCharacterSupernaturalTraitsState distinguishes unavailable from all-false',
      () {
        final CharacterSupernaturalTraitsState? traits =
            decodeCharacterSupernaturalTraitsState(
              _readSupernaturalTraitsFixtureData(
                'state-snapshot-character-supernatural-traits-unavailable.json',
              ),
            );

        expect(traits, isNull);
      },
    );

    test(
      'Function decodeCharacterSupernaturalTraitsState rejects malformed envelopes',
      () {
        for (final JsonMap malformed in <JsonMap>[
          <String, dynamic>{},
          <String, dynamic>{'value': 'none'},
        ]) {
          expect(
            () => decodeCharacterSupernaturalTraitsState(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );
  });
}
