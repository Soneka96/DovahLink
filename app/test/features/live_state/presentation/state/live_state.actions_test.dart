import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';

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
        StateSynchronization,
        TrackedQuestsState;

/// Exercises that Redux actions retain SDK synchronization objects unchanged.
void main() {
  group('CharacterVitalsSynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterVitalsSynchronizationChangedAction retains synchronization identity',
      () {
        final StateSynchronization<CharacterVitalsState> synchronization =
            StateSynchronization<CharacterVitalsState>(
              status: DovahLinkStateStatus.synchronized,
              value: CharacterVitalsState(
                health: const CharacterVital(current: 80, max: 100),
                magicka: const CharacterVital(current: 40, max: 80),
                stamina: const CharacterVital(current: 50, max: 90),
              ),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 1,
            );

        final CharacterVitalsSynchronizationChangedAction action =
            CharacterVitalsSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('CharacterXpSynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterXpSynchronizationChangedAction retains synchronization identity',
      () {
        const StateSynchronization<CharacterXpState> synchronization =
            StateSynchronization<CharacterXpState>(
              status: DovahLinkStateStatus.unavailable,
              value: CharacterXpState(value: null),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 2,
            );

        const CharacterXpSynchronizationChangedAction action =
            CharacterXpSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('CharacterLevelSynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterLevelSynchronizationChangedAction retains synchronization identity',
      () {
        const StateSynchronization<CharacterLevelState> synchronization =
            StateSynchronization<CharacterLevelState>(
              status: DovahLinkStateStatus.synchronized,
              value: CharacterLevelState(value: 43),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 3,
            );

        const CharacterLevelSynchronizationChangedAction action =
            CharacterLevelSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('CharacterIdentitySynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterIdentitySynchronizationChangedAction retains the SDK object',
      () {
        const CharacterIdentityState identity = CharacterIdentityState(
          name: 'Player',
          race: 'Nord',
        );
        const StateSynchronization<CharacterIdentityState?> synchronization =
            StateSynchronization<CharacterIdentityState?>(
              status: DovahLinkStateStatus.synchronized,
              value: identity,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 4,
            );

        const CharacterIdentitySynchronizationChangedAction action =
            CharacterIdentitySynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
        expect(identical(action.synchronization.value, identity), isTrue);
      },
    );
  });

  group(
    'CharacterSupernaturalTraitsSynchronizationChangedAction behaves correctly',
    () {
      test(
        'CharacterSupernaturalTraitsSynchronizationChangedAction retains synchronization identity',
        () {
          const StateSynchronization<CharacterSupernaturalTraitsState?>
          synchronization =
              StateSynchronization<CharacterSupernaturalTraitsState?>(
                status: DovahLinkStateStatus.synchronized,
                value: CharacterSupernaturalTraitsState(
                  isVampire: false,
                  hasVampireLordForm: false,
                  hasWerewolfForm: false,
                ),
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 5,
              );

          const CharacterSupernaturalTraitsSynchronizationChangedAction action =
              CharacterSupernaturalTraitsSynchronizationChangedAction(
                synchronization,
              );

          expect(identical(action.synchronization, synchronization), isTrue);
        },
      );
    },
  );

  group('PlayerLocationSynchronizationChangedAction behaves correctly', () {
    test(
      'PlayerLocationSynchronizationChangedAction retains synchronization identity',
      () {
        const StateSynchronization<PlayerLocationState?> synchronization =
            StateSynchronization<PlayerLocationState?>(
              status: DovahLinkStateStatus.synchronized,
              value: PlayerLocationState(
                cellId: 1,
                cellKind: PlayerLocationCellKind.exterior,
                cellName: null,
                locationId: null,
                locationName: null,
                worldspaceId: null,
                worldspaceName: null,
              ),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 6,
            );

        const PlayerLocationSynchronizationChangedAction action =
            PlayerLocationSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('GameTimeSynchronizationChangedAction behaves correctly', () {
    test(
      'GameTimeSynchronizationChangedAction retains synchronization identity',
      () {
        const StateSynchronization<GameTimeState?> synchronization =
            StateSynchronization<GameTimeState?>(
              status: DovahLinkStateStatus.synchronized,
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
              revision: 7,
            );

        const GameTimeSynchronizationChangedAction action =
            GameTimeSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('TrackedQuestsSynchronizationChangedAction behaves correctly', () {
    test(
      'TrackedQuestsSynchronizationChangedAction retains the complete SDK state',
      () {
        final TrackedQuestsState quests = TrackedQuestsState(quests: const []);
        final StateSynchronization<TrackedQuestsState?> synchronization =
            StateSynchronization<TrackedQuestsState?>(
              status: DovahLinkStateStatus.synchronized,
              value: quests,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 8,
            );

        final TrackedQuestsSynchronizationChangedAction action =
            TrackedQuestsSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
        expect(identical(action.synchronization.value, quests), isTrue);
        expect(action.synchronization.value?.quests, isEmpty);
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
