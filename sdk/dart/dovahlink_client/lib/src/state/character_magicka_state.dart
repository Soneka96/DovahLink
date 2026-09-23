import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_magicka_state.g.dart';

/// The current magicka value from a `character_magicka` snapshot.
@JsonSerializable(checked: true, createToJson: false)
class CharacterMagickaState {
  /// The current magicka value, or `null` when the Host cannot read it.
  @JsonKey(required: true)
  final double? value;

  /// Creates character magicka state.
  /// @param value Current magicka, or `null` when unavailable.
  const CharacterMagickaState({required this.value});

  /// Decodes one `character_magicka` state-area value.
  /// @param json The state-area value object.
  /// @return The typed magicka value.
  /// @throws [ProtocolFormatException] if the value is malformed.
  factory CharacterMagickaState.fromJson(JsonMap json) {
    try {
      return _$CharacterMagickaStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid character_magicka state: $error');
    }
  }
}
