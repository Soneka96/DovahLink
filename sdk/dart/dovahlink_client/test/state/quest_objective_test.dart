import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/quest_objective.dart';

/// Builds one raw objective object for decoder tests.
/// @param index The authored objective index.
/// @param instanceId The owning quest-instance identifier.
/// @param text The localized text or explicit absence.
/// @param state The canonical objective-state value.
/// @return A fresh protocol-shaped objective object.
JsonMap _buildObjectiveJson({
  Object? index = 20,
  Object? instanceId = 5,
  Object? text = 'Objetivo localizado',
  Object? state = 'displayed',
}) => <String, dynamic>{
  'index': index,
  'instanceId': instanceId,
  'text': text,
  'state': state,
};

/// Tests decoding and bounds for typed tracked-quest objectives.
void main() {
  group('Factory QuestObjective behaves correctly', () {
    test(
      'Factory QuestObjective rejects numeric and text values outside bounds',
      () {
        for (final QuestObjective Function() buildMalformed
            in <QuestObjective Function()>[
              () => QuestObjective(
                index: -1,
                instanceId: 0,
                text: null,
                state: TrackedQuestObjectiveState.dormant,
              ),
              () => QuestObjective(
                index: 0x10000,
                instanceId: 0,
                text: null,
                state: TrackedQuestObjectiveState.dormant,
              ),
              () => QuestObjective(
                index: 0,
                instanceId: -1,
                text: null,
                state: TrackedQuestObjectiveState.dormant,
              ),
              () => QuestObjective(
                index: 0,
                instanceId: 0x100000000,
                text: null,
                state: TrackedQuestObjectiveState.dormant,
              ),
              () => QuestObjective(
                index: 0,
                instanceId: 0,
                text: List<String>.filled(64, 'é').join(),
                state: TrackedQuestObjectiveState.dormant,
              ),
              () => QuestObjective(
                index: 0,
                instanceId: 0,
                text: 'bad\u0000text',
                state: TrackedQuestObjectiveState.dormant,
              ),
            ]) {
          expect(buildMalformed, throwsFormatException);
        }
      },
    );
  });

  group('Factory fromJson behaves correctly', () {
    test(
      'Factory fromJson preserves objective identity and localized text',
      () {
        final QuestObjective objective = QuestObjective.fromJson(
          _buildObjectiveJson(),
        );

        expect(objective.index, 20);
        expect(objective.instanceId, 5);
        expect(objective.text, 'Objetivo localizado');
        expect(objective.state, TrackedQuestObjectiveState.displayed);
      },
    );

    test('Factory fromJson decodes all engine objective states', () {
      const List<(String, TrackedQuestObjectiveState)> states =
          <(String, TrackedQuestObjectiveState)>[
            ('dormant', TrackedQuestObjectiveState.dormant),
            ('displayed', TrackedQuestObjectiveState.displayed),
            ('completed', TrackedQuestObjectiveState.completed),
            (
              'completed_and_displayed',
              TrackedQuestObjectiveState.completedAndDisplayed,
            ),
            ('failed', TrackedQuestObjectiveState.failed),
            (
              'failed_and_displayed',
              TrackedQuestObjectiveState.failedAndDisplayed,
            ),
          ];

      for (final (String wireValue, TrackedQuestObjectiveState expected)
          in states) {
        expect(
          QuestObjective.fromJson(_buildObjectiveJson(state: wireValue)).state,
          expected,
        );
      }
    });

    test('Factory fromJson preserves a null objective text', () {
      expect(
        QuestObjective.fromJson(_buildObjectiveJson(text: null)).text,
        isNull,
      );
    });

    test('Factory fromJson accepts the exact UTF-8 text bound', () {
      final String text = List<String>.filled(63, 'é').join();

      expect(
        QuestObjective.fromJson(_buildObjectiveJson(text: text)).text,
        text,
      );
    });

    test('Factory fromJson accepts minimum and maximum numeric bounds', () {
      final QuestObjective minimum = QuestObjective.fromJson(
        _buildObjectiveJson(index: 0, instanceId: 0, text: null),
      );
      final QuestObjective maximum = QuestObjective.fromJson(
        _buildObjectiveJson(index: 0xFFFF, instanceId: 0xFFFFFFFF, text: null),
      );

      expect((minimum.index, minimum.instanceId), (0, 0));
      expect((maximum.index, maximum.instanceId), (0xFFFF, 0xFFFFFFFF));
    });

    test('Factory fromJson rejects malformed or out-of-bounds fields', () {
      final JsonMap missingState = _buildObjectiveJson()..remove('state');
      final String oversizedText = List<String>.filled(64, 'é').join();

      for (final JsonMap malformed in <JsonMap>[
        missingState,
        _buildObjectiveJson(index: -1),
        _buildObjectiveJson(index: 0x10000),
        _buildObjectiveJson(instanceId: -1),
        _buildObjectiveJson(instanceId: 0x100000000),
        _buildObjectiveJson(text: oversizedText),
        _buildObjectiveJson(text: 'bad\u0000text'),
        _buildObjectiveJson(state: 'optional'),
      ]) {
        expect(
          () => QuestObjective.fromJson(malformed),
          throwsA(isA<ProtocolFormatException>()),
        );
      }
    });
  });
}
