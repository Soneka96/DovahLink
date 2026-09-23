import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_xp_state.g.dart';

/// The current character experience value from a `character_xp` snapshot.
@JsonSerializable(checked: true, createToJson: false)
class CharacterXpState {
  /// The current experience value, or `null` when the Host cannot read it.
  @JsonKey(required: true)
  final double? value;

  /// Creates character experience state.
  /// @param value Current experience, or `null` when unavailable.
  const CharacterXpState({required this.value});

  /// Decodes one `character_xp` state-area value.
  /// @param json The state-area value object.
  /// @return The typed experience value.
  /// @throws [ProtocolFormatException] if the value is malformed.
  factory CharacterXpState.fromJson(JsonMap json) {
    try {
      return _$CharacterXpStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid character_xp state: $error');
    }
  }
}
