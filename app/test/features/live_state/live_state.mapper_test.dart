import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/live_state.mapper.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterLevelState,
        CharacterSupernaturalTraitsState,
        CharacterVital,
        CharacterVitalsState,
        CharacterXpState,
        DovahLinkStateStatus,
        GameTimeState,
        PlayerLocationCellKind,
        PlayerLocationState,
        QuestObjective,
        StateSynchronization,
        TrackedQuest,
        TrackedQuestObjectiveState,
        TrackedQuestsState;

/// Exercises mapping from public SDK values into app-owned live state.
void main() {
  group('characterVitals behaves correctly', () {
    test('characterVitals preserves raw coherent resource values', () {
      final CharacterVitalsState value = CharacterVitalsState(
        health: const CharacterVital(current: 205, max: 190),
        magicka: const CharacterVital(current: 42, max: 120),
        stamina: const CharacterVital(current: 91, max: 88),
      );
      final StateSynchronization<CharacterVitalsState> synchronization =
          StateSynchronization<CharacterVitalsState>(
            status: DovahLinkStateStatus.stale,
            value: value,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 17,
          );
      final result = LiveStateMapper.characterVitals(synchronization);

      expect(result.status, LiveStateStatus.stale);
      expect(result.value?.health.current, 205);
      expect(result.value?.health.max, 190);
      expect(result.value?.stamina.current, 91);
      expect(result.revision, 17);
      expect(result.stateAuthorityId, 'authority-a');
      expect(result.playContextId, 'context-a');
    });

    test('characterVitals maps an unavailable capture to a null app value', () {
      final CharacterVitalsState value = CharacterVitalsState(
        health: null,
        magicka: null,
        stamina: null,
      );
      final result = LiveStateMapper.characterVitals(
        StateSynchronization<CharacterVitalsState>(
          status: DovahLinkStateStatus.unavailable,
          value: value,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 18,
        ),
      );

      expect(result.status, LiveStateStatus.unavailable);
      expect(result.value, isNull);
    });
  });

  group('characterXp behaves correctly', () {
    test('characterXp maps the exact numeric SDK value', () {
      final result = LiveStateMapper.characterXp(
        const StateSynchronization<CharacterXpState>(
          status: DovahLinkStateStatus.synchronized,
          value: CharacterXpState(value: 57.25),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 1,
        ),
      );

      expect(result.value, 57.25);
      expect(result.status, LiveStateStatus.synchronized);
    });

    test('characterXp distinguishes an unavailable value from status', () {
      final result = LiveStateMapper.characterXp(
        const StateSynchronization<CharacterXpState>(
          status: DovahLinkStateStatus.unavailable,
          value: CharacterXpState(value: null),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 2,
        ),
      );

      expect(result.value, isNull);
      expect(result.status, LiveStateStatus.unavailable);
    });
  });

  group('characterLevel behaves correctly', () {
    test('characterLevel maps the exact nullable level', () {
      final result = LiveStateMapper.characterLevel(
        const StateSynchronization<CharacterLevelState>(
          status: DovahLinkStateStatus.synchronized,
          value: CharacterLevelState(value: 43),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 3,
        ),
      );

      expect(result.value, 43);
      expect(result.status, LiveStateStatus.synchronized);
    });
  });

  group('characterIdentity behaves correctly', () {
    test('characterIdentity maps name and identity race together', () {
      final result = LiveStateMapper.characterIdentity(
        const StateSynchronization<CharacterIdentityState?>(
          status: DovahLinkStateStatus.synchronized,
          value: CharacterIdentityState(name: 'Player', race: 'Nord'),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 4,
        ),
      );

      expect(result.value?.name, 'Player');
      expect(result.value?.race, 'Nord');
    });

    test('characterIdentity preserves explicit unavailability', () {
      final result = LiveStateMapper.characterIdentity(
        const StateSynchronization<CharacterIdentityState?>(
          status: DovahLinkStateStatus.unavailable,
          value: null,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 5,
        ),
      );

      expect(result.status, LiveStateStatus.unavailable);
      expect(result.value, isNull);
    });
  });

  group('supernaturalTraits behaves correctly', () {
    test(
      'supernaturalTraits preserves independent false and true predicates',
      () {
        final result = LiveStateMapper.supernaturalTraits(
          const StateSynchronization<CharacterSupernaturalTraitsState?>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterSupernaturalTraitsState(
              isVampire: false,
              hasVampireLordForm: true,
              hasWerewolfForm: true,
            ),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 6,
          ),
        );

        expect(result.value?.isVampire, isFalse);
        expect(result.value?.hasVampireLordForm, isTrue);
        expect(result.value?.hasWerewolfForm, isTrue);
      },
    );
  });

  group('playerLocation behaves correctly', () {
    test('playerLocation preserves every distinct fact and nullable name', () {
      final result = LiveStateMapper.playerLocation(
        const StateSynchronization<PlayerLocationState?>(
          status: DovahLinkStateStatus.synchronized,
          value: PlayerLocationState(
            cellId: 21,
            cellKind: PlayerLocationCellKind.interior,
            cellName: null,
            locationId: 22,
            locationName: 'Location',
            worldspaceId: null,
            worldspaceName: null,
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 7,
        ),
      );

      expect(result.value?.cellId, 21);
      expect(result.value?.cellKind, LiveCellKind.interior);
      expect(result.value?.cellName, isNull);
      expect(result.value?.locationId, 22);
      expect(result.value?.worldspaceId, isNull);
    });

    test('playerLocation maps exterior cells separately', () {
      final result = LiveStateMapper.playerLocation(
        const StateSynchronization<PlayerLocationState?>(
          status: DovahLinkStateStatus.synchronized,
          value: PlayerLocationState(
            cellId: 21,
            cellKind: PlayerLocationCellKind.exterior,
            cellName: null,
            locationId: null,
            locationName: null,
            worldspaceId: 22,
            worldspaceName: 'Worldspace',
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 7,
        ),
      );

      expect(result.value?.cellKind, LiveCellKind.exterior);
      expect(result.value?.worldspaceName, 'Worldspace');
    });
  });

  group('gameTime behaves correctly', () {
    test('gameTime maps Skyrim calendar fields without date conversion', () {
      final result = LiveStateMapper.gameTime(
        const StateSynchronization<GameTimeState?>(
          status: DovahLinkStateStatus.recovering,
          value: GameTimeState(
            year: 4,
            month: 8,
            monthName: 'Last Seed',
            day: 12,
            hour: 14,
            minute: 30,
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 8,
        ),
      );

      expect(result.status, LiveStateStatus.recovering);
      expect(result.value?.year, 4);
      expect(result.value?.monthName, 'Last Seed');
      expect(result.value?.minute, 30);
    });
  });

  group('trackedQuests behaves correctly', () {
    test('trackedQuests keeps all quests and objective instances in order', () {
      final TrackedQuestsState value = TrackedQuestsState(
        quests: <TrackedQuest>[
          TrackedQuest(
            questId: 1,
            title: 'First quest',
            type: 7,
            objectives: <QuestObjective>[
              QuestObjective(
                index: 2,
                instanceId: 21,
                text: 'Current objective',
                state: TrackedQuestObjectiveState.completedAndDisplayed,
              ),
              QuestObjective(
                index: 3,
                instanceId: 22,
                text: null,
                state: TrackedQuestObjectiveState.dormant,
              ),
            ],
          ),
          TrackedQuest(
            questId: 2,
            title: 'Second quest',
            type: 9,
            objectives: <QuestObjective>[],
          ),
        ],
      );
      final result = LiveStateMapper.trackedQuests(
        StateSynchronization<TrackedQuestsState?>(
          status: DovahLinkStateStatus.synchronized,
          value: value,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 9,
        ),
      );

      expect(result.value, hasLength(2));
      expect(result.value?[0].questId, 1);
      expect(result.value?[0].type, 7);
      expect(result.value?[0].objectives, [
        (
          index: 2,
          instanceId: 21,
          text: 'Current objective',
          status: LiveQuestObjectiveStatus.completedAndDisplayed,
        ),
        (
          index: 3,
          instanceId: 22,
          text: null,
          status: LiveQuestObjectiveStatus.dormant,
        ),
      ]);
      expect(result.value?[1].questId, 2);
    });

    test(
      'trackedQuests preserves synchronized empty separately from unavailable',
      () {
        final result = LiveStateMapper.trackedQuests(
          StateSynchronization<TrackedQuestsState?>(
            status: DovahLinkStateStatus.synchronized,
            value: TrackedQuestsState(quests: <TrackedQuest>[]),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 10,
          ),
        );

        expect(result.status, LiveStateStatus.synchronized);
        expect(result.value, isEmpty);
        expect(() => result.value?.clear(), throwsUnsupportedError);
      },
    );

    test('trackedQuests maps every objective status independently', () {
      const List<TrackedQuestObjectiveState> sdkStates = [
        TrackedQuestObjectiveState.dormant,
        TrackedQuestObjectiveState.displayed,
        TrackedQuestObjectiveState.completed,
        TrackedQuestObjectiveState.completedAndDisplayed,
        TrackedQuestObjectiveState.failed,
        TrackedQuestObjectiveState.failedAndDisplayed,
      ];
      final TrackedQuestsState value = TrackedQuestsState(
        quests: <TrackedQuest>[
          TrackedQuest(
            questId: 1,
            title: 'Quest',
            type: 2,
            objectives: <QuestObjective>[
              for (int index = 0; index < sdkStates.length; index++)
                QuestObjective(
                  index: index,
                  instanceId: index,
                  text: null,
                  state: sdkStates[index],
                ),
            ],
          ),
        ],
      );
      final result = LiveStateMapper.trackedQuests(
        StateSynchronization<TrackedQuestsState?>(
          status: DovahLinkStateStatus.synchronized,
          value: value,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 11,
        ),
      );

      expect(
        result.value?.single.objectives.map((objective) => objective.status),
        LiveQuestObjectiveStatus.values,
      );
    });
  });

  group('nullable SDK values behave correctly', () {
    test(
      'nullable domain inputs remain unavailable values with status intact',
      () {
        final level = LiveStateMapper.characterLevel(
          const StateSynchronization<CharacterLevelState>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 12,
          ),
        );
        final traits = LiveStateMapper.supernaturalTraits(
          const StateSynchronization<CharacterSupernaturalTraitsState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 13,
          ),
        );
        final location = LiveStateMapper.playerLocation(
          const StateSynchronization<PlayerLocationState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 14,
          ),
        );
        final gameTime = LiveStateMapper.gameTime(
          const StateSynchronization<GameTimeState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 15,
          ),
        );
        final quests = LiveStateMapper.trackedQuests(
          const StateSynchronization<TrackedQuestsState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 16,
          ),
        );

        expect(level.status, LiveStateStatus.unavailable);
        expect(level.value, isNull);
        expect(traits.status, LiveStateStatus.unavailable);
        expect(traits.value, isNull);
        expect(location.status, LiveStateStatus.unavailable);
        expect(location.value, isNull);
        expect(gameTime.status, LiveStateStatus.unavailable);
        expect(gameTime.value, isNull);
        expect(quests.status, LiveStateStatus.unavailable);
        expect(quests.value, isNull);
      },
    );
  });

  group('status conversion behaves correctly', () {
    test('maps every SDK synchronization status without collapsing cases', () {
      const List<(DovahLinkStateStatus, LiveStateStatus)> expected = [
        (DovahLinkStateStatus.notSubscribed, LiveStateStatus.notSubscribed),
        (DovahLinkStateStatus.unavailable, LiveStateStatus.unavailable),
        (DovahLinkStateStatus.synchronized, LiveStateStatus.synchronized),
        (DovahLinkStateStatus.stale, LiveStateStatus.stale),
        (DovahLinkStateStatus.recovering, LiveStateStatus.recovering),
        (DovahLinkStateStatus.failed, LiveStateStatus.failed),
      ];

      for (final (DovahLinkStateStatus sdk, LiveStateStatus app) in expected) {
        final result = LiveStateMapper.characterXp(
          StateSynchronization<CharacterXpState>(
            status: sdk,
            value: const CharacterXpState(value: null),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 11,
          ),
        );

        expect(result.status, app);
      }
    });
  });
}
