import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_stamina_max_state.g.dart';

/// The effective maximum Stamina actor value from a `character_stamina_max` Snapshot.
@JsonSerializable(checked: true, createToJson: false)
class CharacterStaminaMaxState {
  /// The raw maximum Stamina value, or `null` when the Host cannot read it.
  @JsonKey(required: true)
  final double? value;

  /// Creates maximum Stamina state.
  /// @param value The raw maximum Stamina value, or `null` when unavailable.
  const CharacterStaminaMaxState({required this.value});

  /// Decodes one `character_stamina_max` state-area value.
  /// @param json The state-area value object.
  /// @return The typed maximum Stamina value.
  /// @throws [ProtocolFormatException] if the value is malformed.
  factory CharacterStaminaMaxState.fromJson(JsonMap json) {
    try {
      return _$CharacterStaminaMaxStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException(
        'Invalid character_stamina_max state: $error',
      );
    }
  }
}
