import 'dart:convert';

import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'game_time_state.g.dart';

/// A normalized Skyrim calendar value, not a Gregorian date or clock.
@JsonSerializable(checked: true, createToJson: false)
class GameTimeState {
  /// The nonnegative 32-bit game-year value supplied by Skyrim.
  @JsonKey(required: true)
  final int year;

  /// The one-based Skyrim month number, from 1 through 12.
  @JsonKey(required: true)
  final int month;

  /// The localized Skyrim month name supplied by the running game.
  @JsonKey(required: true)
  final String monthName;

  /// The Skyrim calendar day.
  @JsonKey(required: true)
  final int day;

  /// The whole game hour, from 0 through 23.
  @JsonKey(required: true)
  final int hour;

  /// The minute derived from the fractional game hour, from 0 through 59.
  @JsonKey(required: true)
  final int minute;

  /// Creates one complete available Skyrim calendar value.
  /// @param year The game-year value from Skyrim.
  /// @param month The one-based Skyrim month number.
  /// @param monthName The localized month name from the running game.
  /// @param day The Skyrim calendar day.
  /// @param hour The whole game hour.
  /// @param minute The minute derived from the fractional game hour.
  const GameTimeState({
    required this.year,
    required this.month,
    required this.monthName,
    required this.day,
    required this.hour,
    required this.minute,
  });

  /// Decodes one complete available Skyrim calendar value.
  /// @param json The complete object inside the protocol `value` field.
  /// @return The typed game time.
  /// @throws [ProtocolFormatException] if a required field is missing or outside its domain bounds.
  factory GameTimeState.fromJson(JsonMap json) {
    try {
      for (final String field in <String>[
        'year',
        'month',
        'day',
        'hour',
        'minute',
      ]) {
        final Object? value = json[field];
        if (value is! num ||
            !value.isFinite ||
            value != value.roundToDouble()) {
          throw const FormatException(
            'Skyrim calendar fields must be finite integers.',
          );
        }
      }
      final GameTimeState state = _$GameTimeStateFromJson(json);
      if (state.year < 0 ||
          state.year > 0x7FFFFFFF ||
          state.month < 1 ||
          state.month > 12 ||
          state.day < 1 ||
          state.day > 31 ||
          state.hour < 0 ||
          state.hour > 23 ||
          state.minute < 0 ||
          state.minute > 59 ||
          state.monthName.isEmpty ||
          state.monthName.contains('\u0000') ||
          utf8.encode(state.monthName).length > 126) {
        throw const FormatException(
          'Skyrim calendar facts are outside their bounds.',
        );
      }
      return state;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid game_time state: $error');
    }
  }
}

/// Decodes an available or unavailable `game_time` state-area value.
/// @param json The protocol state-area `data` object.
/// @return A typed calendar value, or `null` for explicit unavailability.
/// @throws [ProtocolFormatException] if `value` is missing or malformed.
GameTimeState? decodeGameTimeState(JsonMap json) {
  if (!json.containsKey('value')) {
    throw const ProtocolFormatException(
      'Game Time state is missing its value field.',
    );
  }
  final Object? value = json['value'];
  if (value == null) {
    return null;
  }
  if (value is! Map<String, Object?>) {
    throw const ProtocolFormatException(
      'Game Time value must be an object or null.',
    );
  }
  return GameTimeState.fromJson(value);
}
