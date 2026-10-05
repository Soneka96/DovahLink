import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';

/// One tracked quest and all of its current objective instances.
class LiveTrackedQuest extends Equatable {
  /// Creates a quest projection with an immutable objective collection.
  /// @param questId The runtime quest identifier reported by the SDK.
  /// @param title The localized quest title reported by the SDK.
  /// @param type The raw Skyrim quest type.
  /// @param objectives Every current objective instance in SDK-provided order.
  LiveTrackedQuest({
    required this.questId,
    required this.title,
    required this.type,
    required List<
      ({
        int index,
        int instanceId,
        String? text,
        LiveQuestObjectiveStatus status,
      })
    >
    objectives,
  }) : objectives = List.unmodifiable(objectives);

  /// The runtime quest identifier reported by the SDK.
  final int questId;

  /// The localized quest title reported by the SDK.
  final String title;

  /// The raw Skyrim quest type.
  final int type;

  /// Every current objective instance in SDK-provided order.
  final List<
    ({int index, int instanceId, String? text, LiveQuestObjectiveStatus status})
  >
  objectives;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [questId, title, type, objectives];
}
