import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/current_value_stream.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Applies authoritative Snapshot and Event revisions for one state domain.
abstract interface class IStateRevisionTracker<T> {
  /// The latest synchronization view.
  /// @return The most recently accepted domain state and synchronization metadata.
  StateSynchronization<T> get current;

  /// Replays the current view to new listeners, then emits every accepted transition.
  /// @return The current-state stream for this domain.
  Stream<StateSynchronization<T>> get changes;

  /// Whether an Event-buffer overflow requires the active recovery Snapshot to be replaced.
  bool get recoveryBufferOverflowed;

  /// Marks the domain as waiting for an authoritative Snapshot.
  void beginRecovery();

  /// Clears cached state after the consumer unsubscribes from this domain.
  void resetToNotSubscribed();

  /// Marks recovery as failed while retaining the last known state as diagnostics.
  void failRecovery();

  /// Accepts a Snapshot baseline, replacing state from a different authority or play context.
  /// @param stateAuthorityId The Host continuity epoch from the envelope.
  /// @param playContextId The active play-context identity from the envelope.
  /// @param revision The non-negative baseline revision.
  /// @param value The typed state-area value, including an explicit unavailable value.
  /// @param isUnavailable Whether the typed value represents legitimate unavailability.
  /// @return Whether this Snapshot established a baseline or resolved recovery.
  bool applySnapshot({
    required String stateAuthorityId,
    required String? playContextId,
    required int revision,
    required T value,
    required bool isUnavailable,
  });

  /// Applies one Event only when it extends the current baseline by exactly one revision.
  /// @param stateAuthorityId The Host continuity epoch from the envelope.
  /// @param playContextId The active play-context identity from the envelope.
  /// @param baseRevision The revision this Event expects the client to hold.
  /// @param revision The Event's non-negative resulting revision.
  /// @param value The complete typed state after the Event.
  /// @param isUnavailable Whether the typed value represents legitimate unavailability.
  /// @return Whether the Event applied, was ignored, or requires a fresh Snapshot.
  StateEventApplyResult applyEvent({
    required String stateAuthorityId,
    required String? playContextId,
    required int baseRevision,
    required int revision,
    required T value,
    required bool isUnavailable,
  });
}

/// Implements revision and identity rules for one independently synchronized state domain.
class StateRevisionTracker<T> implements IStateRevisionTracker<T> {
  /// The current value and replayable change stream, supplied by the composition root.
  final CurrentValueStream<StateSynchronization<T>> _state;

  /// Events held until the next accepted authoritative Snapshot establishes their baseline.
  final List<
    ({
      String stateAuthorityId,
      String? playContextId,
      int baseRevision,
      int revision,
      T value,
      bool isUnavailable,
    })
  >
  _bufferedEvents = [];

  /// Whether the current buffer was abandoned because it reached its capacity.
  bool _recoveryBufferOverflowed = false;

  /// Creates a tracker over the domain's current-state stream.
  /// @param state The state stream seeded with this domain's initial status.
  StateRevisionTracker({
    required CurrentValueStream<StateSynchronization<T>> state,
  }) : _state = state;

  /// See [IStateRevisionTracker.current].
  @override
  StateSynchronization<T> get current => _state.value;

  /// See [IStateRevisionTracker.changes].
  @override
  Stream<StateSynchronization<T>> get changes => _state.stream;

  /// See [IStateRevisionTracker.recoveryBufferOverflowed].
  @override
  bool get recoveryBufferOverflowed => _recoveryBufferOverflowed;

  /// See [IStateRevisionTracker.beginRecovery].
  @override
  void beginRecovery() {
    final StateSynchronization<T> previous = current;
    if (previous.status == DovahLinkStateStatus.recovering &&
        !_recoveryBufferOverflowed) {
      return;
    }
    if (_recoveryBufferOverflowed) {
      _bufferedEvents.clear();
      _recoveryBufferOverflowed = false;
    }
    _state.update(
      StateSynchronization<T>(
        status: DovahLinkStateStatus.recovering,
        value: previous.value,
        stateAuthorityId: previous.stateAuthorityId,
        playContextId: previous.playContextId,
        revision: previous.revision,
      ),
    );
  }

  /// See [IStateRevisionTracker.resetToNotSubscribed].
  @override
  void resetToNotSubscribed() {
    _bufferedEvents.clear();
    _recoveryBufferOverflowed = false;
    if (current.status == DovahLinkStateStatus.notSubscribed) {
      return;
    }
    _state.update(StateSynchronization<T>.notSubscribed());
  }

  /// See [IStateRevisionTracker.failRecovery].
  @override
  void failRecovery() {
    final StateSynchronization<T> previous = current;
    _bufferedEvents.clear();
    _recoveryBufferOverflowed = false;
    if (previous.status == DovahLinkStateStatus.failed) {
      return;
    }
    _state.update(
      StateSynchronization<T>(
        status: DovahLinkStateStatus.failed,
        value: previous.value,
        stateAuthorityId: previous.stateAuthorityId,
        playContextId: previous.playContextId,
        revision: previous.revision,
      ),
    );
  }

  /// See [IStateRevisionTracker.applySnapshot].
  @override
  bool applySnapshot({
    required String stateAuthorityId,
    required String? playContextId,
    required int revision,
    required T value,
    required bool isUnavailable,
  }) {
    if (_recoveryBufferOverflowed) {
      return false;
    }
    final StateSynchronization<T> previous = current;
    final bool sameIdentity =
        previous.stateAuthorityId == stateAuthorityId &&
        previous.playContextId == playContextId;
    final int? previousRevision = previous.revision;
    if (sameIdentity && previousRevision != null) {
      if (revision < previousRevision) {
        return false;
      }
      if (revision == previousRevision &&
          previous.status != DovahLinkStateStatus.stale &&
          previous.status != DovahLinkStateStatus.recovering &&
          previous.status != DovahLinkStateStatus.failed) {
        return false;
      }
    }

    _state.update(
      StateSynchronization<T>(
        status: isUnavailable
            ? DovahLinkStateStatus.unavailable
            : DovahLinkStateStatus.synchronized,
        value: value,
        stateAuthorityId: stateAuthorityId,
        playContextId: playContextId,
        revision: revision,
      ),
    );

    final List<
      ({
        String stateAuthorityId,
        String? playContextId,
        int baseRevision,
        int revision,
        T value,
        bool isUnavailable,
      })
    >
    bufferedEvents = List.of(_bufferedEvents);
    _bufferedEvents.clear();
    for (final event in bufferedEvents) {
      if (event.stateAuthorityId != stateAuthorityId ||
          event.playContextId != playContextId ||
          event.revision <= current.revision!) {
        continue;
      }
      if (applyEvent(
            stateAuthorityId: event.stateAuthorityId,
            playContextId: event.playContextId,
            baseRevision: event.baseRevision,
            revision: event.revision,
            value: event.value,
            isUnavailable: event.isUnavailable,
          ) ==
          StateEventApplyResult.recoveryRequired) {
        return false;
      }
    }
    return true;
  }

  /// See [IStateRevisionTracker.applyEvent].
  @override
  StateEventApplyResult applyEvent({
    required String stateAuthorityId,
    required String? playContextId,
    required int baseRevision,
    required int revision,
    required T value,
    required bool isUnavailable,
  }) {
    final StateSynchronization<T> previous = current;
    final bool sameIdentity =
        previous.stateAuthorityId == stateAuthorityId &&
        previous.playContextId == playContextId;
    final int? previousRevision = previous.revision;
    if (sameIdentity && previous.status == DovahLinkStateStatus.failed) {
      return StateEventApplyResult.ignored;
    }

    if (previous.status == DovahLinkStateStatus.stale ||
        previous.status == DovahLinkStateStatus.recovering) {
      if (_recoveryBufferOverflowed) {
        return StateEventApplyResult.recoveryRequired;
      }
      if (!sameIdentity) {
        _bufferedEvents.clear();
        _state.update(
          StateSynchronization<T>(
            status: DovahLinkStateStatus.recovering,
            value: null,
            stateAuthorityId: stateAuthorityId,
            playContextId: playContextId,
            revision: null,
          ),
        );
      } else if (previousRevision != null && revision <= previousRevision) {
        return StateEventApplyResult.ignored;
      }

      if (_bufferedEvents.length == kStateRecoveryEventBufferLimit) {
        _bufferedEvents.clear();
        _recoveryBufferOverflowed = true;
        _state.update(
          StateSynchronization<T>(
            status: DovahLinkStateStatus.stale,
            value: current.value,
            stateAuthorityId: current.stateAuthorityId,
            playContextId: current.playContextId,
            revision: current.revision,
          ),
        );
        return StateEventApplyResult.recoveryRequired;
      }
      _bufferedEvents.add((
        stateAuthorityId: stateAuthorityId,
        playContextId: playContextId,
        baseRevision: baseRevision,
        revision: revision,
        value: value,
        isUnavailable: isUnavailable,
      ));
      return StateEventApplyResult.buffered;
    }

    if (!sameIdentity || previousRevision == null) {
      _state.update(
        StateSynchronization<T>(
          status: DovahLinkStateStatus.recovering,
          value: null,
          stateAuthorityId: stateAuthorityId,
          playContextId: playContextId,
          revision: null,
        ),
      );
      _bufferedEvents.add((
        stateAuthorityId: stateAuthorityId,
        playContextId: playContextId,
        baseRevision: baseRevision,
        revision: revision,
        value: value,
        isUnavailable: isUnavailable,
      ));
      return StateEventApplyResult.recoveryRequired;
    }

    if (revision != baseRevision + 1) {
      if (previous.status == DovahLinkStateStatus.synchronized ||
          previous.status == DovahLinkStateStatus.unavailable) {
        _state.update(
          StateSynchronization<T>(
            status: DovahLinkStateStatus.stale,
            value: previous.value,
            stateAuthorityId: stateAuthorityId,
            playContextId: playContextId,
            revision: previousRevision,
          ),
        );
      }
      return StateEventApplyResult.recoveryRequired;
    }

    if (revision <= previousRevision) {
      return StateEventApplyResult.ignored;
    }

    if (previous.status != DovahLinkStateStatus.synchronized &&
        previous.status != DovahLinkStateStatus.unavailable) {
      return StateEventApplyResult.ignored;
    }

    if (baseRevision != previousRevision) {
      _state.update(
        StateSynchronization<T>(
          status: DovahLinkStateStatus.stale,
          value: previous.value,
          stateAuthorityId: stateAuthorityId,
          playContextId: playContextId,
          revision: previousRevision,
        ),
      );
      _bufferedEvents.add((
        stateAuthorityId: stateAuthorityId,
        playContextId: playContextId,
        baseRevision: baseRevision,
        revision: revision,
        value: value,
        isUnavailable: isUnavailable,
      ));
      return StateEventApplyResult.recoveryRequired;
    }

    _state.update(
      StateSynchronization<T>(
        status: isUnavailable
            ? DovahLinkStateStatus.unavailable
            : DovahLinkStateStatus.synchronized,
        value: value,
        stateAuthorityId: stateAuthorityId,
        playContextId: playContextId,
        revision: revision,
      ),
    );
    return StateEventApplyResult.applied;
  }
}
