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

import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors for public SDK gameplay synchronization values.
abstract final class LiveStateSelectors {
  /// Returns the SDK Vitals synchronization value.
  static StateSynchronization<CharacterVitalsState> characterVitalsSelector(
    AppState state,
  ) => state.liveState.characterVitals;

  /// Returns the SDK XP synchronization value.
  static StateSynchronization<CharacterXpState> characterXpSelector(
    AppState state,
  ) => state.liveState.characterXp;

  /// Returns the SDK Level synchronization value.
  static StateSynchronization<CharacterLevelState> characterLevelSelector(
    AppState state,
  ) => state.liveState.characterLevel;

  /// Returns the SDK Identity synchronization value.
  static StateSynchronization<CharacterIdentityState?>
  characterIdentitySelector(AppState state) =>
      state.liveState.characterIdentity;

  /// Returns the SDK Supernatural Traits synchronization value.
  static StateSynchronization<CharacterSupernaturalTraitsState?>
  supernaturalTraitsSelector(AppState state) =>
      state.liveState.supernaturalTraits;

  /// Returns the SDK Location synchronization value.
  static StateSynchronization<PlayerLocationState?> playerLocationSelector(
    AppState state,
  ) => state.liveState.playerLocation;

  /// Returns the SDK Game Time synchronization value.
  static StateSynchronization<GameTimeState?> gameTimeSelector(
    AppState state,
  ) => state.liveState.gameTime;

  /// Returns the SDK Tracked Quests synchronization value.
  static StateSynchronization<TrackedQuestsState?> trackedQuestsSelector(
    AppState state,
  ) => state.liveState.trackedQuests;
}
