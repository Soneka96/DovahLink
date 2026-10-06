import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/session/presentation/viewdata/session_overview_quest.viewdata.dart';
import '../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkStateStatus,
        QuestObjective,
        TrackedQuest,
        TrackedQuestObjectiveState,
        TrackedQuestsState;

/// Exercises truthful single and plural quest-card summaries.
void main() {
  group('Method fromSynchronization behaves correctly', () {
    test('Method fromSynchronization omits unknown quest state', () {
      final SessionOverviewQuestViewData viewData =
          SessionOverviewQuestViewData.fromSynchronization(
            Fixtures.buildStateSynchronization<TrackedQuestsState?>(
              status: DovahLinkStateStatus.failed,
              value: null,
            ),
          );

      expect(viewData.title, isNull);
      expect(viewData.detail, isNull);
      expect(viewData.status, DovahLinkStateStatus.failed);
    });

    test('Method fromSynchronization uses the approved empty-quest copy', () {
      final SessionOverviewQuestViewData viewData =
          SessionOverviewQuestViewData.fromSynchronization(
            Fixtures.buildStateSynchronization<TrackedQuestsState?>(
              value: Fixtures.buildTrackedQuests(quests: <TrackedQuest>[]),
            ),
          );

      expect(viewData.title, 'NO QUEST TRACKED');
      expect(viewData.detail, 'No path is marked.');
    });

    test(
      'Method fromSynchronization uses one real title and displayed objectives',
      () {
        final SessionOverviewQuestViewData viewData =
            SessionOverviewQuestViewData.fromSynchronization(
              Fixtures.buildStateSynchronization<TrackedQuestsState?>(
                value: Fixtures.buildTrackedQuests(
                  quests: <TrackedQuest>[
                    Fixtures.buildTrackedQuest(
                      title: 'The Horn of Jurgen Windcaller',
                      objectives: <QuestObjective>[
                        Fixtures.buildQuestObjective(
                          index: 10,
                          text: 'Retrieve the Horn from Ustengrav.',
                        ),
                        Fixtures.buildQuestObjective(
                          index: 20,
                          text: 'A dormant objective.',
                          state: TrackedQuestObjectiveState.dormant,
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            );

        expect(viewData.title, 'The Horn of Jurgen Windcaller');
        expect(viewData.detail, 'Retrieve the Horn from Ustengrav.');
      },
    );

    test(
      'Method fromSynchronization retains every displayed objective in order',
      () {
        final List<QuestObjective> objectives = TrackedQuestObjectiveState
            .values
            .indexed
            .map(
              ((int, TrackedQuestObjectiveState) entry) =>
                  Fixtures.buildQuestObjective(
                    index: entry.$1,
                    instanceId: entry.$1 + 1,
                    text: '${entry.$2.name} objective',
                    state: entry.$2,
                  ),
            )
            .toList();
        final SessionOverviewQuestViewData viewData =
            SessionOverviewQuestViewData.fromSynchronization(
              Fixtures.buildStateSynchronization<TrackedQuestsState?>(
                value: Fixtures.buildTrackedQuests(
                  quests: <TrackedQuest>[
                    Fixtures.buildTrackedQuest(objectives: objectives),
                  ],
                ),
              ),
            );

        expect(viewData.detail, 'displayed objective');
      },
    );

    test(
      'Method fromSynchronization keeps multiple usable objective lines',
      () {
        final SessionOverviewQuestViewData viewData =
            SessionOverviewQuestViewData.fromSynchronization(
              Fixtures.buildStateSynchronization<TrackedQuestsState?>(
                value: Fixtures.buildTrackedQuests(
                  quests: <TrackedQuest>[
                    Fixtures.buildTrackedQuest(
                      objectives: <QuestObjective>[
                        Fixtures.buildQuestObjective(
                          index: 1,
                          text: 'First objective',
                        ),
                        Fixtures.buildQuestObjective(index: 2, text: '  '),
                        Fixtures.buildQuestObjective(
                          index: 3,
                          text: 'Third objective',
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            );

        expect(viewData.detail, 'First objective\nThird objective');
      },
    );

    test(
      'Method fromSynchronization omits absent or non-current objectives',
      () {
        final SessionOverviewQuestViewData noObjectives =
            SessionOverviewQuestViewData.fromSynchronization(
              Fixtures.buildStateSynchronization<TrackedQuestsState?>(
                value: Fixtures.buildTrackedQuests(
                  quests: <TrackedQuest>[
                    Fixtures.buildTrackedQuest(
                      title: 'The Horn of Jurgen Windcaller',
                      objectives: <QuestObjective>[
                        Fixtures.buildQuestObjective(index: 1, text: null),
                        Fixtures.buildQuestObjective(
                          index: 2,
                          text: 'Completed objective',
                          state: TrackedQuestObjectiveState.completed,
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            );

        expect(noObjectives.title, 'The Horn of Jurgen Windcaller');
        expect(noObjectives.detail, isNull);
      },
    );

    test(
      'Method fromSynchronization does not choose one from multiple quests',
      () {
        final SessionOverviewQuestViewData viewData =
            SessionOverviewQuestViewData.fromSynchronization(
              Fixtures.buildStateSynchronization<TrackedQuestsState?>(
                value: Fixtures.buildTrackedQuests(
                  quests: <TrackedQuest>[
                    Fixtures.buildTrackedQuest(title: 'First quest'),
                    Fixtures.buildTrackedQuest(
                      questId: 2,
                      title: 'Second quest',
                    ),
                  ],
                ),
              ),
            );

        expect(viewData.title, '2 QUESTS TRACKED');
        expect(viewData.detail, 'Multiple paths remain open.');
        expect(viewData.title, isNot('First quest'));
        expect(viewData.title, isNot('Second quest'));
      },
    );

    test('Method fromSynchronization preserves stale quest presentation', () {
      final SessionOverviewQuestViewData viewData =
          SessionOverviewQuestViewData.fromSynchronization(
            Fixtures.buildStateSynchronization<TrackedQuestsState?>(
              status: DovahLinkStateStatus.stale,
              value: Fixtures.buildTrackedQuests(),
            ),
          );

      expect(viewData.status, DovahLinkStateStatus.stale);
      expect(viewData.title, 'Test Quest');
      expect(viewData.detail, 'Complete the objective');
    });
  });
}
