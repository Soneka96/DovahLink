import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/player_location_state.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Owns the independently synchronized player-location state registration.
abstract interface class IPlayerLocationStateModule {
  /// The stream of typed values and synchronization status.
  Stream<StateSynchronization<PlayerLocationState?>> get changes;

  /// The protocol registration passed to the shared state-message handler.
  IStateDomainDefinition<PlayerLocationState?> get domain;
}

/// Composes the player-location tracker and its Snapshot decoder.
class PlayerLocationStateModule implements IPlayerLocationStateModule {
  /// Tracks the available location or explicit unavailability.
  final IStateRevisionTracker<PlayerLocationState?> _tracker;

  /// The typed protocol registration for this state area.
  late final IStateDomainDefinition<PlayerLocationState?> _domain;

  /// Creates the replayable tracker and protocol registration.
  PlayerLocationStateModule()
    : _tracker = StateRevisionTracker<PlayerLocationState?>(
        state: CurrentValueStream<StateSynchronization<PlayerLocationState?>>(
          const StateSynchronization<PlayerLocationState?>.notSubscribed(),
        ),
      ) {
    _domain = StateDomainDefinition<PlayerLocationState?>(
      stateArea: DovahLinkStateArea.playerLocation.protocolValue,
      decode: decodePlayerLocationState,
      tracker: _tracker,
      isUnavailable: (PlayerLocationState? state) => state == null,
    );
  }

  /// Implements [IPlayerLocationStateModule.changes].
  @override
  Stream<StateSynchronization<PlayerLocationState?>> get changes =>
      _tracker.changes;

  /// Implements [IPlayerLocationStateModule.domain].
  @override
  IStateDomainDefinition<PlayerLocationState?> get domain => _domain;
}
