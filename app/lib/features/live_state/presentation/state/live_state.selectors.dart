import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors for projected gameplay synchronization state.
abstract final class LiveStateSelectors {
  /// Returns the complete Vitals synchronization projection.
  /// @param state The application state.
  /// @return The Vitals projection, including status and revision metadata.
  static LiveDomainState<
    ({
      ({double current, double max}) health,
      ({double current, double max}) magicka,
      ({double current, double max}) stamina,
    })
  >
  characterVitalsSelector(AppState state) => state.liveState.characterVitals;

  /// Returns the XP synchronization projection.
  /// @param state The application state.
  /// @return The XP projection, including status and revision metadata.
  static LiveDomainState<double?> characterXpSelector(AppState state) =>
      state.liveState.characterXp;

  /// Returns the Level synchronization projection.
  /// @param state The application state.
  /// @return The Level projection, including status and revision metadata.
  static LiveDomainState<int?> characterLevelSelector(AppState state) =>
      state.liveState.characterLevel;

  /// Returns the Identity synchronization projection.
  /// @param state The application state.
  /// @return The Identity projection, including status and revision metadata.
  static LiveDomainState<({String name, String race})?>
  characterIdentitySelector(AppState state) =>
      state.liveState.characterIdentity;

  /// Returns the Supernatural Traits synchronization projection.
  /// @param state The application state.
  /// @return The Supernatural Traits projection, including status and revision metadata.
  static LiveDomainState<
    ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
  >
  supernaturalTraitsSelector(AppState state) =>
      state.liveState.supernaturalTraits;

  /// Returns the Location synchronization projection.
  /// @param state The application state.
  /// @return The Location projection, including status and revision metadata.
  static LiveDomainState<
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
  playerLocationSelector(AppState state) => state.liveState.playerLocation;

  /// Returns the Skyrim calendar synchronization projection.
  /// @param state The application state.
  /// @return The Game Time projection, including status and revision metadata.
  static LiveDomainState<
    ({int year, int month, String monthName, int day, int hour, int minute})?
  >
  gameTimeSelector(AppState state) => state.liveState.gameTime;

  /// Returns the complete tracked-quest collection and its synchronization projection.
  /// @param state The application state.
  /// @return The Tracked Quests projection, including status and revision metadata.
  static LiveDomainState<List<LiveTrackedQuest>> trackedQuestsSelector(
    AppState state,
  ) => state.liveState.trackedQuests;
}
