import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.reducer.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';

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

/// Exercises that reducers replace synchronization objects without mapping them.
void main() {
  const SessionLiveState initial = SessionLiveState.initial();

  group(
    'Action CharacterVitalsSynchronizationChangedAction behaves correctly',
    () {
      test('CharacterVitalsSynchronizationChangedAction stores its value', () {
        final StateSynchronization<CharacterVitalsState> synchronization =
            _synchronization<CharacterVitalsState>(
              DovahLinkStateStatus.synchronized,
              CharacterVitalsState(
                health: const CharacterVital(current: 80, max: 100),
                magicka: const CharacterVital(current: 40, max: 80),
                stamina: const CharacterVital(current: 50, max: 90),
              ),
            );

        final SessionLiveState result = liveStateReducer(
          initial,
          CharacterVitalsSynchronizationChangedAction(synchronization),
        );

        expect(identical(result.characterVitals, synchronization), isTrue);
      });
    },
  );

  group('Action CharacterXpSynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterXpSynchronizationChangedAction stores unavailable state',
      () {
        final StateSynchronization<CharacterXpState> synchronization =
            _synchronization<CharacterXpState>(
              DovahLinkStateStatus.unavailable,
              const CharacterXpState(value: null),
            );

        final SessionLiveState result = liveStateReducer(
          initial,
          CharacterXpSynchronizationChangedAction(synchronization),
        );

        expect(identical(result.characterXp, synchronization), isTrue);
        expect(result.characterXp.status, DovahLinkStateStatus.unavailable);
      },
    );
  });

  group(
    'Action CharacterLevelSynchronizationChangedAction behaves correctly',
    () {
      test('CharacterLevelSynchronizationChangedAction stores its value', () {
        final StateSynchronization<CharacterLevelState> synchronization =
            _synchronization<CharacterLevelState>(
              DovahLinkStateStatus.stale,
              const CharacterLevelState(value: 43),
            );

        final SessionLiveState result = liveStateReducer(
          initial,
          CharacterLevelSynchronizationChangedAction(synchronization),
        );

        expect(identical(result.characterLevel, synchronization), isTrue);
      });
    },
  );

  group(
    'Action CharacterIdentitySynchronizationChangedAction behaves correctly',
    () {
      test(
        'CharacterIdentitySynchronizationChangedAction preserves SDK identity',
        () {
          const CharacterIdentityState identity = CharacterIdentityState(
            name: 'Player',
            race: 'Nord',
          );
          final StateSynchronization<CharacterIdentityState?> synchronization =
              _synchronization<CharacterIdentityState?>(
                DovahLinkStateStatus.synchronized,
                identity,
              );

          final SessionLiveState result = liveStateReducer(
            initial,
            CharacterIdentitySynchronizationChangedAction(synchronization),
          );

          expect(identical(result.characterIdentity, synchronization), isTrue);
          expect(identical(result.characterIdentity.value, identity), isTrue);
        },
      );
    },
  );

  group(
    'Action CharacterSupernaturalTraitsSynchronizationChangedAction behaves correctly',
    () {
      test(
        'CharacterSupernaturalTraitsSynchronizationChangedAction stores its value',
        () {
          const CharacterSupernaturalTraitsState traits =
              CharacterSupernaturalTraitsState(
                isVampire: false,
                hasVampireLordForm: false,
                hasWerewolfForm: false,
              );
          final StateSynchronization<CharacterSupernaturalTraitsState?>
          synchronization = _synchronization<CharacterSupernaturalTraitsState?>(
            DovahLinkStateStatus.synchronized,
            traits,
          );

          final SessionLiveState result = liveStateReducer(
            initial,
            CharacterSupernaturalTraitsSynchronizationChangedAction(
              synchronization,
            ),
          );

          expect(identical(result.supernaturalTraits, synchronization), isTrue);
          expect(identical(result.supernaturalTraits.value, traits), isTrue);
        },
      );
    },
  );

  group(
    'Action PlayerLocationSynchronizationChangedAction behaves correctly',
    () {
      test('PlayerLocationSynchronizationChangedAction stores its value', () {
        const PlayerLocationState location = PlayerLocationState(
          cellId: 1,
          cellKind: PlayerLocationCellKind.exterior,
          cellName: null,
          locationId: null,
          locationName: null,
          worldspaceId: null,
          worldspaceName: null,
        );
        final StateSynchronization<PlayerLocationState?> synchronization =
            _synchronization<PlayerLocationState?>(
              DovahLinkStateStatus.synchronized,
              location,
            );

        final SessionLiveState result = liveStateReducer(
          initial,
          PlayerLocationSynchronizationChangedAction(synchronization),
        );

        expect(identical(result.playerLocation, synchronization), isTrue);
      });
    },
  );

  group('Action GameTimeSynchronizationChangedAction behaves correctly', () {
    test('GameTimeSynchronizationChangedAction stores its value', () {
      const GameTimeState time = GameTimeState(
        year: 4,
        month: 8,
        monthName: 'Last Seed',
        day: 12,
        hour: 14,
        minute: 30,
      );
      final StateSynchronization<GameTimeState?> synchronization =
          _synchronization<GameTimeState?>(
            DovahLinkStateStatus.synchronized,
            time,
          );

      final SessionLiveState result = liveStateReducer(
        initial,
        GameTimeSynchronizationChangedAction(synchronization),
      );

      expect(identical(result.gameTime, synchronization), isTrue);
    });
  });

  group(
    'Action TrackedQuestsSynchronizationChangedAction behaves correctly',
    () {
      test(
        'TrackedQuestsSynchronizationChangedAction stores complete quests',
        () {
          final TrackedQuestsState quests = TrackedQuestsState(
            quests: [
              TrackedQuest(
                questId: 1,
                title: 'Test Quest',
                type: 0,
                objectives: [
                  QuestObjective(
                    index: 0,
                    instanceId: 1,
                    text: 'Complete the objective',
                    state: TrackedQuestObjectiveState.displayed,
                  ),
                ],
              ),
            ],
          );
          final StateSynchronization<TrackedQuestsState?> synchronization =
              _synchronization<TrackedQuestsState?>(
                DovahLinkStateStatus.synchronized,
                quests,
              );

          final SessionLiveState result = liveStateReducer(
            initial,
            TrackedQuestsSynchronizationChangedAction(synchronization),
          );

          expect(identical(result.trackedQuests, synchronization), isTrue);
          expect(identical(result.trackedQuests.value, quests), isTrue);
          expect(result.trackedQuests.value?.quests.single.title, 'Test Quest');
        },
      );
    },
  );

  group('Action SessionLiveStateResetAction behaves correctly', () {
    test('SessionLiveStateResetAction clears a populated slice', () {
      const StateSynchronization<CharacterXpState> xp =
          StateSynchronization<CharacterXpState>(
            status: DovahLinkStateStatus.synchronized,
            value: CharacterXpState(value: 42),
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 1,
          );
      final SessionLiveState populated = initial.copyWith(characterXp: xp);

      expect(
        liveStateReducer(populated, const SessionLiveStateResetAction()),
        const SessionLiveState.initial(),
      );
    });
  });

  group('Nullable SDK synchronization behaves correctly', () {
    test('reducers preserve explicit unavailable nullable domain values', () {
      const StateSynchronization<CharacterIdentityState?> identity =
          StateSynchronization<CharacterIdentityState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 1,
          );
      const StateSynchronization<CharacterSupernaturalTraitsState?> traits =
          StateSynchronization<CharacterSupernaturalTraitsState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 2,
          );
      const StateSynchronization<PlayerLocationState?> location =
          StateSynchronization<PlayerLocationState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 3,
          );
      const StateSynchronization<GameTimeState?> time =
          StateSynchronization<GameTimeState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 4,
          );
      const StateSynchronization<TrackedQuestsState?> quests =
          StateSynchronization<TrackedQuestsState?>(
            status: DovahLinkStateStatus.unavailable,
            value: null,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 5,
          );

      SessionLiveState result = liveStateReducer(
        initial,
        const CharacterIdentitySynchronizationChangedAction(identity),
      );
      result = liveStateReducer(
        result,
        const CharacterSupernaturalTraitsSynchronizationChangedAction(traits),
      );
      result = liveStateReducer(
        result,
        const PlayerLocationSynchronizationChangedAction(location),
      );
      result = liveStateReducer(
        result,
        const GameTimeSynchronizationChangedAction(time),
      );
      result = liveStateReducer(
        result,
        const TrackedQuestsSynchronizationChangedAction(quests),
      );

      expect(result.characterIdentity, same(identity));
      expect(result.supernaturalTraits, same(traits));
      expect(result.playerLocation, same(location));
      expect(result.gameTime, same(time));
      expect(result.trackedQuests, same(quests));
      expect(result.trackedQuests.value, isNull);
    });
  });

  group('Action Object behaves correctly', () {
    test(
      'liveStateReducer returns the current slice for an unknown action',
      () {
        expect(identical(liveStateReducer(initial, Object()), initial), isTrue);
      },
    );
  });
}

/// Creates synchronization metadata around the exact supplied SDK domain value.
StateSynchronization<T> _synchronization<T>(
  DovahLinkStateStatus status,
  T? value,
) => StateSynchronization<T>(
  status: status,
  value: value,
  stateAuthorityId: 'authority-a',
  playContextId: 'context-a',
  revision: 1,
);
