import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';

part 'rename_request_payload.g.dart';

/// Outgoing `rename_request` payload (`protocol/schema/README.md`'s `rename_request`). Encode-only:
/// the client never decodes its own `rename_request`.
@JsonSerializable(createFactory: false)
class RenameRequestPayload {
  /// The requested display name. May be empty, which clears the device's display name.
  final String displayName;

  /// Creates a rename-request payload.
  const RenameRequestPayload({required this.displayName});

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => _$RenameRequestPayloadToJson(this);
}
