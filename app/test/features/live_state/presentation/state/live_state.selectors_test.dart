import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.reducer.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.selectors.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Exercises the selectors for all projected gameplay domains.
void main() {
  group('characterVitalsSelector behaves correctly', () {
    test(
      'characterVitalsSelector returns the complete synchronized Vitals state',
      () {
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
                  health: (current: 80, max: 100),
                  magicka: (current: 20, max: 60),
                  stamina: (current: 44, max: 70),
                ),
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 1,
              ),
            );
        final SessionLiveState liveState = liveStateReducer(
          const SessionLiveState.initial(),
          action,
        );

        final synchronization = LiveStateSelectors.characterVitalsSelector(
          AppState.initial(liveState: liveState),
        );

        expect(synchronization.status, LiveStateStatus.synchronized);
        expect(synchronization.value?.health.current, 80);
        expect(synchronization.value?.health.max, 100);
        expect(synchronization.revision, 1);
      },
    );
  });

  group('characterXpSelector behaves correctly', () {
    test('characterXpSelector returns the exact XP synchronization state', () {
      const CharacterXpSynchronizationChangedAction action =
          CharacterXpSynchronizationChangedAction(
            LiveDomainState<double?>(
              status: LiveStateStatus.stale,
              value: 54.5,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 2,
            ),
          );
      final SessionLiveState liveState = liveStateReducer(
        const SessionLiveState.initial(),
        action,
      );

      final synchronization = LiveStateSelectors.characterXpSelector(
        AppState.initial(liveState: liveState),
      );

      expect(synchronization.status, LiveStateStatus.stale);
      expect(synchronization.value, 54.5);
      expect(synchronization.revision, 2);
    });
  });

  group('characterLevelSelector behaves correctly', () {
    test('characterLevelSelector returns the Level synchronization state', () {
      const CharacterLevelSynchronizationChangedAction action =
          CharacterLevelSynchronizationChangedAction(
            LiveDomainState<int?>(
              status: LiveStateStatus.synchronized,
              value: 43,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 3,
            ),
          );
      final SessionLiveState liveState = liveStateReducer(
        const SessionLiveState.initial(),
        action,
      );

      final synchronization = LiveStateSelectors.characterLevelSelector(
        AppState.initial(liveState: liveState),
      );

      expect(synchronization.status, LiveStateStatus.synchronized);
      expect(synchronization.value, 43);
    });
  });

  group('characterIdentitySelector behaves correctly', () {
    test('characterIdentitySelector returns the full identity', () {
      const CharacterIdentitySynchronizationChangedAction action =
          CharacterIdentitySynchronizationChangedAction(
            LiveDomainState<({String name, String race})?>(
              status: LiveStateStatus.synchronized,
              value: (name: 'Player', race: 'Nord'),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 4,
            ),
          );
      final SessionLiveState liveState = liveStateReducer(
        const SessionLiveState.initial(),
        action,
      );

      final synchronization = LiveStateSelectors.characterIdentitySelector(
        AppState.initial(liveState: liveState),
      );

      expect(synchronization.value?.name, 'Player');
      expect(synchronization.value?.race, 'Nord');
    });
  });

  group('supernaturalTraitsSelector behaves correctly', () {
    test(
      'supernaturalTraitsSelector preserves all-false as synchronized data',
      () {
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
                revision: 5,
              ),
            );
        final SessionLiveState liveState = liveStateReducer(
          const SessionLiveState.initial(),
          action,
        );

        final synchronization = LiveStateSelectors.supernaturalTraitsSelector(
          AppState.initial(liveState: liveState),
        );

        expect(synchronization.status, LiveStateStatus.synchronized);
        expect(synchronization.value?.isVampire, isFalse);
        expect(synchronization.value?.hasVampireLordForm, isFalse);
        expect(synchronization.value?.hasWerewolfForm, isFalse);
      },
    );
  });

  group('playerLocationSelector behaves correctly', () {
    test('playerLocationSelector retains optional location fields', () {
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
                cellKind: LiveCellKind.interior,
                cellName: 'Interior',
                locationId: null,
                locationName: null,
                worldspaceId: null,
                worldspaceName: null,
              ),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 6,
            ),
          );
      final SessionLiveState liveState = liveStateReducer(
        const SessionLiveState.initial(),
        action,
      );

      final synchronization = LiveStateSelectors.playerLocationSelector(
        AppState.initial(liveState: liveState),
      );

      expect(synchronization.value?.cellId, 22);
      expect(synchronization.value?.locationName, isNull);
      expect(synchronization.value?.worldspaceId, isNull);
    });
  });

  group('gameTimeSelector behaves correctly', () {
    test(
      'gameTimeSelector returns Skyrim calendar fields without conversion',
      () {
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
                revision: 7,
              ),
            );
        final SessionLiveState liveState = liveStateReducer(
          const SessionLiveState.initial(),
          action,
        );

        final synchronization = LiveStateSelectors.gameTimeSelector(
          AppState.initial(liveState: liveState),
        );

        expect(synchronization.value?.year, 4);
        expect(synchronization.value?.monthName, 'Last Seed');
        expect(synchronization.value?.minute, 30);
      },
    );
  });

  group('trackedQuestsSelector behaves correctly', () {
    test('trackedQuestsSelector distinguishes empty from unavailable', () {
      const TrackedQuestsSynchronizationChangedAction action =
          TrackedQuestsSynchronizationChangedAction(
            LiveDomainState<List<LiveTrackedQuest>>(
              status: LiveStateStatus.synchronized,
              value: <LiveTrackedQuest>[],
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 8,
            ),
          );
      final SessionLiveState liveState = liveStateReducer(
        const SessionLiveState.initial(),
        action,
      );

      final synchronization = LiveStateSelectors.trackedQuestsSelector(
        AppState.initial(liveState: liveState),
      );

      expect(synchronization.status, LiveStateStatus.synchronized);
      expect(synchronization.value, isEmpty);
      expect(synchronization.value, isNotNull);
    });
  });
}
