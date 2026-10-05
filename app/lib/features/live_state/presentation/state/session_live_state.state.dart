import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';

/// Redux projection of the independently synchronized Skyrim gameplay domains.
class SessionLiveState extends Equatable {
  /// Creates a complete live-state slice.
  /// @param characterVitals The coherent Vitals domain projection.
  /// @param characterXp The XP domain projection.
  /// @param characterLevel The Level domain projection.
  /// @param characterIdentity The complete Identity domain projection.
  /// @param supernaturalTraits The independent Supernatural Traits projection.
  /// @param playerLocation The complete Location facts.
  /// @param gameTime The Skyrim calendar projection.
  /// @param trackedQuests The complete tracked-quest collection.
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
    : characterVitals = const LiveDomainState.notSubscribed(),
      characterXp = const LiveDomainState.notSubscribed(),
      characterLevel = const LiveDomainState.notSubscribed(),
      characterIdentity = const LiveDomainState.notSubscribed(),
      supernaturalTraits = const LiveDomainState.notSubscribed(),
      playerLocation = const LiveDomainState.notSubscribed(),
      gameTime = const LiveDomainState.notSubscribed(),
      trackedQuests = const LiveDomainState.notSubscribed();

  /// One coherent Health, Magicka, and Stamina observation.
  final LiveDomainState<
    ({
      ({double current, double max}) health,
      ({double current, double max}) magicka,
      ({double current, double max}) stamina,
    })
  >
  characterVitals;

  /// The exact numeric XP value, without an inferred percentage.
  final LiveDomainState<double?> characterXp;

  /// The latest character level.
  final LiveDomainState<int?> characterLevel;

  /// The player's display name and game-provided identity-race name.
  final LiveDomainState<({String name, String race})?> characterIdentity;

  /// The three independent supernatural predicates.
  final LiveDomainState<
    ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
  >
  supernaturalTraits;

  /// The cell, selected location, and worldspace facts without display fallback.
  final LiveDomainState<
    ({
      int cellId,
      LiveCellKind cellKind,
      String? cellName,
      int? locationId,
      String? locationName,
      int? worldspaceId,
      String? worldspaceName,
    })?
  >
  playerLocation;

  /// Skyrim calendar fields, kept separate from Gregorian date/time types.
  final LiveDomainState<
    ({int year, int month, String monthName, int day, int hour, int minute})?
  >
  gameTime;

  /// The complete tracked quest collection; an available empty list is authoritative.
  final LiveDomainState<List<LiveTrackedQuest>> trackedQuests;

  /// Returns a copy with selected domain projections replaced.
  SessionLiveState copyWith({
    /// The replacement Vitals projection, when supplied.
    LiveDomainState<
      ({
        ({double current, double max}) health,
        ({double current, double max}) magicka,
        ({double current, double max}) stamina,
      })
    >?
    characterVitals,

    /// The replacement XP projection, when supplied.
    LiveDomainState<double?>? characterXp,

    /// The replacement Level projection, when supplied.
    LiveDomainState<int?>? characterLevel,

    /// The replacement Identity projection, when supplied.
    LiveDomainState<({String name, String race})?>? characterIdentity,

    /// The replacement Supernatural Traits projection, when supplied.
    LiveDomainState<
      ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
    >?
    supernaturalTraits,

    /// The replacement Location projection, when supplied.
    LiveDomainState<
      ({
        int cellId,
        LiveCellKind cellKind,
        String? cellName,
        int? locationId,
        String? locationName,
        int? worldspaceId,
        String? worldspaceName,
      })?
    >?
    playerLocation,

    /// The replacement Skyrim calendar projection, when supplied.
    LiveDomainState<
      ({int year, int month, String monthName, int day, int hour, int minute})?
    >?
    gameTime,

    /// The replacement complete tracked-quest projection, when supplied.
    LiveDomainState<List<LiveTrackedQuest>>? trackedQuests,
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

  /// Returns the initial projections after an admitted session has ended.
  /// @return A fresh slice with every domain marked `notSubscribed`.
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
