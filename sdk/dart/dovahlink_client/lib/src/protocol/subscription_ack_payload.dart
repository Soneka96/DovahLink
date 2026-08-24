import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';

part 'subscription_ack_payload.g.dart';

/// Incoming `subscription_ack` payload (`protocol/schema/README.md`'s `subscription_ack`).
/// Decode-only: the client never sends `subscription_ack`.
@JsonSerializable(checked: true, createToJson: false)
class SubscriptionAckPayload {
  /// The state areas accepted by the bridge.
  @JsonKey(required: true)
  final List<String> acceptedStateAreas;

  /// The state areas rejected by the bridge.
  @JsonKey(required: true)
  final List<String> rejectedStateAreas;

  /// Creates a subscription-ack payload.
  const SubscriptionAckPayload({required this.acceptedStateAreas, required this.rejectedStateAreas});

  /// Decodes and validates one subscription-ack payload.
  factory SubscriptionAckPayload.fromJson(JsonMap json) {
    try {
      return _$SubscriptionAckPayloadFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid subscription_ack payload: $error');
    }
  }
}
