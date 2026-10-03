import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_magicka_max_state.g.dart';

/// The effective maximum Magicka actor value from a `character_magicka_max` Snapshot.
@JsonSerializable(checked: true, createToJson: false)
class CharacterMagickaMaxState {
  /// The raw maximum Magicka value, or `null` when the Host cannot read it.
  @JsonKey(required: true)
  final double? value;

  /// Creates maximum Magicka state.
  /// @param value The raw maximum Magicka value, or `null` when unavailable.
  const CharacterMagickaMaxState({required this.value});

  /// Decodes one `character_magicka_max` state-area value.
  /// @param json The state-area value object.
  /// @return The typed maximum Magicka value.
  /// @throws [ProtocolFormatException] if the value is malformed.
  factory CharacterMagickaMaxState.fromJson(JsonMap json) {
    try {
      return _$CharacterMagickaMaxStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException(
        'Invalid character_magicka_max state: $error',
      );
    }
  }
}
