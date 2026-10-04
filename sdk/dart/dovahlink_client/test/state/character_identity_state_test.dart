import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_identity_state.dart';

/// Reads one canonical Identity fixture's state-area data object.
/// @param fileName The fixture file name.
/// @return The complete `data` object.
JsonMap _readIdentityFixtureData(String fileName) {
  final String text = File(
    '../../../protocol/fixtures/state/$fileName',
  ).readAsStringSync();
  final JsonMap envelope = jsonDecode(text) as JsonMap;
  final JsonMap payload = envelope['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Tests the Character Identity value decoder and explicit unavailability.
void main() {
  group('Factory fromJson behaves correctly', () {
    test('Factory fromJson decodes both available Identity strings', () {
      final CharacterIdentityState identity = CharacterIdentityState.fromJson(
        <String, dynamic>{'name': 'Gonçalo', 'race': 'Nord'},
      );

      expect(identity.name, 'Gonçalo');
      expect(identity.race, 'Nord');
    });

    test('Factory fromJson preserves Unicode in both Identity strings', () {
      const String race = 'Рос—Nord';
      final CharacterIdentityState identity = CharacterIdentityState.fromJson(
        <String, dynamic>{'name': 'Gonçalo ç á', 'race': race},
      );

      expect(identity.name, 'Gonçalo ç á');
      expect(identity.race, race);
    });

    test('Factory fromJson rejects a missing name', () {
      expect(
        () =>
            CharacterIdentityState.fromJson(<String, dynamic>{'race': 'Nord'}),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Factory fromJson rejects a missing race', () {
      expect(
        () => CharacterIdentityState.fromJson(<String, dynamic>{
          'name': 'Gonçalo',
        }),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Factory fromJson rejects wrong name and race types', () {
      for (final JsonMap malformed in <JsonMap>[
        <String, dynamic>{'name': 7, 'race': 'Nord'},
        <String, dynamic>{'name': 'Gonçalo', 'race': false},
      ]) {
        expect(
          () => CharacterIdentityState.fromJson(malformed),
          throwsA(isA<ProtocolFormatException>()),
        );
      }
    });
  });

  group('Function decodeCharacterIdentityState behaves correctly', () {
    test(
      'Function decodeCharacterIdentityState decodes the available protocol fixture',
      () {
        final CharacterIdentityState? identity = decodeCharacterIdentityState(
          _readIdentityFixtureData('state-snapshot-character-identity.json'),
        );

        expect(identity?.name, 'Gonçalo');
        expect(identity?.race, 'Nord');
      },
    );

    test(
      'Function decodeCharacterIdentityState maps the unavailable fixture to null',
      () {
        final CharacterIdentityState? identity = decodeCharacterIdentityState(
          _readIdentityFixtureData(
            'state-snapshot-character-identity-unavailable.json',
          ),
        );

        expect(identity, isNull);
      },
    );

    test(
      'Function decodeCharacterIdentityState rejects malformed envelopes',
      () {
        for (final JsonMap malformed in <JsonMap>[
          <String, dynamic>{},
          <String, dynamic>{'value': 'Gonçalo'},
          <String, dynamic>{
            'value': <String, dynamic>{'name': 'Gonçalo'},
          },
          <String, dynamic>{
            'value': <String, dynamic>{'race': 'Nord'},
          },
        ]) {
          expect(
            () => decodeCharacterIdentityState(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );
  });
}
