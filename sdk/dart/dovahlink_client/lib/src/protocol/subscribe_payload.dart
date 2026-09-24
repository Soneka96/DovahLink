import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';

part 'subscribe_payload.g.dart';

/// Outgoing `subscribe` payload (`protocol/schema/README.md`'s `subscribe`). [stateAreas] is the
/// client's complete desired active set; an empty list removes every state-area subscription.
/// Encode-only: the client never decodes its own `subscribe`.
@JsonSerializable(createFactory: false)
class SubscribePayload {
  /// The complete desired set of state areas for this connection.
  final List<String> stateAreas;

  /// Creates a subscribe payload.
  const SubscribePayload({required this.stateAreas});

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => _$SubscribePayloadToJson(this);
}
