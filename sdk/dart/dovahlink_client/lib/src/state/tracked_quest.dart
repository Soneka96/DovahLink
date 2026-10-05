import 'dart:convert';

import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/state/quest_objective.dart';

part 'tracked_quest.g.dart';

/// One currently tracked quest and its current objective instances.
@JsonSerializable(checked: true, createToJson: false)
class TrackedQuest {
  /// The active-runtime quest FormID.
  @JsonKey(required: true)
  final int questId;

  /// The localized title supplied by the running game.
  @JsonKey(required: true)
  final String title;

  /// The raw Skyrim quest-type value.
  @JsonKey(required: true)
  final int type;

  /// The objective instances in Host-provided deterministic order.
  @JsonKey(required: true)
  final List<QuestObjective> objectives;

  /// Creates one validated tracked quest with an immutable objective list.
  /// @param questId The active-runtime quest FormID.
  /// @param title The localized title supplied by the running game.
  /// @param type The raw Skyrim quest-type value.
  /// @param objectives The complete current objective instances.
  /// @throws [FormatException] if identity, title, type, or objective facts exceed their bounds.
  factory TrackedQuest({
    required int questId,
    required String title,
    required int type,
    required List<QuestObjective> objectives,
  }) {
    if (questId <= 0 ||
        questId > 0xFFFFFFFF ||
        title.isEmpty ||
        title.contains('\u0000') ||
        utf8.encode(title).length > 126 ||
        type < 0 ||
        type > 0xFF ||
        objectives.length > 1024) {
      throw const FormatException(
        'Tracked quest facts are outside their bounds.',
      );
    }
    final Set<(int, int)> objectiveIds = <(int, int)>{};
    for (final QuestObjective objective in objectives) {
      if (!objectiveIds.add((objective.index, objective.instanceId))) {
        throw const FormatException(
          'Tracked quest contains duplicate objective instances.',
        );
      }
    }
    return TrackedQuest._(
      questId: questId,
      title: title,
      type: type,
      objectives: List<QuestObjective>.unmodifiable(objectives),
    );
  }

  /// Stores one validated quest and its immutable ordered objective list.
  /// @param questId The active-runtime quest FormID.
  /// @param title The localized title supplied by the running game.
  /// @param type The raw Skyrim quest-type value.
  /// @param objectives The complete current objective instances.
  const TrackedQuest._({
    required this.questId,
    required this.title,
    required this.type,
    required this.objectives,
  });

  /// Decodes one tracked quest from its protocol object.
  /// @param json The quest object.
  /// @return The typed quest.
  /// @throws [ProtocolFormatException] if a field is malformed or outside its bounds.
  factory TrackedQuest.fromJson(JsonMap json) {
    try {
      return _$TrackedQuestFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid tracked quest: $error');
    }
  }
}
