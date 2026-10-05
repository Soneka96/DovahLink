import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';
import 'package:dovahlink_client_sdk/src/state/tracked_quests_state.dart';

/// Owns the independently synchronized tracked-quests state registration.
abstract interface class ITrackedQuestsStateModule {
  /// The stream of typed values and synchronization status.
  Stream<StateSynchronization<TrackedQuestsState?>> get changes;

  /// The protocol registration passed to the shared state-message handler.
  IStateDomainDefinition<TrackedQuestsState?> get domain;
}

/// Composes the tracked-quests tracker and its Snapshot decoder.
class TrackedQuestsStateModule implements ITrackedQuestsStateModule {
  /// Tracks the complete available collection or explicit unavailability.
  final IStateRevisionTracker<TrackedQuestsState?> _tracker;

  /// The typed protocol registration for this state area.
  late final IStateDomainDefinition<TrackedQuestsState?> _domain;

  /// Creates the replayable tracker and protocol registration.
  TrackedQuestsStateModule()
    : _tracker = StateRevisionTracker<TrackedQuestsState?>(
        state: CurrentValueStream<StateSynchronization<TrackedQuestsState?>>(
          const StateSynchronization<TrackedQuestsState?>.notSubscribed(),
        ),
      ) {
    _domain = StateDomainDefinition<TrackedQuestsState?>(
      stateArea: DovahLinkStateArea.trackedQuests.protocolValue,
      decode: decodeTrackedQuestsState,
      tracker: _tracker,
      isUnavailable: (TrackedQuestsState? state) => state == null,
    );
  }

  /// Implements [ITrackedQuestsStateModule.changes].
  @override
  Stream<StateSynchronization<TrackedQuestsState?>> get changes =>
      _tracker.changes;

  /// Implements [ITrackedQuestsStateModule.domain].
  @override
  IStateDomainDefinition<TrackedQuestsState?> get domain => _domain;
}
