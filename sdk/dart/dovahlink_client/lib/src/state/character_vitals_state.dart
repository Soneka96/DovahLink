import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/character_vital.dart';

part 'character_vitals_state.g.dart';

/// The complete `character_vitals` Snapshot value, or explicit unavailability.
@JsonSerializable(checked: true, createToJson: false)
class CharacterVitalsState {
  /// Health values, or `null` when the complete Vitals capture is unavailable.
  @JsonKey(required: true)
  final CharacterVital? health;

  /// Magicka values, or `null` when the complete Vitals capture is unavailable.
  @JsonKey(required: true)
  final CharacterVital? magicka;

  /// Stamina values, or `null` when the complete Vitals capture is unavailable.
  @JsonKey(required: true)
  final CharacterVital? stamina;

  /// Creates a complete or unavailable Vitals state.
  /// @param health Health values, or `null` for an unavailable Vitals capture.
  /// @param magicka Magicka values, or `null` for an unavailable Vitals capture.
  /// @param stamina Stamina values, or `null` for an unavailable Vitals capture.
  /// @throws [ArgumentError] if only some resources are unavailable.
  factory CharacterVitalsState({
    required CharacterVital? health,
    required CharacterVital? magicka,
    required CharacterVital? stamina,
  }) {
    final bool allUnavailable =
        health == null && magicka == null && stamina == null;
    final bool allAvailable =
        health != null && magicka != null && stamina != null;
    if (!allUnavailable && !allAvailable) {
      throw ArgumentError('Vitals resources must be available together.');
    }
    return CharacterVitalsState._(
      health: health,
      magicka: magicka,
      stamina: stamina,
    );
  }

  /// Creates a validated Vitals value after the public constructor checks availability.
  const CharacterVitalsState._({
    required this.health,
    required this.magicka,
    required this.stamina,
  });

  /// Whether all three resources are unavailable as one coherent capture.
  bool get isUnavailable =>
      health == null && magicka == null && stamina == null;

  /// Decodes one `character_vitals` state-area value.
  /// @param json The state-area value object.
  /// @return The typed Vitals state.
  /// @throws [ProtocolFormatException] if the value is missing or malformed.
  factory CharacterVitalsState.fromJson(JsonMap json) {
    try {
      if (!json.containsKey('value')) {
        throw const ProtocolFormatException(
          'Character Vitals state is missing its value field.',
        );
      }
      final Object? value = json['value'];
      if (value == null) {
        return CharacterVitalsState(health: null, magicka: null, stamina: null);
      }
      if (value is! Map<String, Object?>) {
        throw const ProtocolFormatException(
          'Character Vitals value must be an object or null.',
        );
      }
      final CharacterVitalsState state = _$CharacterVitalsStateFromJson(value);
      if (state.health == null ||
          state.magicka == null ||
          state.stamina == null) {
        throw const ProtocolFormatException(
          'Character Vitals resources must be available together.',
        );
      }
      return state;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid character_vitals state: $error');
    }
  }
}
