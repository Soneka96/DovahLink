import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/quest_objective.dart';
import 'package:dovahlink_client_sdk/src/state/tracked_quest.dart';
import 'package:dovahlink_client_sdk/src/state/tracked_quests_state.dart';

/// Reads one canonical tracked-quest fixture's state-area data.
/// @param fileName The shared fixture file name.
/// @return The complete state-area data object.
JsonMap _readTrackedQuestsFixtureData(String fileName) {
  final String text = File(
    '../../../protocol/fixtures/state/$fileName',
  ).readAsStringSync();
  final JsonMap envelope = jsonDecode(text) as JsonMap;
  final JsonMap payload = envelope['payload'] as JsonMap;
  return payload['data'] as JsonMap;
}

/// Builds one raw collection for protocol-boundary validation tests.
/// @param quests The protocol-shaped quest list.
/// @return A fresh state-area data object.
JsonMap _buildTrackedQuestsData(Object? quests) => <String, dynamic>{
  'value': <String, dynamic>{'quests': quests},
};

/// Tests the tracked-quest state value and its available/unavailable decoder.
void main() {
  group('Function decodeTrackedQuestsState behaves correctly', () {
    test(
      'Function decodeTrackedQuestsState preserves every fixture quest and objective state',
      () {
        final TrackedQuestsState? state = decodeTrackedQuestsState(
          _readTrackedQuestsFixtureData('state-snapshot-tracked-quests.json'),
        );

        expect(state, isNotNull);
        expect(state!.quests.map((quest) => quest.questId), [10, 20]);
        expect(state.quests.first.title, 'Cenário — Localized');
        expect(
          state.quests.first.objectives.map((objective) => objective.state),
          <TrackedQuestObjectiveState>[
            TrackedQuestObjectiveState.dormant,
            TrackedQuestObjectiveState.displayed,
            TrackedQuestObjectiveState.completed,
            TrackedQuestObjectiveState.completedAndDisplayed,
            TrackedQuestObjectiveState.failed,
            TrackedQuestObjectiveState.failedAndDisplayed,
          ],
        );
        expect(state.quests.first.objectives[2].text, isNull);
      },
    );

    test(
      'Function decodeTrackedQuestsState preserves an available empty collection',
      () {
        final TrackedQuestsState? state = decodeTrackedQuestsState(
          _readTrackedQuestsFixtureData(
            'state-snapshot-tracked-quests-empty.json',
          ),
        );

        expect(state, isNotNull);
        expect(state!.quests, isEmpty);
      },
    );

    test(
      'Function decodeTrackedQuestsState maps the unavailable fixture to null',
      () {
        expect(
          decodeTrackedQuestsState(
            _readTrackedQuestsFixtureData(
              'state-snapshot-tracked-quests-unavailable.json',
            ),
          ),
          isNull,
        );
      },
    );

    test(
      'Function decodeTrackedQuestsState rejects malformed state values',
      () {
        for (final JsonMap malformed in <JsonMap>[
          <String, dynamic>{},
          <String, dynamic>{'value': 'not-an-object'},
          <String, dynamic>{'value': <String, dynamic>{}},
          _buildTrackedQuestsData('not-a-list'),
          _buildTrackedQuestsData(<Object>[3]),
        ]) {
          expect(
            () => decodeTrackedQuestsState(malformed),
            throwsA(isA<ProtocolFormatException>()),
          );
        }
      },
    );
  });

  group('Factory TrackedQuestsState behaves correctly', () {
    test('Factory TrackedQuestsState accepts the maximum quest count', () {
      final List<TrackedQuest> quests = List<TrackedQuest>.generate(
        128,
        (int index) => TrackedQuest(
          questId: index + 1,
          title: 'Quest ${index + 1}',
          type: 0,
          objectives: <QuestObjective>[],
        ),
      );

      expect(TrackedQuestsState(quests: quests).quests, hasLength(128));
    });

    test(
      'Factory TrackedQuestsState rejects duplicate quest IDs and oversized lists',
      () {
        final JsonMap duplicateQuest = <String, dynamic>{
          'questId': 10,
          'title': 'Quest',
          'type': 0,
          'objectives': <Object>[],
        };

        expect(
          () => TrackedQuestsState.fromJson(
            _buildTrackedQuestsData(<JsonMap>[
                  duplicateQuest,
                  duplicateQuest,
                ])['value']
                as JsonMap,
          ),
          throwsA(isA<ProtocolFormatException>()),
        );

        final List<JsonMap> tooMany = List<JsonMap>.generate(
          129,
          (int index) => <String, dynamic>{
            'questId': index + 1,
            'title': 'Quest ${index + 1}',
            'type': 0,
            'objectives': <Object>[],
          },
        );
        expect(
          () =>
              TrackedQuestsState.fromJson(<String, dynamic>{'quests': tooMany}),
          throwsA(isA<ProtocolFormatException>()),
        );
      },
    );

    test(
      'Factory TrackedQuestsState rejects more than 1024 total objectives',
      () {
        final List<QuestObjective> objectiveBatch =
            List<QuestObjective>.generate(
              513,
              (int index) => QuestObjective(
                index: index,
                instanceId: 1,
                text: null,
                state: TrackedQuestObjectiveState.dormant,
              ),
            );
        final TrackedQuest firstQuest = TrackedQuest(
          questId: 1,
          title: 'Quest 1',
          type: 0,
          objectives: objectiveBatch,
        );
        final TrackedQuest secondQuest = TrackedQuest(
          questId: 2,
          title: 'Quest 2',
          type: 0,
          objectives: objectiveBatch.sublist(0, 512),
        );

        expect(
          () => TrackedQuestsState(
            quests: <TrackedQuest>[firstQuest, secondQuest],
          ),
          throwsFormatException,
        );
      },
    );

    test('Factory TrackedQuestsState exposes an immutable quest list', () {
      final TrackedQuestsState state = TrackedQuestsState.fromJson(
        _readTrackedQuestsFixtureData(
              'state-snapshot-tracked-quests.json',
            )['value']
            as JsonMap,
      );

      expect(() => state.quests.clear(), throwsUnsupportedError);
    });
  });
}
