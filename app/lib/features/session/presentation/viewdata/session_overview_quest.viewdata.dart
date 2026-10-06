import 'package:equatable/equatable.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkStateStatus,
        QuestObjective,
        StateSynchronization,
        TrackedQuest,
        TrackedQuestObjectiveState,
        TrackedQuestsState;

/// The Overview's single quest-card title, detail, and sync standing.
class SessionOverviewQuestViewData extends Equatable {
  /// The real quest title or truthful zero/plural summary, when known.
  final String? title;

  /// The real displayed objective or approved neutral summary, when known.
  final String? detail;

  /// The synchronization standing retained for visual state treatment.
  final DovahLinkStateStatus status;

  /// Creates the quest-card presentation values.
  /// @param title The usable quest title or truthful collection summary.
  /// @param detail The usable objective or approved collection detail.
  /// @param status The tracked-quest synchronization standing.
  const SessionOverviewQuestViewData({
    required this.title,
    required this.detail,
    required this.status,
  });

  /// Derives the card summary without selecting an arbitrary quest.
  /// @param synchronization The SDK's complete tracked-quest synchronization value.
  /// @return A truthful summary, or empty display values when no value is usable.
  factory SessionOverviewQuestViewData.fromSynchronization(
    StateSynchronization<TrackedQuestsState?> synchronization,
  ) {
    final TrackedQuestsState? state = synchronization.value;
    if (state == null) {
      return SessionOverviewQuestViewData(
        title: null,
        detail: null,
        status: synchronization.status,
      );
    }

    final List<TrackedQuest> quests = state.quests;
    if (quests.isEmpty) {
      return SessionOverviewQuestViewData(
        title: 'NO QUEST TRACKED',
        detail: 'No path is marked.',
        status: synchronization.status,
      );
    }
    if (quests.length > 1) {
      return SessionOverviewQuestViewData(
        title: '${quests.length} QUESTS TRACKED',
        detail: 'Multiple paths remain open.',
        status: synchronization.status,
      );
    }

    final TrackedQuest quest = quests.single;
    final String title = quest.title.trim();
    final List<String> objectives = <String>[
      for (final QuestObjective objective in quest.objectives)
        if (objective.state == TrackedQuestObjectiveState.displayed &&
            objective.text?.trim().isNotEmpty == true)
          objective.text!.trim(),
    ];
    return SessionOverviewQuestViewData(
      title: title.isEmpty ? null : title,
      detail: objectives.isEmpty ? null : objectives.join('\n'),
      status: synchronization.status,
    );
  }

  /// See [Equatable.props].
  @override
  List<Object?> get props => [title, detail, status];
}
