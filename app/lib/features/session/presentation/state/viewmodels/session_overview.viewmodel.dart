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

/// Typed Redux projection consumed by the Session Overview presentation.
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

  /// The character name when the SDK currently has a meaningful one.
  String? get characterName {
    final String? name = characterIdentity.value?.name.trim();
    return name == null || name.isEmpty ? null : name;
  }

  /// The character level with current XP when both values are available.
  String? get characterLevelLabel {
    final int? value = characterLevel.value?.value;
    if (value == null) {
      return null;
    }

    final double? experience = characterXp.value?.value;
    final String level = 'Level $value';
    return experience == null
        ? level
        : '$level (${_formatExperience(experience)} XP)';
  }

  /// The single most specific meaningful current place name.
  String? get locationName {
    final PlayerLocationState? location = playerLocation.value;
    for (final String? candidate in <String?>[
      location?.locationName,
      location?.cellName,
      location?.worldspaceName,
    ]) {
      final String? name = candidate?.trim();
      if (name != null && name.isNotEmpty) {
        return name;
      }
    }
    return null;
  }

  /// The Skyrim year and twelve-hour time when the SDK provides game time.
  String? get gameTimeLabel {
    final GameTimeState? time = gameTime.value;
    if (time == null) {
      return null;
    }

    final int hour = time.hour % 12 == 0 ? 12 : time.hour % 12;
    final String minute = time.minute.toString().padLeft(2, '0');
    final String period = time.hour < 12 ? 'AM' : 'PM';
    return '4E ${time.year}, $hour:$minute $period';
  }

  /// Joins the available character, place, and Skyrim-time context segments.
  String? get contextLine {
    final List<String> segments = <String>[
      if (characterName case final String name) name,
      if (locationName case final String name) name,
      if (gameTimeLabel case final String label) label,
    ];
    return segments.isEmpty ? null : segments.join(' · ');
  }

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

  /// Removes an unnecessary decimal suffix without changing the SDK value.
  String _formatExperience(double value) => value == value.truncateToDouble()
      ? value.toInt().toString()
      : value.toString();
}
