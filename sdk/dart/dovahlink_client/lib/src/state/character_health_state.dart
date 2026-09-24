import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_health_state.g.dart';

/// The current health value from a `character_health` snapshot.
@JsonSerializable(checked: true, createToJson: false)
class CharacterHealthState {
  /// The current health value, or `null` when the Host cannot read it.
  @JsonKey(required: true)
  final double? value;

  /// Creates character health state.
  /// @param value Current health, or `null` when unavailable.
  const CharacterHealthState({required this.value});

  /// Decodes one `character_health` state-area value.
  /// @param json The state-area value object.
  /// @return The typed health value.
  /// @throws [ProtocolFormatException] if the value is malformed.
  factory CharacterHealthState.fromJson(JsonMap json) {
    try {
      return _$CharacterHealthStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid character_health state: $error');
    }
  }
}
