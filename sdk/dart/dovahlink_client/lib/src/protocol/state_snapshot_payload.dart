import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_revision_validator.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_timestamp_validator.dart';

part 'state_snapshot_payload.g.dart';

/// Incoming `state_snapshot` payload (`protocol/schema/README.md`'s `state_snapshot`). Decode-only:
/// the client never sends `state_snapshot`. `data` stays an untyped [JsonMap]: no state area is
/// currently registered (protocol/schema/README.md's "Registered state areas"), so there is no
/// domain shape to decode it against yet.
@JsonSerializable(checked: true, createToJson: false)
class StateSnapshotPayload {
  /// The state area this snapshot represents.
  @JsonKey(required: true)
  final String stateArea;

  /// The revision established by this snapshot.
  @JsonKey(required: true)
  final int revision;

  /// UTC RFC 3339 wall-clock time for display and diagnostics; not an ordering source.
  @JsonKey(required: true)
  final String occurredAt;

  /// The state-area-specific snapshot data.
  @JsonKey(required: true)
  final JsonMap data;

  /// Creates a state-snapshot payload.
  const StateSnapshotPayload({
    required this.stateArea,
    required this.revision,
    required this.occurredAt,
    required this.data,
  });

  /// Decodes and validates one `state_snapshot` payload.
  factory StateSnapshotPayload.fromJson(JsonMap json) {
    try {
      ProtocolRevisionValidator.validateNonNegativeInt(
        json['revision'],
        'revision',
      );
      final StateSnapshotPayload payload = _$StateSnapshotPayloadFromJson(json);
      ProtocolTimestampValidator.validateUtcRfc3339(payload.occurredAt);
      return payload;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid state_snapshot payload: $error');
    }
  }
}
