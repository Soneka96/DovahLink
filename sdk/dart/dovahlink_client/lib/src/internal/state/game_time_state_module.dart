import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/game_time_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Owns the independently synchronized Skyrim game-time state registration.
abstract interface class IGameTimeStateModule {
  /// The stream of typed values and synchronization status.
  Stream<StateSynchronization<GameTimeState?>> get changes;

  /// The protocol registration passed to the shared state-message handler.
  IStateDomainDefinition<GameTimeState?> get domain;
}

/// Composes the Skyrim game-time tracker and its Snapshot decoder.
class GameTimeStateModule implements IGameTimeStateModule {
  /// Tracks the available calendar or explicit unavailability.
  final IStateRevisionTracker<GameTimeState?> _tracker;

  /// The typed protocol registration for this state area.
  late final IStateDomainDefinition<GameTimeState?> _domain;

  /// Creates the replayable tracker and protocol registration.
  GameTimeStateModule()
    : _tracker = StateRevisionTracker<GameTimeState?>(
        state: CurrentValueStream<StateSynchronization<GameTimeState?>>(
          const StateSynchronization<GameTimeState?>.notSubscribed(),
        ),
      ) {
    _domain = StateDomainDefinition<GameTimeState?>(
      stateArea: DovahLinkStateArea.gameTime.protocolValue,
      decode: decodeGameTimeState,
      tracker: _tracker,
      isUnavailable: (GameTimeState? state) => state == null,
    );
  }

  /// Implements [IGameTimeStateModule.changes].
  @override
  Stream<StateSynchronization<GameTimeState?>> get changes => _tracker.changes;

  /// Implements [IGameTimeStateModule.domain].
  @override
  IStateDomainDefinition<GameTimeState?> get domain => _domain;
}
