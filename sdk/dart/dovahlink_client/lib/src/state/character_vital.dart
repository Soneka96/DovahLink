import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'character_vital.g.dart';

/// One resource's current and effective maximum values from a Vitals observation.
@JsonSerializable(checked: true, createToJson: false)
class CharacterVital {
  /// The current value of this resource.
  @JsonKey(required: true)
  final double current;

  /// The effective maximum value of this resource.
  @JsonKey(required: true)
  final double max;

  /// Creates one typed resource value.
  /// @param current The current resource value.
  /// @param max The effective maximum resource value.
  const CharacterVital({required this.current, required this.max});

  /// Decodes one current-and-maximum resource value.
  /// @param json The nested resource object.
  /// @return The typed resource value.
  /// @throws [ProtocolFormatException] if either field is missing or malformed.
  factory CharacterVital.fromJson(JsonMap json) {
    try {
      return _$CharacterVitalFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid Character resource value: $error');
    }
  }
}
