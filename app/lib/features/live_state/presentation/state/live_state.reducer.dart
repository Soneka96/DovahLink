import 'package:dovahlink_client/features/live_state/presentation/state/live_state.actions.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/session_live_state.state.dart';

/// Applies a live-state action to [state] without changing unrelated domains.
/// @param state The current live-state slice.
/// @param action The Redux action to reduce.
/// @return The current or updated live-state slice.
SessionLiveState liveStateReducer(SessionLiveState state, Object? action) =>
    switch (action) {
      CharacterVitalsSynchronizationChangedAction(:final synchronization) =>
        state.copyWith(characterVitals: synchronization),
      CharacterXpSynchronizationChangedAction(:final synchronization) =>
        state.copyWith(characterXp: synchronization),
      CharacterLevelSynchronizationChangedAction(:final synchronization) =>
        state.copyWith(characterLevel: synchronization),
      CharacterIdentitySynchronizationChangedAction(:final synchronization) =>
        state.copyWith(characterIdentity: synchronization),
      CharacterSupernaturalTraitsSynchronizationChangedAction(
        :final synchronization,
      ) =>
        state.copyWith(supernaturalTraits: synchronization),
      PlayerLocationSynchronizationChangedAction(:final synchronization) =>
        state.copyWith(playerLocation: synchronization),
      GameTimeSynchronizationChangedAction(:final synchronization) =>
        state.copyWith(gameTime: synchronization),
      TrackedQuestsSynchronizationChangedAction(:final synchronization) =>
        state.copyWith(trackedQuests: synchronization),
      SessionLiveStateResetAction() => state.reset(),
      _ => state,
    };
