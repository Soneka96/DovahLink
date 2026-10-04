import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_identity_state.g.dart';

/// The complete player display name and identity-race display name.
@JsonSerializable(checked: true, createToJson: false)
class CharacterIdentityState {
  /// The player's display name.
  @JsonKey(required: true)
  final String name;

  /// The game-provided display name for the identity race.
  @JsonKey(required: true)
  final String race;

  /// Creates a complete, available Character Identity value.
  /// @param name The player's display name.
  /// @param race The identity-race display name.
  const CharacterIdentityState({required this.name, required this.race});

  /// Decodes one available Character Identity value object.
  /// @param json The complete object inside the protocol `value` field.
  /// @return The typed identity.
  /// @throws [ProtocolFormatException] if either required string is missing or malformed.
  factory CharacterIdentityState.fromJson(JsonMap json) {
    try {
      return _$CharacterIdentityStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid character_identity state: $error');
    }
  }
}

/// Decodes one complete or unavailable `character_identity` state-area value.
/// @param json The protocol state-area `data` object.
/// @return A typed identity, or `null` for the explicit unavailable envelope.
/// @throws [ProtocolFormatException] if `value` is missing or malformed.
CharacterIdentityState? decodeCharacterIdentityState(JsonMap json) {
  if (!json.containsKey('value')) {
    throw const ProtocolFormatException(
      'Character Identity state is missing its value field.',
    );
  }
  final Object? value = json['value'];
  if (value == null) {
    return null;
  }
  if (value is! Map<String, Object?>) {
    throw const ProtocolFormatException(
      'Character Identity value must be an object or null.',
    );
  }
  return CharacterIdentityState.fromJson(value);
}
