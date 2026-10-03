import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_health_max_state.g.dart';

/// The effective maximum Health actor value from a `character_health_max` Snapshot.
@JsonSerializable(checked: true, createToJson: false)
class CharacterHealthMaxState {
  /// The raw maximum Health value, or `null` when the Host cannot read it.
  @JsonKey(required: true)
  final double? value;

  /// Creates maximum Health state.
  /// @param value The raw maximum Health value, or `null` when unavailable.
  const CharacterHealthMaxState({required this.value});

  /// Decodes one `character_health_max` state-area value.
  /// @param json The state-area value object.
  /// @return The typed maximum Health value.
  /// @throws [ProtocolFormatException] if the value is malformed.
  factory CharacterHealthMaxState.fromJson(JsonMap json) {
    try {
      return _$CharacterHealthMaxStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException(
        'Invalid character_health_max state: $error',
      );
    }
  }
}
