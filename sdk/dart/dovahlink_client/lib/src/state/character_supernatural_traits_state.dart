import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_supernatural_traits_state.g.dart';

/// The three independent supernatural status and transformation capabilities.
@JsonSerializable(checked: true, createToJson: false)
class CharacterSupernaturalTraitsState {
  /// Whether the PlayerIsVampire global is nonzero.
  @JsonKey(required: true)
  final bool isVampire;

  /// Whether the player possesses the Vampire Lord transformation spell.
  @JsonKey(required: true)
  final bool hasVampireLordForm;

  /// Whether the player possesses the Beast Form transformation spell.
  @JsonKey(required: true)
  final bool hasWerewolfForm;

  /// Creates one complete available supernatural-traits value.
  /// @param isVampire The observed vampire status.
  /// @param hasVampireLordForm The observed Vampire Lord capability.
  /// @param hasWerewolfForm The observed Beast Form capability.
  const CharacterSupernaturalTraitsState({
    required this.isVampire,
    required this.hasVampireLordForm,
    required this.hasWerewolfForm,
  });

  /// Decodes one available supernatural-traits value object.
  /// @param json The complete object inside the protocol `value` field.
  /// @return The typed independent predicates.
  /// @throws [ProtocolFormatException] if any required boolean is missing or malformed.
  factory CharacterSupernaturalTraitsState.fromJson(JsonMap json) {
    try {
      return _$CharacterSupernaturalTraitsStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException(
        'Invalid character_supernatural_traits state: $error',
      );
    }
  }
}

/// Decodes one complete or unavailable `character_supernatural_traits` value.
/// @param json The protocol state-area `data` object.
/// @return Typed independent predicates, or `null` for explicit unavailability.
/// @throws [ProtocolFormatException] if `value` is missing or malformed.
CharacterSupernaturalTraitsState? decodeCharacterSupernaturalTraitsState(
  JsonMap json,
) {
  if (!json.containsKey('value')) {
    throw const ProtocolFormatException(
      'Character supernatural traits are missing the value field.',
    );
  }
  final Object? value = json['value'];
  if (value == null) {
    return null;
  }
  if (value is! Map<String, Object?>) {
    throw const ProtocolFormatException(
      'Character supernatural traits must be an object or null.',
    );
  }
  return CharacterSupernaturalTraitsState.fromJson(value);
}
