import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_level_state.g.dart';

/// The current character level from a `character_level` snapshot or event.
@JsonSerializable(checked: true, createToJson: false)
class CharacterLevelState {
  /// The current level, or `null` when the Host cannot read it.
  @JsonKey(required: true, fromJson: decodeCharacterLevel)
  final int? value;

  /// Creates character level state.
  /// @param value Current level, or `null` when unavailable.
  const CharacterLevelState({required this.value});

  /// Decodes one `character_level` state-area value.
  /// @param json The state-area value object.
  /// @return The typed level value.
  /// @throws [ProtocolFormatException] if the value is malformed.
  factory CharacterLevelState.fromJson(JsonMap json) {
    try {
      return _$CharacterLevelStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid character_level state: $error');
    }
  }
}

/// Decodes a nullable, integer-valued level in the canonical 0–65535 range.
/// @param value The JSON value to validate.
/// @return The decoded level, or `null` when unavailable.
/// @throws [ProtocolFormatException] if the value is not a valid level.
int? decodeCharacterLevel(Object? value) {
  if (value == null) {
    return null;
  }
  if (value is! num ||
      !value.isFinite ||
      value < 0 ||
      value > 65535 ||
      value != value.toInt()) {
    throw const ProtocolFormatException(
      'Character level must be null or an integer from 0 through 65535.',
    );
  }
  return value.toInt();
}
