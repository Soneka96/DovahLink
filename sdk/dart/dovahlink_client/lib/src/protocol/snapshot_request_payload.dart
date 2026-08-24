import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';

part 'snapshot_request_payload.g.dart';

/// Outgoing `snapshot_request` payload (`protocol/schema/README.md`'s `snapshot_request`).
/// Encode-only: the client never decodes its own `snapshot_request`.
@JsonSerializable(createFactory: false)
class SnapshotRequestPayload {
  /// The state area whose snapshot is requested.
  final String stateArea;

  /// The client's latest known revision, when available; advisory only. Omitted from the encoded
  /// object entirely (not merely `null`) when absent.
  @JsonKey(includeIfNull: false)
  final int? knownRevision;

  /// Creates a snapshot-request payload.
  const SnapshotRequestPayload({required this.stateArea, this.knownRevision});

  /// Encodes this payload as a JSON object.
  JsonMap toJson() => _$SnapshotRequestPayloadToJson(this);
}
