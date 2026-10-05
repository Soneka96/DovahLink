import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises the app-owned tracked-quest projection.
void main() {
  group('LiveTrackedQuest behaves correctly', () {
    test('LiveTrackedQuest preserves raw type and all ordered objectives', () {
      final LiveTrackedQuest quest = Fixtures.buildLiveTrackedQuest(
        questId: 22,
        title: 'Quest title',
        type: 9,
        objectives: [
          (
            index: 2,
            instanceId: 41,
            text: 'Second objective',
            status: LiveQuestObjectiveStatus.failedAndDisplayed,
          ),
          (
            index: 4,
            instanceId: 42,
            text: null,
            status: LiveQuestObjectiveStatus.completed,
          ),
        ],
      );

      expect(quest.questId, 22);
      expect(quest.title, 'Quest title');
      expect(quest.type, 9);
      expect(quest.objectives, [
        (
          index: 2,
          instanceId: 41,
          text: 'Second objective',
          status: LiveQuestObjectiveStatus.failedAndDisplayed,
        ),
        (
          index: 4,
          instanceId: 42,
          text: null,
          status: LiveQuestObjectiveStatus.completed,
        ),
      ]);
    });

    test('LiveTrackedQuest makes its objective collection immutable', () {
      final List<
        ({
          int index,
          int instanceId,
          String? text,
          LiveQuestObjectiveStatus status,
        })
      >
      objectives = [
        (
          index: 0,
          instanceId: 1,
          text: null,
          status: LiveQuestObjectiveStatus.dormant,
        ),
      ];
      final LiveTrackedQuest quest = Fixtures.buildLiveTrackedQuest(
        objectives: objectives,
      );

      expect(quest.objectives, hasLength(1));
      expect(
        () => quest.objectives.add(objectives.single),
        throwsUnsupportedError,
      );
    });

    test('LiveTrackedQuest copies the input objective collection', () {
      final List<
        ({
          int index,
          int instanceId,
          String? text,
          LiveQuestObjectiveStatus status,
        })
      >
      objectives = [
        (
          index: 0,
          instanceId: 1,
          text: null,
          status: LiveQuestObjectiveStatus.dormant,
        ),
      ];
      final LiveTrackedQuest quest = Fixtures.buildLiveTrackedQuest(
        objectives: objectives,
      );

      objectives.add((
        index: 1,
        instanceId: 2,
        text: 'Later change',
        status: LiveQuestObjectiveStatus.displayed,
      ));

      expect(quest.objectives, hasLength(1));
    });
  });

  group('LiveTrackedQuest equality behaves correctly', () {
    test('LiveTrackedQuest equality compares quest and objective facts', () {
      final LiveTrackedQuest first = Fixtures.buildLiveTrackedQuest();
      final LiveTrackedQuest second = Fixtures.buildLiveTrackedQuest();

      expect(first, second);
      expect(first.hashCode, second.hashCode);
      expect(identical(first, second), isFalse);
    });
  });
}
