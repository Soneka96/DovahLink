import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_state.selectors.dart';
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

/// Typed Redux projection consumed by the future Session Overview presentation.
class SessionOverviewViewModel extends Equatable {
  /// The SDK Vitals synchronization value.
  final StateSynchronization<CharacterVitalsState> characterVitals;

  /// The SDK XP synchronization value.
  final StateSynchronization<CharacterXpState> characterXp;

  /// The SDK Level synchronization value.
  final StateSynchronization<CharacterLevelState> characterLevel;

  /// The SDK Identity synchronization value.
  final StateSynchronization<CharacterIdentityState?> characterIdentity;

  /// The SDK Supernatural Traits synchronization value.
  final StateSynchronization<CharacterSupernaturalTraitsState?>
  supernaturalTraits;

  /// The SDK Location synchronization value.
  final StateSynchronization<PlayerLocationState?> playerLocation;

  /// The SDK Game Time synchronization value.
  final StateSynchronization<GameTimeState?> gameTime;

  /// The SDK Tracked Quests synchronization value.
  final StateSynchronization<TrackedQuestsState?> trackedQuests;

  /// Creates a lossless projection of every selected live-state domain.
  /// @param characterVitals The SDK Vitals synchronization value.
  /// @param characterXp The SDK XP synchronization value.
  /// @param characterLevel The SDK Level synchronization value.
  /// @param characterIdentity The SDK Identity synchronization value.
  /// @param supernaturalTraits The SDK Supernatural Traits synchronization value.
  /// @param playerLocation The SDK Location synchronization value.
  /// @param gameTime The SDK Game Time synchronization value.
  /// @param trackedQuests The SDK Tracked Quests synchronization value.
  const SessionOverviewViewModel({
    required this.characterVitals,
    required this.characterXp,
    required this.characterLevel,
    required this.characterIdentity,
    required this.supernaturalTraits,
    required this.playerLocation,
    required this.gameTime,
    required this.trackedQuests,
  });

  /// Builds the Overview projection from the application's Redux store.
  /// @param store The Redux store containing the live-state slice.
  /// @return A lossless projection of the SDK synchronization values.
  factory SessionOverviewViewModel.fromStore(Store<AppState> store) =>
      SessionOverviewViewModel(
        characterVitals: LiveStateSelectors.characterVitalsSelector(
          store.state,
        ),
        characterXp: LiveStateSelectors.characterXpSelector(store.state),
        characterLevel: LiveStateSelectors.characterLevelSelector(store.state),
        characterIdentity: LiveStateSelectors.characterIdentitySelector(
          store.state,
        ),
        supernaturalTraits: LiveStateSelectors.supernaturalTraitsSelector(
          store.state,
        ),
        playerLocation: LiveStateSelectors.playerLocationSelector(store.state),
        gameTime: LiveStateSelectors.gameTimeSelector(store.state),
        trackedQuests: LiveStateSelectors.trackedQuestsSelector(store.state),
      );

  /// See [Equatable.props].
  @override
  List<Object?> get props => [
    characterVitals,
    characterXp,
    characterLevel,
    characterIdentity,
    supernaturalTraits,
    playerLocation,
    gameTime,
    trackedQuests,
  ];
}
