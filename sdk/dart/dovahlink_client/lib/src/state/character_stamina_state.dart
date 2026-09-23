import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_stamina_state.g.dart';

/// The current stamina value from a `character_stamina` snapshot.
@JsonSerializable(checked: true, createToJson: false)
class CharacterStaminaState {
  /// The current stamina value, or `null` when the Host cannot read it.
  @JsonKey(required: true)
  final double? value;

  /// Creates character stamina state.
  /// @param value Current stamina, or `null` when unavailable.
  const CharacterStaminaState({required this.value});

  /// Decodes one `character_stamina` state-area value.
  /// @param json The state-area value object.
  /// @return The typed stamina value.
  /// @throws [ProtocolFormatException] if the value is malformed.
  factory CharacterStaminaState.fromJson(JsonMap json) {
    try {
      return _$CharacterStaminaStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid character_stamina state: $error');
    }
  }
}
