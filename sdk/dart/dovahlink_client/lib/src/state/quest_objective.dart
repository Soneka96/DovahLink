import 'dart:convert';

import 'package:json_annotation/json_annotation.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

part 'quest_objective.g.dart';

/// One current objective instance belonging to a tracked quest.
@JsonSerializable(checked: true, createToJson: false)
class QuestObjective {
  /// The authored objective index.
  @JsonKey(required: true)
  final int index;

  /// The engine quest-instance identifier.
  @JsonKey(required: true)
  final int instanceId;

  /// The localized authored objective text, or `null` when unavailable.
  @JsonKey(required: true)
  final String? text;

  /// The engine-reported state for this objective instance.
  @JsonKey(required: true)
  final TrackedQuestObjectiveState state;

  /// Creates one validated objective value.
  /// @param index The authored objective index.
  /// @param instanceId The engine quest-instance identifier.
  /// @param text The localized authored text, or `null` when absent.
  /// @param state The engine-reported objective state.
  /// @throws [FormatException] if numeric fields or display text exceed their protocol bounds.
  factory QuestObjective({
    required int index,
    required int instanceId,
    required String? text,
    required TrackedQuestObjectiveState state,
  }) {
    if (index < 0 ||
        index > 0xFFFF ||
        instanceId < 0 ||
        instanceId > 0xFFFFFFFF ||
        (text != null &&
            (text.contains('\u0000') || utf8.encode(text).length > 126))) {
      throw const FormatException(
        'Quest objective facts are outside their bounds.',
      );
    }
    return QuestObjective._(
      index: index,
      instanceId: instanceId,
      text: text,
      state: state,
    );
  }

  /// Stores one validated objective value.
  /// @param index The authored objective index.
  /// @param instanceId The engine quest-instance identifier.
  /// @param text The localized authored text, or `null` when absent.
  /// @param state The engine-reported objective state.
  const QuestObjective._({
    required this.index,
    required this.instanceId,
    required this.text,
    required this.state,
  });

  /// Decodes one objective instance from its protocol object.
  /// @param json The objective object.
  /// @return The typed objective.
  /// @throws [ProtocolFormatException] if a field is malformed or outside its bounds.
  factory QuestObjective.fromJson(JsonMap json) {
    try {
      return _$QuestObjectiveFromJson(json);
    } on Object catch (error) {
      throw ProtocolFormatException('Invalid tracked quest objective: $error');
    }
  }
}
