import 'package:equatable/equatable.dart';

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

/// Redux projection of the independently synchronized Skyrim gameplay domains.
class SessionLiveState extends Equatable {
  /// Creates a complete live-state slice.
  /// @param characterVitals The SDK Vitals synchronization value.
  /// @param characterXp The SDK XP synchronization value.
  /// @param characterLevel The SDK Level synchronization value.
  /// @param characterIdentity The SDK Identity synchronization value.
  /// @param supernaturalTraits The SDK Supernatural Traits synchronization value.
  /// @param playerLocation The SDK Location synchronization value.
  /// @param gameTime The SDK Game Time synchronization value.
  /// @param trackedQuests The SDK Tracked Quests synchronization value.
  const SessionLiveState({
    required this.characterVitals,
    required this.characterXp,
    required this.characterLevel,
    required this.characterIdentity,
    required this.supernaturalTraits,
    required this.playerLocation,
    required this.gameTime,
    required this.trackedQuests,
  });

  /// Creates the initial slice before any domain is subscribed.
  const SessionLiveState.initial()
    : characterVitals =
          const StateSynchronization<CharacterVitalsState>.notSubscribed(),
      characterXp =
          const StateSynchronization<CharacterXpState>.notSubscribed(),
      characterLevel =
          const StateSynchronization<CharacterLevelState>.notSubscribed(),
      characterIdentity =
          const StateSynchronization<CharacterIdentityState?>.notSubscribed(),
      supernaturalTraits =
          const StateSynchronization<
            CharacterSupernaturalTraitsState?
          >.notSubscribed(),
      playerLocation =
          const StateSynchronization<PlayerLocationState?>.notSubscribed(),
      gameTime = const StateSynchronization<GameTimeState?>.notSubscribed(),
      trackedQuests =
          const StateSynchronization<TrackedQuestsState?>.notSubscribed();

  /// The SDK-owned coherent Vitals synchronization value.
  final StateSynchronization<CharacterVitalsState> characterVitals;

  /// The SDK-owned XP synchronization value.
  final StateSynchronization<CharacterXpState> characterXp;

  /// The SDK-owned Level synchronization value.
  final StateSynchronization<CharacterLevelState> characterLevel;

  /// The SDK-owned complete Identity synchronization value.
  final StateSynchronization<CharacterIdentityState?> characterIdentity;

  /// The SDK-owned Supernatural Traits synchronization value.
  final StateSynchronization<CharacterSupernaturalTraitsState?>
  supernaturalTraits;

  /// The SDK-owned Location synchronization value.
  final StateSynchronization<PlayerLocationState?> playerLocation;

  /// The SDK-owned Skyrim calendar synchronization value.
  final StateSynchronization<GameTimeState?> gameTime;

  /// The SDK-owned complete tracked-quest synchronization value.
  final StateSynchronization<TrackedQuestsState?> trackedQuests;

  /// Returns a copy with selected synchronization values replaced.
  SessionLiveState copyWith({
    /// The replacement Vitals synchronization value, when supplied.
    StateSynchronization<CharacterVitalsState>? characterVitals,

    /// The replacement XP synchronization value, when supplied.
    StateSynchronization<CharacterXpState>? characterXp,

    /// The replacement Level synchronization value, when supplied.
    StateSynchronization<CharacterLevelState>? characterLevel,

    /// The replacement Identity synchronization value, when supplied.
    StateSynchronization<CharacterIdentityState?>? characterIdentity,

    /// The replacement Supernatural Traits synchronization value, when supplied.
    StateSynchronization<CharacterSupernaturalTraitsState?>? supernaturalTraits,

    /// The replacement Location synchronization value, when supplied.
    StateSynchronization<PlayerLocationState?>? playerLocation,

    /// The replacement Game Time synchronization value, when supplied.
    StateSynchronization<GameTimeState?>? gameTime,

    /// The replacement Tracked Quests synchronization value, when supplied.
    StateSynchronization<TrackedQuestsState?>? trackedQuests,
  }) => SessionLiveState(
    characterVitals: characterVitals ?? this.characterVitals,
    characterXp: characterXp ?? this.characterXp,
    characterLevel: characterLevel ?? this.characterLevel,
    characterIdentity: characterIdentity ?? this.characterIdentity,
    supernaturalTraits: supernaturalTraits ?? this.supernaturalTraits,
    playerLocation: playerLocation ?? this.playerLocation,
    gameTime: gameTime ?? this.gameTime,
    trackedQuests: trackedQuests ?? this.trackedQuests,
  );

  /// Returns the initial synchronization values after an admitted session ends.
  SessionLiveState reset() => const SessionLiveState.initial();

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
