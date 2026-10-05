import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
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
        PlayerLocationState,
        StateSynchronization,
        TrackedQuestsState;

/// Exercises the Session Live State slice and its immutable SDK-value updates.
void main() {
  group('SessionLiveState initial behaves correctly', () {
    test('SessionLiveState initial marks all domains notSubscribed', () {
      const SessionLiveState state = SessionLiveState.initial();

      expect(state.characterVitals.status, DovahLinkStateStatus.notSubscribed);
      expect(state.characterXp.status, DovahLinkStateStatus.notSubscribed);
      expect(state.characterLevel.status, DovahLinkStateStatus.notSubscribed);
      expect(
        state.characterIdentity.status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(
        state.supernaturalTraits.status,
        DovahLinkStateStatus.notSubscribed,
      );
      expect(state.playerLocation.status, DovahLinkStateStatus.notSubscribed);
      expect(state.gameTime.status, DovahLinkStateStatus.notSubscribed);
      expect(state.trackedQuests.status, DovahLinkStateStatus.notSubscribed);
    });
  });

  group('SessionLiveState copyWith behaves correctly', () {
    test(
      'SessionLiveState copyWith retains the complete SDK synchronization object',
      () {
        const SessionLiveState state = SessionLiveState.initial();
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
              revision: 8,
            );

        final SessionLiveState updated = state.copyWith(
          characterIdentity: synchronization,
        );

        expect(identical(updated.characterIdentity, synchronization), isTrue);
        expect(identical(updated.characterIdentity.value, identity), isTrue);
        expect(updated.characterXp, state.characterXp);
        expect(identical(updated, state), isFalse);
      },
    );

    test('SessionLiveState copyWith replaces all eight domains', () {
      const StateSynchronization<CharacterVitalsState> vitals =
          StateSynchronization<CharacterVitalsState>.notSubscribed();
      const StateSynchronization<CharacterXpState> xp =
          StateSynchronization<CharacterXpState>.notSubscribed();
      const StateSynchronization<CharacterLevelState> level =
          StateSynchronization<CharacterLevelState>.notSubscribed();
      const StateSynchronization<CharacterIdentityState?> identity =
          StateSynchronization<CharacterIdentityState?>.notSubscribed();
      const StateSynchronization<CharacterSupernaturalTraitsState?> traits =
          StateSynchronization<
            CharacterSupernaturalTraitsState?
          >.notSubscribed();
      const StateSynchronization<PlayerLocationState?> location =
          StateSynchronization<PlayerLocationState?>.notSubscribed();
      const StateSynchronization<GameTimeState?> time =
          StateSynchronization<GameTimeState?>.notSubscribed();
      const StateSynchronization<TrackedQuestsState?> quests =
          StateSynchronization<TrackedQuestsState?>.notSubscribed();
      const SessionLiveState state = SessionLiveState.initial();

      final SessionLiveState updated = state.copyWith(
        characterVitals: vitals,
        characterXp: xp,
        characterLevel: level,
        characterIdentity: identity,
        supernaturalTraits: traits,
        playerLocation: location,
        gameTime: time,
        trackedQuests: quests,
      );

      expect(updated.characterVitals, same(vitals));
      expect(updated.characterXp, same(xp));
      expect(updated.characterLevel, same(level));
      expect(updated.characterIdentity, same(identity));
      expect(updated.supernaturalTraits, same(traits));
      expect(updated.playerLocation, same(location));
      expect(updated.gameTime, same(time));
      expect(updated.trackedQuests, same(quests));
    });
  });

  group('SessionLiveState reset behaves correctly', () {
    test(
      'SessionLiveState reset clears populated SDK synchronization values',
      () {
        final StateSynchronization<CharacterIdentityState?> synchronization =
            StateSynchronization<CharacterIdentityState?>(
              status: DovahLinkStateStatus.synchronized,
              value: Fixtures.buildCharacterIdentity(
                name: 'Player',
                race: 'Nord',
              ),
              stateAuthorityId: 'authority-a',
              playContextId: 'context-a',
              revision: 1,
            );
        final SessionLiveState state = const SessionLiveState.initial()
            .copyWith(characterIdentity: synchronization);

        final SessionLiveState reset = state.reset();

        expect(
          reset.characterIdentity.status,
          DovahLinkStateStatus.notSubscribed,
        );
        expect(reset.characterIdentity.value, isNull);
        expect(reset.characterIdentity.stateAuthorityId, isNull);
        expect(reset.characterIdentity.playContextId, isNull);
        expect(reset.characterIdentity.revision, isNull);
        expect(reset.trackedQuests.status, DovahLinkStateStatus.notSubscribed);
      },
    );
  });
}
