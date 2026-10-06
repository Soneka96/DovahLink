import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import '../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterLevelState,
        CharacterSupernaturalTraitsState,
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
              value: Fixtures.buildCharacterVitals(
                health: Fixtures.buildCharacterVital(current: 80, max: 100),
                magicka: Fixtures.buildCharacterVital(current: 40, max: 80),
                stamina: Fixtures.buildCharacterVital(current: 50, max: 90),
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
        final StateSynchronization<CharacterXpState> synchronization =
            StateSynchronization<CharacterXpState>(
              status: DovahLinkStateStatus.unavailable,
              value: Fixtures.buildCharacterXp(value: null),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 2,
            );

        final CharacterXpSynchronizationChangedAction action =
            CharacterXpSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('CharacterLevelSynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterLevelSynchronizationChangedAction retains synchronization identity',
      () {
        final StateSynchronization<CharacterLevelState> synchronization =
            StateSynchronization<CharacterLevelState>(
              status: DovahLinkStateStatus.synchronized,
              value: Fixtures.buildCharacterLevel(value: 43),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 3,
            );

        final CharacterLevelSynchronizationChangedAction action =
            CharacterLevelSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('CharacterIdentitySynchronizationChangedAction behaves correctly', () {
    test(
      'CharacterIdentitySynchronizationChangedAction retains the SDK object',
      () {
        final CharacterIdentityState identity = Fixtures.buildCharacterIdentity(
          name: 'Player',
          race: 'Nord',
        );
        final StateSynchronization<CharacterIdentityState?> synchronization =
            StateSynchronization<CharacterIdentityState?>(
              status: DovahLinkStateStatus.synchronized,
              value: identity,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 4,
            );

        final CharacterIdentitySynchronizationChangedAction action =
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
          final StateSynchronization<CharacterSupernaturalTraitsState?>
          synchronization =
              StateSynchronization<CharacterSupernaturalTraitsState?>(
                status: DovahLinkStateStatus.synchronized,
                value: Fixtures.buildSupernaturalTraits(
                  isVampire: false,
                  hasVampireLordForm: false,
                  hasWerewolfForm: false,
                ),
                stateAuthorityId: 'authority-a',
                playContextId: 'context-a',
                revision: 5,
              );

          final CharacterSupernaturalTraitsSynchronizationChangedAction action =
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
        final StateSynchronization<PlayerLocationState?> synchronization =
            StateSynchronization<PlayerLocationState?>(
              status: DovahLinkStateStatus.synchronized,
              value: Fixtures.buildPlayerLocation(
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

        final PlayerLocationSynchronizationChangedAction action =
            PlayerLocationSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('GameTimeSynchronizationChangedAction behaves correctly', () {
    test(
      'GameTimeSynchronizationChangedAction retains synchronization identity',
      () {
        final StateSynchronization<GameTimeState?> synchronization =
            StateSynchronization<GameTimeState?>(
              status: DovahLinkStateStatus.synchronized,
              value: Fixtures.buildGameTime(
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

        final GameTimeSynchronizationChangedAction action =
            GameTimeSynchronizationChangedAction(synchronization);

        expect(identical(action.synchronization, synchronization), isTrue);
      },
    );
  });

  group('TrackedQuestsSynchronizationChangedAction behaves correctly', () {
    test(
      'TrackedQuestsSynchronizationChangedAction retains the complete SDK state',
      () {
        final TrackedQuestsState quests = Fixtures.buildTrackedQuests(
          quests: const [],
        );
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
