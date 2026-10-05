import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/quest_objective.dart';
import 'package:dovahlink_client_sdk/src/state/tracked_quest.dart';

/// Builds one objective object nested in a quest test value.
/// @param index The authored objective index.
/// @param instanceId The owning quest-instance identifier.
/// @return A fresh protocol-shaped objective object.
JsonMap _buildObjectiveJson({int index = 20, int instanceId = 5}) =>
    <String, dynamic>{
      'index': index,
      'instanceId': instanceId,
      'text': 'Objective $index',
      'state': 'displayed',
    };

/// Builds one raw tracked quest object for decoder tests.
/// @param questId The runtime quest FormID.
/// @param title The localized quest title.
/// @param type The raw Skyrim quest type.
/// @param objectives The protocol-shaped objective list.
/// @return A fresh protocol-shaped quest object.
JsonMap _buildQuestJson({
  Object? questId = 10,
  Object? title = 'Missão localizada',
  Object? type = 254,
  Object? objectives,
}) => <String, dynamic>{
  'questId': questId,
  'title': title,
  'type': type,
  'objectives': objectives ?? <JsonMap>[_buildObjectiveJson()],
};

/// Tests decoding, immutability, and bounds for one tracked quest.
void main() {
  group('Factory fromJson behaves correctly', () {
    test('Factory fromJson preserves localized quest and objective facts', () {
      final TrackedQuest quest = TrackedQuest.fromJson(
        _buildQuestJson(
          objectives: <JsonMap>[
            _buildObjectiveJson(index: 30),
            _buildObjectiveJson(index: 10),
          ],
        ),
      );

      expect(quest.questId, 10);
      expect(quest.title, 'Missão localizada');
      expect(quest.type, 254);
      expect(quest.objectives.map((QuestObjective item) => item.index), [
        30,
        10,
      ]);
      expect(
        quest.objectives.first.state,
        TrackedQuestObjectiveState.displayed,
      );
    });

    test(
      'Factory fromJson keeps objective order and returns an immutable list',
      () {
        final TrackedQuest quest = TrackedQuest.fromJson(
          _buildQuestJson(
            objectives: <JsonMap>[
              _buildObjectiveJson(index: 30),
              _buildObjectiveJson(index: 10),
            ],
          ),
        );

        expect(quest.objectives.map((QuestObjective item) => item.index), [
          30,
          10,
        ]);
        expect(
          () => quest.objectives.add(
            QuestObjective.fromJson(_buildObjectiveJson(index: 40)),
          ),
          throwsUnsupportedError,
        );
      },
    );

    test('Factory fromJson accepts distinct objective identity pairs', () {
      final TrackedQuest quest = TrackedQuest.fromJson(
        _buildQuestJson(
          objectives: <JsonMap>[
            _buildObjectiveJson(index: 10, instanceId: 1),
            _buildObjectiveJson(index: 10, instanceId: 2),
            _buildObjectiveJson(index: 20, instanceId: 1),
          ],
        ),
      );

      expect(quest.objectives, hasLength(3));
    });

    test('Factory fromJson rejects duplicate objective instances', () {
      expect(
        () => TrackedQuest.fromJson(
          _buildQuestJson(
            objectives: <JsonMap>[
              _buildObjectiveJson(index: 10),
              _buildObjectiveJson(index: 10),
            ],
          ),
        ),
        throwsA(isA<ProtocolFormatException>()),
      );
    });

    test('Factory fromJson rejects malformed or out-of-bounds quest facts', () {
      final JsonMap missingTitle = _buildQuestJson()..remove('title');
      final String oversizedTitle = List<String>.filled(64, 'é').join();

      for (final JsonMap malformed in <JsonMap>[
        missingTitle,
        _buildQuestJson(questId: 0),
        _buildQuestJson(questId: 0x100000000),
        _buildQuestJson(title: ''),
        _buildQuestJson(title: oversizedTitle),
        _buildQuestJson(title: 'bad\u0000title'),
        _buildQuestJson(type: -1),
        _buildQuestJson(type: 256),
        _buildQuestJson(objectives: 'not-a-list'),
      ]) {
        expect(
          () => TrackedQuest.fromJson(malformed),
          throwsA(isA<ProtocolFormatException>()),
        );
      }
    });

    test(
      'Factory fromJson accepts the maximum quest ID, type, and title bytes',
      () {
        final String title = List<String>.filled(126, 'x').join();
        final TrackedQuest quest = TrackedQuest.fromJson(
          _buildQuestJson(questId: 0xFFFFFFFF, title: title, type: 0xFF),
        );

        expect(quest.questId, 0xFFFFFFFF);
        expect(quest.title, title);
        expect(quest.type, 0xFF);
      },
    );

    test(
      'Factory fromJson accepts the maximum objective count for one quest',
      () {
        final List<JsonMap> objectives = List<JsonMap>.generate(
          1024,
          (int index) => _buildObjectiveJson(index: index, instanceId: 1),
        );

        expect(
          TrackedQuest.fromJson(
            _buildQuestJson(objectives: objectives),
          ).objectives,
          hasLength(1024),
        );
      },
    );
  });
}
