import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_timestamp_validator.dart';

part 'state_event_payload.g.dart';

/// Incoming `state_event` payload (`protocol/schema/README.md`'s `state_event`). Decode-only: the
/// client never sends `state_event`. `data` stays an untyped [JsonMap]: no state area is currently
/// registered (protocol/schema/README.md's "Registered state areas"), so there is no domain shape
/// to decode it against yet. Contains complete post-change state, not a patch.
@JsonSerializable(checked: true, createToJson: false)
class StateEventPayload {
  /// The state area this event represents.
  @JsonKey(required: true)
  final String stateArea;

  /// The revision this event expects the client to have before applying it.
  @JsonKey(required: true)
  final int baseRevision;

  /// The revision established by this event. Must equal [baseRevision] + 1.
  @JsonKey(required: true)
  final int revision;

  /// UTC RFC 3339 wall-clock time for display and diagnostics; not an ordering source.
  @JsonKey(required: true)
  final String occurredAt;

  /// The complete post-change, state-area-specific data.
  @JsonKey(required: true)
  final JsonMap data;

  /// Creates a state-event payload.
  const StateEventPayload({
    required this.stateArea,
    required this.baseRevision,
    required this.revision,
    required this.occurredAt,
    required this.data,
  });

  /// Decodes and validates one `state_event` payload.
  factory StateEventPayload.fromJson(JsonMap json) {
    try {
      _requireNonNegativeInt(json['baseRevision'], 'baseRevision');
      _requireNonNegativeInt(json['revision'], 'revision');

      final StateEventPayload payload = _$StateEventPayloadFromJson(json);
      ProtocolTimestampValidator.validateUtcRfc3339(payload.occurredAt);
      if (payload.revision != payload.baseRevision + 1) {
        throw const ProtocolFormatException(
          'revision must equal baseRevision + 1.',
        );
      }
      return payload;
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid state_event payload: $error');
    }
  }
}

/// Requires one state-event revision field to be an integer greater than or equal to zero before
/// generated JSON conversion can coerce a numeric value to Dart's [int].
void _requireNonNegativeInt(Object? value, String fieldName) {
  if (value is! int || value < 0) {
    throw ProtocolFormatException('$fieldName must be a non-negative integer.');
  }
}
