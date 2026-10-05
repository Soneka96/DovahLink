import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        CharacterIdentityState,
        CharacterLevelState,
        CharacterSupernaturalTraitsState,
        CharacterVitalsState,
        CharacterXpState,
        DovahLinkStateStatus,
        GameTimeState,
        PlayerLocationCellKind,
        PlayerLocationState,
        QuestObjective,
        StateSynchronization,
        TrackedQuest,
        TrackedQuestObjectiveState,
        TrackedQuestsState;

/// Projects public SDK synchronization values into app-owned live-state values.
abstract final class LiveStateMapper {
  /// Maps the coherent Vitals state without clamping resource values.
  /// @param synchronization The SDK Vitals stream value.
  /// @return App-owned Vitals data and synchronization metadata.
  static LiveDomainState<
    ({
      ({double current, double max}) health,
      ({double current, double max}) magicka,
      ({double current, double max}) stamina,
    })
  >
  characterVitals(StateSynchronization<CharacterVitalsState> synchronization) {
    final CharacterVitalsState? value = synchronization.value;
    final health = value?.health;
    final magicka = value?.magicka;
    final stamina = value?.stamina;
    return _project(
      synchronization,
      value: health == null || magicka == null || stamina == null
          ? null
          : (
              health: (current: health.current, max: health.max),
              magicka: (current: magicka.current, max: magicka.max),
              stamina: (current: stamina.current, max: stamina.max),
            ),
    );
  }

  /// Maps the exact XP value without deriving a progress percentage.
  /// @param synchronization The SDK XP stream value.
  /// @return App-owned XP data and synchronization metadata.
  static LiveDomainState<double?> characterXp(
    StateSynchronization<CharacterXpState> synchronization,
  ) => _project(synchronization, value: synchronization.value?.value);

  /// Maps the latest character level.
  /// @param synchronization The SDK Level stream value.
  /// @return App-owned Level data and synchronization metadata.
  static LiveDomainState<int?> characterLevel(
    StateSynchronization<CharacterLevelState> synchronization,
  ) => _project(synchronization, value: synchronization.value?.value);

  /// Maps the complete player identity when the SDK reports it as available.
  /// @param synchronization The SDK Identity stream value.
  /// @return App-owned identity data and synchronization metadata.
  static LiveDomainState<({String name, String race})?> characterIdentity(
    StateSynchronization<CharacterIdentityState?> synchronization,
  ) {
    final CharacterIdentityState? value = synchronization.value;
    return _project(
      synchronization,
      value: value == null ? null : (name: value.name, race: value.race),
    );
  }

  /// Maps the three independent supernatural predicates.
  /// @param synchronization The SDK Supernatural Traits stream value.
  /// @return App-owned predicates and synchronization metadata.
  static LiveDomainState<
    ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
  >
  supernaturalTraits(
    StateSynchronization<CharacterSupernaturalTraitsState?> synchronization,
  ) {
    final CharacterSupernaturalTraitsState? value = synchronization.value;
    return _project(
      synchronization,
      value: value == null
          ? null
          : (
              isVampire: value.isVampire,
              hasVampireLordForm: value.hasVampireLordForm,
              hasWerewolfForm: value.hasWerewolfForm,
            ),
    );
  }

  /// Maps distinct cell, selected-location, and worldspace facts.
  /// @param synchronization The SDK Location stream value.
  /// @return App-owned location data and synchronization metadata.
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
  playerLocation(StateSynchronization<PlayerLocationState?> synchronization) {
    final PlayerLocationState? value = synchronization.value;
    return _project(
      synchronization,
      value: value == null
          ? null
          : (
              cellId: value.cellId,
              cellKind: _cellKind(value.cellKind),
              cellName: value.cellName,
              locationId: value.locationId,
              locationName: value.locationName,
              worldspaceId: value.worldspaceId,
              worldspaceName: value.worldspaceName,
            ),
    );
  }

  /// Maps Skyrim calendar fields without converting them into a system date.
  /// @param synchronization The SDK Game Time stream value.
  /// @return App-owned calendar data and synchronization metadata.
  static LiveDomainState<
    ({int year, int month, String monthName, int day, int hour, int minute})?
  >
  gameTime(StateSynchronization<GameTimeState?> synchronization) {
    final GameTimeState? value = synchronization.value;
    return _project(
      synchronization,
      value: value == null
          ? null
          : (
              year: value.year,
              month: value.month,
              monthName: value.monthName,
              day: value.day,
              hour: value.hour,
              minute: value.minute,
            ),
    );
  }

  /// Maps the entire tracked-quest collection in SDK order.
  /// @param synchronization The SDK Tracked Quests stream value.
  /// @return App-owned quests and synchronization metadata.
  static LiveDomainState<List<LiveTrackedQuest>> trackedQuests(
    StateSynchronization<TrackedQuestsState?> synchronization,
  ) {
    final TrackedQuestsState? value = synchronization.value;
    return _project(
      synchronization,
      value: value == null
          ? null
          : List<LiveTrackedQuest>.unmodifiable(
              value.quests.map(_trackedQuest),
            ),
    );
  }

  /// Copies synchronization metadata while mapping the status to an app-owned enum.
  /// @param synchronization The SDK domain status and authority metadata.
  /// @param value The mapped app-owned domain value.
  /// @return The app-owned status and value projection.
  static LiveDomainState<T> _project<T>(
    StateSynchronization<Object?> synchronization, {
    required T? value,
  }) => LiveDomainState<T>(
    status: _status(synchronization.status),
    value: value,
    stateAuthorityId: synchronization.stateAuthorityId,
    playContextId: synchronization.playContextId,
    revision: synchronization.revision,
  );

  /// Maps one SDK quest and all of its objective instances.
  /// @param quest The SDK quest value.
  /// @return An app-owned quest projection.
  static LiveTrackedQuest _trackedQuest(TrackedQuest quest) => LiveTrackedQuest(
    questId: quest.questId,
    title: quest.title,
    type: quest.type,
    objectives: quest.objectives.map(_objective).toList(growable: false),
  );

  /// Maps one SDK objective instance without selecting a canonical objective.
  /// @param objective The SDK objective value.
  /// @return An app-owned objective record.
  static ({
    int index,
    int instanceId,
    String? text,
    LiveQuestObjectiveStatus status,
  })
  _objective(QuestObjective objective) => (
    index: objective.index,
    instanceId: objective.instanceId,
    text: objective.text,
    status: _objectiveStatus(objective.state),
  );

  /// Maps the SDK synchronization status without collapsing distinct cases.
  /// @param status The SDK domain synchronization status.
  /// @return The matching app-owned status.
  static LiveStateStatus _status(DovahLinkStateStatus status) =>
      switch (status) {
        DovahLinkStateStatus.notSubscribed => LiveStateStatus.notSubscribed,
        DovahLinkStateStatus.unavailable => LiveStateStatus.unavailable,
        DovahLinkStateStatus.synchronized => LiveStateStatus.synchronized,
        DovahLinkStateStatus.stale => LiveStateStatus.stale,
        DovahLinkStateStatus.recovering => LiveStateStatus.recovering,
        DovahLinkStateStatus.failed => LiveStateStatus.failed,
      };

  /// Maps the SDK cell classification into app-owned state.
  /// @param kind The SDK cell kind.
  /// @return The matching app-owned cell kind.
  static LiveCellKind _cellKind(PlayerLocationCellKind kind) => switch (kind) {
    PlayerLocationCellKind.interior => LiveCellKind.interior,
    PlayerLocationCellKind.exterior => LiveCellKind.exterior,
  };

  /// Maps the SDK objective state into app-owned state.
  /// @param state The SDK objective state.
  /// @return The matching app-owned objective status.
  static LiveQuestObjectiveStatus _objectiveStatus(
    TrackedQuestObjectiveState state,
  ) => switch (state) {
    TrackedQuestObjectiveState.dormant => LiveQuestObjectiveStatus.dormant,
    TrackedQuestObjectiveState.displayed => LiveQuestObjectiveStatus.displayed,
    TrackedQuestObjectiveState.completed => LiveQuestObjectiveStatus.completed,
    TrackedQuestObjectiveState.completedAndDisplayed =>
      LiveQuestObjectiveStatus.completedAndDisplayed,
    TrackedQuestObjectiveState.failed => LiveQuestObjectiveStatus.failed,
    TrackedQuestObjectiveState.failedAndDisplayed =>
      LiveQuestObjectiveStatus.failedAndDisplayed,
  };
}
