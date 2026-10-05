import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state.selectors.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterLevelState,
        CharacterSupernaturalTraitsState,
        CharacterVitalsState,
        CharacterXpState,
        GameTimeState,
        PlayerLocationState,
        StateSynchronization,
        TrackedQuestsState;

/// Exercises direct selector access to the SDK synchronization values.
void main() {
  final SessionLiveState liveState = _buildLiveState();
  final AppState state = AppState.initial(liveState: liveState);

  group('LiveStateSelectors behaves correctly', () {
    test('characterVitalsSelector returns the stored SDK synchronization', () {
      expect(
        LiveStateSelectors.characterVitalsSelector(state),
        same(liveState.characterVitals),
      );
    });

    test('characterXpSelector returns the stored SDK synchronization', () {
      expect(
        LiveStateSelectors.characterXpSelector(state),
        same(liveState.characterXp),
      );
    });

    test('characterLevelSelector returns the stored SDK synchronization', () {
      expect(
        LiveStateSelectors.characterLevelSelector(state),
        same(liveState.characterLevel),
      );
    });

    test(
      'characterIdentitySelector returns the stored SDK synchronization',
      () {
        expect(
          LiveStateSelectors.characterIdentitySelector(state),
          same(liveState.characterIdentity),
        );
      },
    );

    test(
      'supernaturalTraitsSelector returns the stored SDK synchronization',
      () {
        expect(
          LiveStateSelectors.supernaturalTraitsSelector(state),
          same(liveState.supernaturalTraits),
        );
      },
    );

    test('playerLocationSelector returns the stored SDK synchronization', () {
      expect(
        LiveStateSelectors.playerLocationSelector(state),
        same(liveState.playerLocation),
      );
    });

    test('gameTimeSelector returns the stored SDK synchronization', () {
      expect(
        LiveStateSelectors.gameTimeSelector(state),
        same(liveState.gameTime),
      );
    });

    test('trackedQuestsSelector returns the stored SDK synchronization', () {
      expect(
        LiveStateSelectors.trackedQuestsSelector(state),
        same(liveState.trackedQuests),
      );
    });
  });
}

/// Creates one state whose eight values use their exact public SDK generic types.
SessionLiveState _buildLiveState() => const SessionLiveState(
  characterVitals: StateSynchronization<CharacterVitalsState>.notSubscribed(),
  characterXp: StateSynchronization<CharacterXpState>.notSubscribed(),
  characterLevel: StateSynchronization<CharacterLevelState>.notSubscribed(),
  characterIdentity:
      StateSynchronization<CharacterIdentityState?>.notSubscribed(),
  supernaturalTraits:
      StateSynchronization<CharacterSupernaturalTraitsState?>.notSubscribed(),
  playerLocation: StateSynchronization<PlayerLocationState?>.notSubscribed(),
  gameTime: StateSynchronization<GameTimeState?>.notSubscribed(),
  trackedQuests: StateSynchronization<TrackedQuestsState?>.notSubscribed(),
);
