import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises the typed Redux actions used by live-state middleware.
void main() {
  group('CharacterVitalsSynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterVitalsSynchronizationChangedAction carries coherent raw values',
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
                  health: (current: 195, max: 180),
                  magicka: (current: 30, max: 120),
                  stamina: (current: 88, max: 100),
                ),
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 3,
              ),
            );

        expect(action.synchronization.status, LiveStateStatus.synchronized);
        expect(action.synchronization.value?.health.current, 195);
        expect(action.synchronization.value?.health.max, 180);
        expect(action.synchronization.revision, 3);
      },
    );
  });

  group('CharacterXpSynchronizationChangedAction behaves correctly', () {
    test('CharacterXpSynchronizationChangedAction carries exact XP', () {
      const CharacterXpSynchronizationChangedAction action =
          CharacterXpSynchronizationChangedAction(
            LiveDomainState<double?>(
              status: LiveStateStatus.stale,
              value: 72.5,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 4,
            ),
          );

      expect(action.synchronization.status, LiveStateStatus.stale);
      expect(action.synchronization.value, 72.5);
      expect(action.synchronization.revision, 4);
    });
  });

  group('CharacterLevelSynchronizationChangedAction behaves correctly', () {
    test('CharacterLevelSynchronizationChangedAction carries the level', () {
      const CharacterLevelSynchronizationChangedAction action =
          CharacterLevelSynchronizationChangedAction(
            LiveDomainState<int?>(
              status: LiveStateStatus.synchronized,
              value: 43,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 5,
            ),
          );

      expect(action.synchronization.value, 43);
      expect(action.synchronization.status, LiveStateStatus.synchronized);
    });
  });

  group('CharacterIdentitySynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterIdentitySynchronizationChangedAction carries both identity fields',
      () {
        const CharacterIdentitySynchronizationChangedAction action =
            CharacterIdentitySynchronizationChangedAction(
              LiveDomainState<({String name, String race})?>(
                status: LiveStateStatus.synchronized,
                value: (name: 'Player', race: 'Nord'),
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 6,
              ),
            );

        expect(action.synchronization.value?.name, 'Player');
        expect(action.synchronization.value?.race, 'Nord');
      },
    );
  });

  group(
    'CharacterSupernaturalTraitsSynchronizationChangedAction behaves correctly',
    () {
      test(
        'CharacterSupernaturalTraitsSynchronizationChangedAction keeps predicates independent',
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
                    hasVampireLordForm: true,
                    hasWerewolfForm: true,
                  ),
                  stateAuthorityId: 'authority-a',
                  playContextId: 'context-a',
                  revision: 7,
                ),
              );

          expect(action.synchronization.value?.isVampire, isFalse);
          expect(action.synchronization.value?.hasVampireLordForm, isTrue);
          expect(action.synchronization.value?.hasWerewolfForm, isTrue);
        },
      );
    },
  );

  group('PlayerLocationSynchronizationChangedAction behaves correctly', () {
    test(
      'PlayerLocationSynchronizationChangedAction preserves nullable location facts',
      () {
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
                  cellName: 'Cell',
                  locationId: null,
                  locationName: null,
                  worldspaceId: null,
                  worldspaceName: null,
                ),
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 8,
              ),
            );

        expect(action.synchronization.value?.cellKind, LiveCellKind.interior);
        expect(action.synchronization.value?.locationName, isNull);
        expect(action.synchronization.value?.worldspaceName, isNull);
      },
    );
  });

  group('GameTimeSynchronizationChangedAction behaves correctly', () {
    test(
      'GameTimeSynchronizationChangedAction carries Skyrim calendar fields',
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
                revision: 9,
              ),
            );

        expect(action.synchronization.value?.year, 4);
        expect(action.synchronization.value?.month, 8);
        expect(action.synchronization.value?.monthName, 'Last Seed');
        expect(action.synchronization.value?.hour, 14);
        expect(action.synchronization.value?.minute, 30);
      },
    );
  });

  group('TrackedQuestsSynchronizationChangedAction behaves correctly', () {
    test(
      'TrackedQuestsSynchronizationChangedAction carries the complete collection',
      () {
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
                revision: 10,
              ),
            );

        expect(action.synchronization.value, hasLength(2));
        expect(action.synchronization.value?.map((quest) => quest.questId), [
          1,
          2,
        ]);
        expect(action.synchronization.revision, 10);
      },
    );
  });

  group('SessionLiveStateResetAction behaves correctly', () {
    test('SessionLiveStateResetAction instances are equal', () {
      expect(
        const SessionLiveStateResetAction(),
        const SessionLiveStateResetAction(),
      );
    });
  });
}
