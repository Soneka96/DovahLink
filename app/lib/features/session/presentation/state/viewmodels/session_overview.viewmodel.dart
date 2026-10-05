import 'package:equatable/equatable.dart';
import 'package:redux/redux.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state.selectors.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Typed Redux projection consumed by the future Session Overview presentation.
class SessionOverviewViewModel extends Equatable {
  /// The coherent Vitals value and synchronization metadata.
  final LiveDomainState<
    ({
      ({double current, double max}) health,
      ({double current, double max}) magicka,
      ({double current, double max}) stamina,
    })
  >
  characterVitals;

  /// The exact XP value and synchronization metadata.
  final LiveDomainState<double?> characterXp;

  /// The current level and synchronization metadata.
  final LiveDomainState<int?> characterLevel;

  /// The complete identity value and synchronization metadata.
  final LiveDomainState<({String name, String race})?> characterIdentity;

  /// The independent supernatural predicates and synchronization metadata.
  final LiveDomainState<
    ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
  >
  supernaturalTraits;

  /// Distinct cell, selected-location, and worldspace facts with synchronization metadata.
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

  /// Skyrim calendar fields and synchronization metadata.
  final LiveDomainState<
    ({int year, int month, String monthName, int day, int hour, int minute})?
  >
  gameTime;

  /// Every tracked quest and objective instance with synchronization metadata.
  final LiveDomainState<List<LiveTrackedQuest>> trackedQuests;

  /// Creates a ViewModel from all currently selected Overview domains.
  /// @param characterVitals The complete Vitals projection.
  /// @param characterXp The XP projection.
  /// @param characterLevel The Level projection.
  /// @param characterIdentity The Identity projection.
  /// @param supernaturalTraits The independent supernatural predicates.
  /// @param playerLocation The complete Location projection.
  /// @param gameTime The Skyrim calendar projection.
  /// @param trackedQuests The complete tracked-quest collection.
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

  /// Builds the Overview state projection from the application's Redux store.
  /// @param store The Redux store containing the live-state slice.
  /// @return A ViewModel with every domain's value and synchronization status.
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
