import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'capability.g.dart';

/// One entry in a `capabilities` message's `capabilities` list (`protocol/schema/README.md`'s
/// `capabilities`). `id` and `version` are canonical protocol values, independent of the Bridge
/// release version; no capability is currently registered.
@JsonSerializable(checked: true)
class Capability {
  /// The capability identifier.
  @JsonKey(required: true)
  final String id;

  /// The capability version.
  @JsonKey(required: true)
  final int version;

  /// Creates a capability entry.
  const Capability({required this.id, required this.version});

  /// Decodes and validates one capability entry.
  factory Capability.fromJson(JsonMap json) {
    try {
      return _$CapabilityFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid capability entry: $error');
    }
  }

  /// Encodes this capability entry as a JSON object.
  JsonMap toJson() => _$CapabilityToJson(this);
}
