import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.reducer.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises domain-by-domain live-state Redux reduction.
void main() {
  group('Action Object behaves correctly', () {
    test(
      'Object returns the same live-state slice for an unhandled action',
      () {
        const SessionLiveState state = SessionLiveState.initial();

        expect(identical(liveStateReducer(state, Object()), state), isTrue);
      },
    );
  });

  group(
    'Action CharacterVitalsSynchronizationChangedAction behaves correctly',
    () {
      test(
        'CharacterVitalsSynchronizationChangedAction stores one coherent snapshot',
        () {
          const SessionLiveState state = SessionLiveState.initial();
          const CharacterVitalsSynchronizationChangedAction action =
              CharacterVitalsSynchronizationChangedAction(
                LiveDomainState<
                  ({
                    ({double current, double max}) health,
                    ({double current, double max}) magicka,
                    ({double current, double max}) stamina,
                  })
                >(
                  status: LiveStateStatus.synchronized,
                  value: (
                    health: (current: 201, max: 190),
                    magicka: (current: 42, max: 120),
                    stamina: (current: 88, max: 100),
                  ),
                  stateAuthorityId: 'authority-a',
                  playContextId: 'context-a',
                  revision: 1,
                ),
              );

          final SessionLiveState result = liveStateReducer(state, action);

          expect(result.characterVitals, action.synchronization);
          expect(result.characterVitals.value?.health.max, 190);
          expect(result.characterVitals.status, LiveStateStatus.synchronized);
        },
      );
    },
  );

  group('Action CharacterXpSynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterXpSynchronizationChangedAction keeps stale value and status',
      () {
        const SessionLiveState state = SessionLiveState.initial();
        const CharacterXpSynchronizationChangedAction stale =
            CharacterXpSynchronizationChangedAction(
              LiveDomainState<double?>(
                status: LiveStateStatus.stale,
                value: 71.25,
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 2,
              ),
            );

        final SessionLiveState staleState = liveStateReducer(state, stale);
        const CharacterXpSynchronizationChangedAction fresh =
            CharacterXpSynchronizationChangedAction(
              LiveDomainState<double?>(
                status: LiveStateStatus.synchronized,
                value: 74.5,
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 3,
              ),
            );

        final SessionLiveState recoveredState = liveStateReducer(
          staleState,
          fresh,
        );

        expect(staleState.characterXp.status, LiveStateStatus.stale);
        expect(staleState.characterXp.value, 71.25);
        expect(recoveredState.characterXp.status, LiveStateStatus.synchronized);
        expect(recoveredState.characterXp.value, 74.5);
        expect(recoveredState.characterXp.revision, 3);
      },
    );
  });

  group(
    'Action CharacterLevelSynchronizationChangedAction behaves correctly',
    () {
      test('CharacterLevelSynchronizationChangedAction stores unavailable', () {
        const SessionLiveState state = SessionLiveState.initial();
        const CharacterLevelSynchronizationChangedAction action =
            CharacterLevelSynchronizationChangedAction(
              LiveDomainState<int?>(
                status: LiveStateStatus.unavailable,
                value: null,
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 2,
              ),
            );

        final SessionLiveState result = liveStateReducer(state, action);

        expect(result.characterLevel.status, LiveStateStatus.unavailable);
        expect(result.characterLevel.value, isNull);
        expect(result.characterLevel.revision, 2);
      });
    },
  );

  group(
    'Action CharacterIdentitySynchronizationChangedAction behaves correctly',
    () {
      test(
        'CharacterIdentitySynchronizationChangedAction stores both identity fields',
        () {
          const SessionLiveState state = SessionLiveState.initial();
          const CharacterIdentitySynchronizationChangedAction action =
              CharacterIdentitySynchronizationChangedAction(
                LiveDomainState<({String name, String race})?>(
                  status: LiveStateStatus.synchronized,
                  value: (name: 'Player', race: 'Nord'),
                  stateAuthorityId: 'authority-a',
                  playContextId: 'context-a',
                  revision: 3,
                ),
              );

          final SessionLiveState result = liveStateReducer(state, action);

          expect(result.characterIdentity.value?.name, 'Player');
          expect(result.characterIdentity.value?.race, 'Nord');
        },
      );
    },
  );

  group(
    'Action CharacterSupernaturalTraitsSynchronizationChangedAction behaves correctly',
    () {
      test(
        'CharacterSupernaturalTraitsSynchronizationChangedAction preserves all false',
        () {
          const SessionLiveState state = SessionLiveState.initial();
          const CharacterSupernaturalTraitsSynchronizationChangedAction action =
              CharacterSupernaturalTraitsSynchronizationChangedAction(
                LiveDomainState<
                  ({
                    bool isVampire,
                    bool hasVampireLordForm,
                    bool hasWerewolfForm,
                  })
                >(
                  status: LiveStateStatus.synchronized,
                  value: (
                    isVampire: false,
                    hasVampireLordForm: false,
                    hasWerewolfForm: false,
                  ),
                  stateAuthorityId: 'authority-a',
                  playContextId: 'context-a',
                  revision: 4,
                ),
              );

          final SessionLiveState result = liveStateReducer(state, action);

          expect(
            result.supernaturalTraits.status,
            LiveStateStatus.synchronized,
          );
          expect(result.supernaturalTraits.value?.isVampire, isFalse);
          expect(result.supernaturalTraits.value?.hasVampireLordForm, isFalse);
          expect(result.supernaturalTraits.value?.hasWerewolfForm, isFalse);
        },
      );
    },
  );

  group(
    'Action PlayerLocationSynchronizationChangedAction behaves correctly',
    () {
      test(
        'PlayerLocationSynchronizationChangedAction keeps nullable subfields',
        () {
          const SessionLiveState state = SessionLiveState.initial();
          const PlayerLocationSynchronizationChangedAction action =
              PlayerLocationSynchronizationChangedAction(
                LiveDomainState<
                  ({
                    int cellId,
                    LiveCellKind cellKind,
                    String? cellName,
                    int? locationId,
                    String? locationName,
                    int? worldspaceId,
                    String? worldspaceName,
                  })?
                >(
                  status: LiveStateStatus.synchronized,
                  value: (
                    cellId: 22,
                    cellKind: LiveCellKind.exterior,
                    cellName: null,
                    locationId: null,
                    locationName: null,
                    worldspaceId: 1,
                    worldspaceName: 'Worldspace',
                  ),
                  stateAuthorityId: 'authority-a',
                  playContextId: 'context-a',
                  revision: 5,
                ),
              );

          final SessionLiveState result = liveStateReducer(state, action);

          expect(result.playerLocation.value?.cellName, isNull);
          expect(result.playerLocation.value?.locationName, isNull);
          expect(result.playerLocation.value?.worldspaceName, 'Worldspace');
        },
      );
    },
  );

  group('Action GameTimeSynchronizationChangedAction behaves correctly', () {
    test(
      'GameTimeSynchronizationChangedAction keeps Skyrim calendar shape',
      () {
        const SessionLiveState state = SessionLiveState.initial();
        const GameTimeSynchronizationChangedAction action =
            GameTimeSynchronizationChangedAction(
              LiveDomainState<
                ({
                  int year,
                  int month,
                  String monthName,
                  int day,
                  int hour,
                  int minute,
                })?
              >(
                status: LiveStateStatus.synchronized,
                value: (
                  year: 4,
                  month: 8,
                  monthName: 'Last Seed',
                  day: 12,
                  hour: 14,
                  minute: 30,
                ),
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 6,
              ),
            );

        final SessionLiveState result = liveStateReducer(state, action);

        expect(result.gameTime.value?.year, 4);
        expect(result.gameTime.value?.monthName, 'Last Seed');
        expect(result.gameTime.value?.minute, 30);
      },
    );
  });

  group('Action TrackedQuestsSynchronizationChangedAction behaves correctly', () {
    test(
      'TrackedQuestsSynchronizationChangedAction stores synchronized empty collection',
      () {
        const SessionLiveState state = SessionLiveState.initial();
        const TrackedQuestsSynchronizationChangedAction action =
            TrackedQuestsSynchronizationChangedAction(
              LiveDomainState<List<LiveTrackedQuest>>(
                status: LiveStateStatus.synchronized,
                value: <LiveTrackedQuest>[],
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 7,
              ),
            );

        final SessionLiveState result = liveStateReducer(state, action);

        expect(result.trackedQuests.status, LiveStateStatus.synchronized);
        expect(result.trackedQuests.value, isEmpty);
      },
    );

    test(
      'TrackedQuestsSynchronizationChangedAction retains every tracked quest',
      () {
        const SessionLiveState state = SessionLiveState.initial();
        final TrackedQuestsSynchronizationChangedAction action =
            TrackedQuestsSynchronizationChangedAction(
              LiveDomainState<List<LiveTrackedQuest>>(
                status: LiveStateStatus.synchronized,
                value: [
                  Fixtures.buildLiveTrackedQuest(questId: 1),
                  Fixtures.buildLiveTrackedQuest(questId: 2),
                ],
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 7,
              ),
            );

        final SessionLiveState result = liveStateReducer(state, action);

        expect(result.trackedQuests.value, hasLength(2));
        expect(result.trackedQuests.value?.map((quest) => quest.questId), [
          1,
          2,
        ]);
      },
    );
  });

  group('Action SessionLiveStateResetAction behaves correctly', () {
    test(
      'SessionLiveStateResetAction resets all domains after termination',
      () {
        const SessionLiveState active = SessionLiveState(
          characterVitals: LiveDomainState.notSubscribed(),
          characterXp: LiveDomainState<double?>(
            status: LiveStateStatus.stale,
            value: 54,
            stateAuthorityId: 'authority-a',
            playContextId: 'context-a',
            revision: 7,
          ),
          characterLevel: LiveDomainState.notSubscribed(),
          characterIdentity: LiveDomainState.notSubscribed(),
          supernaturalTraits: LiveDomainState.notSubscribed(),
          playerLocation: LiveDomainState.notSubscribed(),
          gameTime: LiveDomainState.notSubscribed(),
          trackedQuests: LiveDomainState.notSubscribed(),
        );

        final SessionLiveState result = liveStateReducer(
          active,
          const SessionLiveStateResetAction(),
        );

        expect(result, const SessionLiveState.initial());
        expect(result.characterXp.status, LiveStateStatus.notSubscribed);
        expect(result.characterXp.value, isNull);
      },
    );
  });
}
