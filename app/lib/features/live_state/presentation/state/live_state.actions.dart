import 'package:equatable/equatable.dart';

import 'package:dovahlink_client/features/live_state/presentation/state/live_domain_state.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_state_enums.dart';
import 'package:dovahlink_client/features/live_state/presentation/state/live_tracked_quest.dart';

/// Carries the latest coherent Vitals synchronization projection.
class CharacterVitalsSynchronizationChangedAction extends Equatable {
  /// Creates a Vitals synchronization action.
  /// @param synchronization The complete Vitals projection.
  const CharacterVitalsSynchronizationChangedAction(this.synchronization);

  /// The complete typed Vitals state and synchronization metadata.
  final LiveDomainState<
    ({
      ({double current, double max}) health,
      ({double current, double max}) magicka,
      ({double current, double max}) stamina,
    })
  >
  synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest XP synchronization projection.
class CharacterXpSynchronizationChangedAction extends Equatable {
  /// Creates an XP synchronization action.
  /// @param synchronization The XP value and synchronization metadata.
  const CharacterXpSynchronizationChangedAction(this.synchronization);

  /// The exact numeric XP value and synchronization metadata.
  final LiveDomainState<double?> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Level synchronization projection.
class CharacterLevelSynchronizationChangedAction extends Equatable {
  /// Creates a Level synchronization action.
  /// @param synchronization The Level value and synchronization metadata.
  const CharacterLevelSynchronizationChangedAction(this.synchronization);

  /// The latest level and synchronization metadata.
  final LiveDomainState<int?> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Identity synchronization projection.
class CharacterIdentitySynchronizationChangedAction extends Equatable {
  /// Creates an Identity synchronization action.
  /// @param synchronization The complete identity and synchronization metadata.
  const CharacterIdentitySynchronizationChangedAction(this.synchronization);

  /// The complete identity value and synchronization metadata.
  final LiveDomainState<({String name, String race})?> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Supernatural Traits synchronization projection.
class CharacterSupernaturalTraitsSynchronizationChangedAction
    extends Equatable {
  /// Creates a Supernatural Traits synchronization action.
  /// @param synchronization The independent predicates and synchronization metadata.
  const CharacterSupernaturalTraitsSynchronizationChangedAction(
    this.synchronization,
  );

  /// The independent predicates and synchronization metadata.
  final LiveDomainState<
    ({bool isVampire, bool hasVampireLordForm, bool hasWerewolfForm})
  >
  synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Location synchronization projection.
class PlayerLocationSynchronizationChangedAction extends Equatable {
  /// Creates a Location synchronization action.
  /// @param synchronization The location facts and synchronization metadata.
  const PlayerLocationSynchronizationChangedAction(this.synchronization);

  /// The distinct cell, selected-location, and worldspace facts.
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
  synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Skyrim calendar synchronization projection.
class GameTimeSynchronizationChangedAction extends Equatable {
  /// Creates a Game Time synchronization action.
  /// @param synchronization The Skyrim calendar and synchronization metadata.
  const GameTimeSynchronizationChangedAction(this.synchronization);

  /// The Skyrim calendar fields and synchronization metadata.
  final LiveDomainState<
    ({int year, int month, String monthName, int day, int hour, int minute})?
  >
  synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the complete tracked-quest synchronization projection.
class TrackedQuestsSynchronizationChangedAction extends Equatable {
  /// Creates a Tracked Quests synchronization action.
  /// @param synchronization The complete tracked-quest projection.
  const TrackedQuestsSynchronizationChangedAction(this.synchronization);

  /// Every tracked quest and its objective instances, or an authoritative empty list.
  final LiveDomainState<List<LiveTrackedQuest>> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Resets every projected domain after an admitted session truly ends.
class SessionLiveStateResetAction extends Equatable {
  /// Creates a live-state reset action.
  const SessionLiveStateResetAction();

  /// See [Equatable.props].
  @override
  List<Object?> get props => const <Object?>[];
}
