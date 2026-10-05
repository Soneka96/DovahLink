import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/tracked_quest.dart';

part 'tracked_quests_state.g.dart';

/// The complete collection of quests currently tracked by the player.
@JsonSerializable(checked: true, createToJson: false)
class TrackedQuestsState {
  /// Every currently tracked quest in Host-provided deterministic order.
  @JsonKey(required: true)
  final List<TrackedQuest> quests;

  /// Creates one complete tracked-quest value with an immutable quest list.
  /// @param quests Every currently tracked quest; an empty list is available state.
  /// @throws [FormatException] if the collection exceeds its bounds or contains duplicate IDs.
  factory TrackedQuestsState({required List<TrackedQuest> quests}) {
    if (quests.length > 128) {
      throw const FormatException(
        'Tracked quest collection exceeds its bound.',
      );
    }
    final Set<int> questIds = <int>{};
    int objectiveCount = 0;
    for (final TrackedQuest quest in quests) {
      if (!questIds.add(quest.questId)) {
        throw const FormatException(
          'Tracked quest collection has duplicate quest IDs.',
        );
      }
      objectiveCount += quest.objectives.length;
      if (objectiveCount > 1024) {
        throw const FormatException(
          'Tracked quest collection exceeds its objective bound.',
        );
      }
    }
    return TrackedQuestsState._(List<TrackedQuest>.unmodifiable(quests));
  }

  /// Stores one complete immutable quest list.
  /// @param quests The complete ordered quest list.
  const TrackedQuestsState._(this.quests);

  /// Decodes the available tracked-quest collection inside a state value.
  /// @param json The object inside the protocol `value` field.
  /// @return The complete typed collection.
  /// @throws [ProtocolFormatException] if the collection is malformed or outside its bounds.
  factory TrackedQuestsState.fromJson(JsonMap json) {
    try {
      return _$TrackedQuestsStateFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid tracked_quests state: $error');
    }
  }
}

/// Decodes an available or unavailable `tracked_quests` state-area value.
/// @param json The protocol state-area `data` object.
/// @return The typed collection, or `null` for explicit unavailability.
/// @throws [ProtocolFormatException] if `value` is missing or malformed.
TrackedQuestsState? decodeTrackedQuestsState(JsonMap json) {
  if (!json.containsKey('value')) {
    throw const ProtocolFormatException(
      'Tracked Quests state is missing its value field.',
    );
  }
  final Object? value = json['value'];
  if (value == null) {
    return null;
  }
  if (value is! Map<String, Object?>) {
    throw const ProtocolFormatException(
      'Tracked Quests value must be an object or null.',
    );
  }
  return TrackedQuestsState.fromJson(value);
}
