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

/// Carries the latest Vitals synchronization value from the SDK.
class CharacterVitalsSynchronizationChangedAction extends Equatable {
  /// Creates a Vitals synchronization action.
  /// @param synchronization The public SDK Vitals synchronization value.
  const CharacterVitalsSynchronizationChangedAction(this.synchronization);

  /// The SDK-owned Vitals synchronization value.
  final StateSynchronization<CharacterVitalsState> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest XP synchronization value from the SDK.
class CharacterXpSynchronizationChangedAction extends Equatable {
  /// Creates an XP synchronization action.
  /// @param synchronization The public SDK XP synchronization value.
  const CharacterXpSynchronizationChangedAction(this.synchronization);

  /// The SDK-owned XP synchronization value.
  final StateSynchronization<CharacterXpState> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Level synchronization value from the SDK.
class CharacterLevelSynchronizationChangedAction extends Equatable {
  /// Creates a Level synchronization action.
  /// @param synchronization The public SDK Level synchronization value.
  const CharacterLevelSynchronizationChangedAction(this.synchronization);

  /// The SDK-owned Level synchronization value.
  final StateSynchronization<CharacterLevelState> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Identity synchronization value from the SDK.
class CharacterIdentitySynchronizationChangedAction extends Equatable {
  /// Creates an Identity synchronization action.
  /// @param synchronization The public SDK Identity synchronization value.
  const CharacterIdentitySynchronizationChangedAction(this.synchronization);

  /// The SDK-owned Identity synchronization value.
  final StateSynchronization<CharacterIdentityState?> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Supernatural Traits synchronization value from the SDK.
class CharacterSupernaturalTraitsSynchronizationChangedAction
    extends Equatable {
  /// Creates a Supernatural Traits synchronization action.
  /// @param synchronization The public SDK traits synchronization value.
  const CharacterSupernaturalTraitsSynchronizationChangedAction(
    this.synchronization,
  );

  /// The SDK-owned Supernatural Traits synchronization value.
  final StateSynchronization<CharacterSupernaturalTraitsState?> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Location synchronization value from the SDK.
class PlayerLocationSynchronizationChangedAction extends Equatable {
  /// Creates a Location synchronization action.
  /// @param synchronization The public SDK Location synchronization value.
  const PlayerLocationSynchronizationChangedAction(this.synchronization);

  /// The SDK-owned Location synchronization value.
  final StateSynchronization<PlayerLocationState?> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the latest Game Time synchronization value from the SDK.
class GameTimeSynchronizationChangedAction extends Equatable {
  /// Creates a Game Time synchronization action.
  /// @param synchronization The public SDK Game Time synchronization value.
  const GameTimeSynchronizationChangedAction(this.synchronization);

  /// The SDK-owned Game Time synchronization value.
  final StateSynchronization<GameTimeState?> synchronization;

  /// See [Equatable.props].
  @override
  List<Object?> get props => [synchronization];
}

/// Carries the complete Tracked Quests synchronization value from the SDK.
class TrackedQuestsSynchronizationChangedAction extends Equatable {
  /// Creates a Tracked Quests synchronization action.
  /// @param synchronization The complete public SDK synchronization value.
  const TrackedQuestsSynchronizationChangedAction(this.synchronization);

  /// The SDK-owned complete Tracked Quests synchronization value.
  final StateSynchronization<TrackedQuestsState?> synchronization;

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
