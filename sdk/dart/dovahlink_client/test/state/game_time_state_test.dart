import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/game_time_state.dart';

/// Reads one canonical game-time fixture's state-area data object.
/// @param fileName The fixture file name.
/// @return The complete state-area data object.
JsonMap _readGameTimeFixtureData(String fileName) {
  final String text = File(
    '../../../protocol/fixtures/state/$fileName',
  ).readAsStringSync();
  final JsonMap envelope = jsonDecode(text) as JsonMap;
  final JsonMap payload = envelope['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Builds one representative Skyrim calendar value.
/// @param year The game year.
/// @param month The public one-based month.
/// @param monthName The localized month name.
/// @param day The Skyrim day.
/// @param hour The whole game hour.
/// @param minute The derived minute.
/// @return A fresh calendar state map.
JsonMap _buildGameTimeJson({
  Object? year = 201,
  Object? month = 9,
  Object? monthName = 'Hearthfire',
  Object? day = 17,
  Object? hour = 17,
  Object? minute = 45,
}) => <String, dynamic>{
  'year': year,
  'month': month,
  'monthName': monthName,
  'day': day,
  'hour': hour,
  'minute': minute,
};

/// Tests the typed Skyrim calendar decoder and explicit unavailability.
void main() {
  group('Factory fromJson behaves correctly', () {
    test('Factory fromJson decodes normalized Skyrim calendar fields', () {
      final GameTimeState state = GameTimeState.fromJson(_buildGameTimeJson());

      expect(state.year, 201);
      expect(state.month, 9);
      expect(state.monthName, 'Hearthfire');
      expect(state.day, 17);
      expect(state.hour, 17);
      expect(state.minute, 45);
    });

    test('Factory fromJson preserves localized month names', () {
      final GameTimeState state = GameTimeState.fromJson(
        _buildGameTimeJson(monthName: 'Sønens Dag'),
      );

      expect(state.monthName, 'Sønens Dag');
    });

    test('Factory fromJson accepts the exact UTF-8 month-name byte bound', () {
      final String monthName = List<String>.filled(126, 'x').join();

      expect(
        GameTimeState.fromJson(
          _buildGameTimeJson(monthName: monthName),
        ).monthName,
        monthName,
      );
    });

    test(
      'Factory fromJson rejects missing fields and fractional calendar numbers',
      () {
        final JsonMap complete = _buildGameTimeJson();
        final JsonMap missingMonth = Map<String, dynamic>.of(complete)
          ..remove('month');

        for (final JsonMap malformed in <JsonMap>[
          missingMonth,
          _buildGameTimeJson(hour: 17.5),
          _buildGameTimeJson(minute: '45'),
        ]) {
          expect(
            () => GameTimeState.fromJson(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );

    test(
      'Factory fromJson rejects values outside calendar and UTF-8 bounds',
      () {
        final JsonMap oversizedUtf8Name = _buildGameTimeJson(
          monthName: List<String>.filled(64, 'é').join(),
        );
        for (final JsonMap malformed in <JsonMap>[
          _buildGameTimeJson(year: -1),
          _buildGameTimeJson(year: 0x80000000),
          _buildGameTimeJson(month: 0),
          _buildGameTimeJson(month: 13),
          _buildGameTimeJson(day: 0),
          _buildGameTimeJson(day: 32),
          _buildGameTimeJson(hour: -1),
          _buildGameTimeJson(hour: 24),
          _buildGameTimeJson(minute: -1),
          _buildGameTimeJson(minute: 60),
          _buildGameTimeJson(monthName: ''),
          _buildGameTimeJson(monthName: List<String>.filled(127, 'x').join()),
          oversizedUtf8Name,
          _buildGameTimeJson(monthName: 'Sun\u0000Dusk'),
        ]) {
          expect(
            () => GameTimeState.fromJson(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );
  });

  group('Function decodeGameTimeState behaves correctly', () {
    test(
      'Function decodeGameTimeState decodes the available shared fixture',
      () {
        final GameTimeState? gameTime = decodeGameTimeState(
          _readGameTimeFixtureData('state-snapshot-game-time.json'),
        );

        expect(gameTime?.year, 201);
        expect(gameTime?.month, 9);
        expect(gameTime?.monthName, 'Hearthfire');
        expect(gameTime?.day, 17);
        expect(gameTime?.hour, 17);
        expect(gameTime?.minute, 45);
      },
    );

    test(
      'Function decodeGameTimeState maps the unavailable fixture to null',
      () {
        expect(
          decodeGameTimeState(
            _readGameTimeFixtureData(
              'state-snapshot-game-time-unavailable.json',
            ),
          ),
          isNull,
        );
      },
    );

    test('Function decodeGameTimeState rejects malformed state envelopes', () {
      for (final JsonMap malformed in <JsonMap>[
        <String, dynamic>{},
        <String, dynamic>{'value': 'Hearthfire'},
        <String, dynamic>{
          'value': <String, dynamic>{'month': 9},
        },
      ]) {
        expect(
          () => decodeGameTimeState(malformed),
          throwsA(isA<ProtocolFormatException>()),
        );
      }
    });
  });
}
