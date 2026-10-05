import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import '../../../../fixtures/fixtures.dart';

/// Exercises the Session Live State slice and its immutable updates.
void main() {
  group('SessionLiveState initial behaves correctly', () {
    test('SessionLiveState initial marks all domains notSubscribed', () {
      const SessionLiveState state = SessionLiveState.initial();

      expect(state.characterVitals.status, LiveStateStatus.notSubscribed);
      expect(state.characterXp.status, LiveStateStatus.notSubscribed);
      expect(state.characterLevel.status, LiveStateStatus.notSubscribed);
      expect(state.characterIdentity.status, LiveStateStatus.notSubscribed);
      expect(state.supernaturalTraits.status, LiveStateStatus.notSubscribed);
      expect(state.playerLocation.status, LiveStateStatus.notSubscribed);
      expect(state.gameTime.status, LiveStateStatus.notSubscribed);
      expect(state.trackedQuests.status, LiveStateStatus.notSubscribed);
    });
  });

  group('SessionLiveState copyWith behaves correctly', () {
    test(
      'SessionLiveState copyWith changes one domain and preserves siblings',
      () {
        const SessionLiveState state = SessionLiveState.initial();
        const LiveDomainState<double?> xp = LiveDomainState<double?>(
          status: LiveStateStatus.synchronized,
          value: 63.25,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 8,
        );

        final SessionLiveState updated = state.copyWith(characterXp: xp);

        expect(updated.characterXp, xp);
        expect(updated.characterVitals, state.characterVitals);
        expect(updated.trackedQuests, state.trackedQuests);
        expect(identical(updated, state), isFalse);
      },
    );

    test('SessionLiveState copyWith can replace each domain independently', () {
      SessionLiveState updated = const SessionLiveState.initial();
      final SessionLiveState initial = updated;
      updated = updated.copyWith(
        characterVitals:
            const LiveDomainState<
              ({
                ({double current, double max}) health,
                ({double current, double max}) magicka,
                ({double current, double max}) stamina,
              })
            >(
              status: LiveStateStatus.stale,
              value: (
                health: (current: 81, max: 100),
                magicka: (current: 32, max: 70),
                stamina: (current: 50, max: 90),
              ),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 1,
            ),
      );
      expect(updated.characterVitals.status, LiveStateStatus.stale);
      expect(identical(updated.characterXp, initial.characterXp), isTrue);

      updated = updated.copyWith(
        characterXp: const LiveDomainState<double?>(
          status: LiveStateStatus.recovering,
          value: 63.25,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 2,
        ),
      );
      expect(updated.characterXp.status, LiveStateStatus.recovering);
      expect(
        identical(updated.characterVitals, initial.characterVitals),
        isFalse,
      );

      updated = updated.copyWith(
        characterLevel: const LiveDomainState<int?>(
          status: LiveStateStatus.failed,
          value: 43,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 3,
        ),
      );
      updated = updated.copyWith(
        characterIdentity: const LiveDomainState<({String name, String race})?>(
          status: LiveStateStatus.unavailable,
          value: null,
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 4,
        ),
      );
      updated = updated.copyWith(
        supernaturalTraits:
            const LiveDomainState<
              ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
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
      updated = updated.copyWith(
        playerLocation:
            const LiveDomainState<
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
              status: LiveStateStatus.stale,
              value: (
                cellId: 10,
                cellKind: LiveCellKind.interior,
                cellName: null,
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
      updated = updated.copyWith(
        gameTime:
            const LiveDomainState<
              ({
                int year,
                int month,
                String monthName,
                int day,
                int hour,
                int minute,
              })?
            >(
              status: LiveStateStatus.recovering,
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
      updated = updated.copyWith(
        trackedQuests: LiveDomainState<List<LiveTrackedQuest>>(
          status: LiveStateStatus.synchronized,
          value: [Fixtures.buildLiveTrackedQuest()],
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 8,
        ),
      );

      expect(updated.characterVitals.status, LiveStateStatus.stale);
      expect(updated.characterXp.value, 63.25);
      expect(updated.characterLevel.value, 43);
      expect(updated.characterIdentity.status, LiveStateStatus.unavailable);
      expect(updated.supernaturalTraits.value?.hasVampireLordForm, isFalse);
      expect(updated.playerLocation.value?.locationName, isNull);
      expect(updated.gameTime.value?.year, 4);
      expect(updated.trackedQuests.value, hasLength(1));
    });
  });

  group('SessionLiveState trackedQuests behaves correctly', () {
    test(
      'SessionLiveState distinguishes unavailable quests from synchronized empty',
      () {
        const SessionLiveState initial = SessionLiveState.initial();
        const LiveDomainState<List<LiveTrackedQuest>> unavailable =
            LiveDomainState<List<LiveTrackedQuest>>(
              status: LiveStateStatus.unavailable,
              value: null,
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 8,
            );
        const LiveDomainState<List<LiveTrackedQuest>> empty =
            LiveDomainState<List<LiveTrackedQuest>>(
              status: LiveStateStatus.synchronized,
              value: <LiveTrackedQuest>[],
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 8,
            );

        expect(
          initial.copyWith(trackedQuests: unavailable).trackedQuests.value,
          isNull,
        );
        expect(
          initial.copyWith(trackedQuests: unavailable).trackedQuests.status,
          LiveStateStatus.unavailable,
        );
        expect(
          initial.copyWith(trackedQuests: empty).trackedQuests.value,
          isEmpty,
        );
        expect(
          initial.copyWith(trackedQuests: empty).trackedQuests.status,
          LiveStateStatus.synchronized,
        );
      },
    );
  });

  group('SessionLiveState reset behaves correctly', () {
    test('SessionLiveState reset clears values and authority metadata', () {
      final SessionLiveState state = _buildPopulatedLiveState();
      final SessionLiveState reset = state.reset();

      expect(reset.characterLevel.status, LiveStateStatus.notSubscribed);
      expect(reset.characterLevel.value, isNull);
      expect(reset.characterLevel.stateAuthorityId, isNull);
      expect(reset.characterLevel.playContextId, isNull);
      expect(reset.characterLevel.revision, isNull);
      expect(reset.characterVitals.status, LiveStateStatus.notSubscribed);
      expect(reset.characterXp.status, LiveStateStatus.notSubscribed);
      expect(reset.characterIdentity.status, LiveStateStatus.notSubscribed);
      expect(reset.supernaturalTraits.status, LiveStateStatus.notSubscribed);
      expect(reset.playerLocation.status, LiveStateStatus.notSubscribed);
      expect(reset.gameTime.status, LiveStateStatus.notSubscribed);
      expect(reset.trackedQuests.status, LiveStateStatus.notSubscribed);
      expect(reset.characterVitals.value, isNull);
      expect(reset.characterXp.value, isNull);
      expect(reset.characterIdentity.value, isNull);
      expect(reset.supernaturalTraits.value, isNull);
      expect(reset.playerLocation.value, isNull);
      expect(reset.gameTime.value, isNull);
      expect(reset.trackedQuests.value, isNull);
      expect(reset.characterVitals.stateAuthorityId, isNull);
      expect(reset.characterXp.stateAuthorityId, isNull);
      expect(reset.characterLevel.stateAuthorityId, isNull);
      expect(reset.characterIdentity.stateAuthorityId, isNull);
      expect(reset.supernaturalTraits.stateAuthorityId, isNull);
      expect(reset.playerLocation.stateAuthorityId, isNull);
      expect(reset.gameTime.stateAuthorityId, isNull);
      expect(reset.trackedQuests.stateAuthorityId, isNull);
      expect(reset.characterVitals.playContextId, isNull);
      expect(reset.characterXp.playContextId, isNull);
      expect(reset.characterLevel.playContextId, isNull);
      expect(reset.characterIdentity.playContextId, isNull);
      expect(reset.supernaturalTraits.playContextId, isNull);
      expect(reset.playerLocation.playContextId, isNull);
      expect(reset.gameTime.playContextId, isNull);
      expect(reset.trackedQuests.playContextId, isNull);
      expect(reset.characterVitals.revision, isNull);
      expect(reset.characterXp.revision, isNull);
      expect(reset.characterLevel.revision, isNull);
      expect(reset.characterIdentity.revision, isNull);
      expect(reset.supernaturalTraits.revision, isNull);
      expect(reset.playerLocation.revision, isNull);
      expect(reset.gameTime.revision, isNull);
      expect(reset.trackedQuests.revision, isNull);
    });
  });
}

/// Builds a complete slice with known current values for reset assertions.
/// @return A slice with a value and synchronization metadata for every domain.
SessionLiveState _buildPopulatedLiveState() {
  const SessionLiveState initial = SessionLiveState.initial();
  return initial.copyWith(
    characterVitals:
        const LiveDomainState<
          ({
            ({double current, double max}) health,
            ({double current, double max}) magicka,
            ({double current, double max}) stamina,
          })
        >(
          status: LiveStateStatus.synchronized,
          value: (
            health: (current: 80, max: 100),
            magicka: (current: 40, max: 80),
            stamina: (current: 50, max: 90),
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 1,
        ),
    characterXp: const LiveDomainState<double?>(
      status: LiveStateStatus.stale,
      value: 71.5,
      stateAuthorityId: 'authority-a',
      playContextId: 'context-a',
      revision: 2,
    ),
    characterLevel: const LiveDomainState<int?>(
      status: LiveStateStatus.synchronized,
      value: 43,
      stateAuthorityId: 'authority-a',
      playContextId: 'context-a',
      revision: 3,
    ),
    characterIdentity: const LiveDomainState<({String name, String race})?>(
      status: LiveStateStatus.synchronized,
      value: (name: 'Player', race: 'Nord'),
      stateAuthorityId: 'authority-a',
      playContextId: 'context-a',
      revision: 4,
    ),
    supernaturalTraits:
        const LiveDomainState<
          ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
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
    playerLocation:
        const LiveDomainState<
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
            cellId: 10,
            cellKind: LiveCellKind.interior,
            cellName: 'Cell',
            locationId: 11,
            locationName: 'Place',
            worldspaceId: null,
            worldspaceName: null,
          ),
          stateAuthorityId: 'authority-a',
          playContextId: 'context-a',
          revision: 6,
        ),
    gameTime:
        const LiveDomainState<
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
    trackedQuests: LiveDomainState<List<LiveTrackedQuest>>(
      status: LiveStateStatus.synchronized,
      value: [Fixtures.buildLiveTrackedQuest()],
      stateAuthorityId: 'authority-a',
      playContextId: 'context-a',
      revision: 8,
    ),
  );
}
