import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/capability.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'capabilities_payload.g.dart';

/// A `capabilities` message payload (`protocol/schema/README.md`'s `capabilities`), sent by both
/// endpoints after `hello_ack`. No capability is currently registered, so both the Bridge's and
/// this SDK's own list are always empty.
@JsonSerializable(checked: true, explicitToJson: true)
class CapabilitiesPayload {
  /// The capabilities advertised or requested by the sender.
  @JsonKey(required: true)
  final List<Capability> capabilities;

  /// Creates a capabilities payload.
  const CapabilitiesPayload({required this.capabilities});

  /// Decodes and validates one capabilities payload.
  factory CapabilitiesPayload.fromJson(JsonMap json) {
    try {
      return _$CapabilitiesPayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid capabilities payload: $error');
    }
  }

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => _$CapabilitiesPayloadToJson(this);
}
