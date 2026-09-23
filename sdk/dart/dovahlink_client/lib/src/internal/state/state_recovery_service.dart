import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/snapshot_request_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Coordinates bounded Event buffering and authoritative Snapshot recovery for one state domain.
abstract interface class IStateRecoveryService<T> {
  /// Accepts a Host-pushed Snapshot when no client-requested recovery is in flight.
  /// @param stateArea The canonical area named by the decoded payload.
  /// @param stateAuthorityId The Host continuity epoch from the envelope.
  /// @param playContextId The active play-context identity from the envelope.
  /// @param revision The non-negative snapshot revision.
  /// @param value The typed state-area value.
  /// @param isUnavailable Whether the value represents legitimate unavailability.
  void handleSnapshot({
    required String stateArea,
    required String stateAuthorityId,
    required String? playContextId,
    required int revision,
    required T value,
    required bool isUnavailable,
  });

  /// Applies an Event or starts recovery when the current baseline cannot accept it.
  /// @param stateAuthorityId The Host continuity epoch from the envelope.
  /// @param playContextId The active play-context identity from the envelope.
  /// @param baseRevision The revision the Event expects the client to hold.
  /// @param revision The Event's resulting revision.
  /// @param value The complete typed state after the Event.
  /// @param isUnavailable Whether the value represents legitimate unavailability.
  /// @return Whether the Event applied, buffered, was ignored, or requires recovery.
  StateEventApplyResult handleEvent({
    required String stateAuthorityId,
    required String? playContextId,
    required int baseRevision,
    required int revision,
    required T value,
    required bool isUnavailable,
  });

  /// Requests and reconciles authoritative Snapshots until this recovery is complete or fails.
  /// Concurrent calls share the same in-flight recovery.
  /// @return The current recovery operation.
  Future<void> recover();
}

/// Implements per-domain recovery through correlated `snapshot_request` operations.
class StateRecoveryService<T> implements IStateRecoveryService<T> {
  /// The registered state area this service recovers.
  final String _stateArea;

  /// Validates and applies authoritative state revisions.
  final IStateRevisionTracker<T> _tracker;

  /// Sends the correlated recovery request.
  final IRequestService _requestService;

  /// Reports a failed recovery through connection lifecycle handling.
  final ISessionService _sessionService;

  /// Decodes one registered area's canonical `data` object into typed state.
  final ({T value, bool isUnavailable}) Function(JsonMap data) _decodeState;

  /// Event updates received while [StateRecoveryService.recover] is pending.
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

  /// Whether the current recovery response must be discarded and followed by a new request.
  bool _restartAfterSnapshot = false;

  /// The single in-flight recovery loop, when any.
  Future<void>? _recoveryTask;

  /// Creates recovery for [_stateArea] using the supplied domain codec and collaborators.
  /// @param stateArea The canonical registered area this service owns.
  /// @param tracker The per-domain revision tracker.
  /// @param requestService The authenticated client request boundary.
  /// @param sessionService The connection lifecycle boundary used after recovery failure.
  /// @param decodeState The pure typed decoder for the area's canonical `data` object.
  StateRecoveryService({
    required String stateArea,
    required IStateRevisionTracker<T> tracker,
    required IRequestService requestService,
    required ISessionService sessionService,
    required ({T value, bool isUnavailable}) Function(JsonMap data) decodeState,
  }) : _stateArea = stateArea,
       _tracker = tracker,
       _requestService = requestService,
       _sessionService = sessionService,
       _decodeState = decodeState;

  /// See [IStateRecoveryService.handleSnapshot].
  @override
  void handleSnapshot({
    required String stateArea,
    required String stateAuthorityId,
    required String? playContextId,
    required int revision,
    required T value,
    required bool isUnavailable,
  }) {
    if (stateArea != _stateArea) {
      _sessionService.onProtocolViolation(
        DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message:
              'Received a snapshot for $stateArea while recovering '
              '$_stateArea.',
          retryable: false,
        ),
        orphanRetrySafeOperations: false,
      );
      return;
    }
    // The correlated Snapshot requested by the active recovery is the only barrier used to
    // resume Events; a concurrent Host-pushed Snapshot cannot settle that request.
    if (_recoveryTask != null) {
      return;
    }
    _tracker.applySnapshot(
      stateAuthorityId: stateAuthorityId,
      playContextId: playContextId,
      revision: revision,
      value: value,
      isUnavailable: isUnavailable,
    );
  }

  /// See [IStateRecoveryService.handleEvent].
  @override
  StateEventApplyResult handleEvent({
    required String stateAuthorityId,
    required String? playContextId,
    required int baseRevision,
    required int revision,
    required T value,
    required bool isUnavailable,
  }) {
    if (_recoveryTask != null) {
      final StateSynchronization<T> current = _tracker.current;
      if (!_restartAfterSnapshot &&
          current.stateAuthorityId == stateAuthorityId &&
          current.playContextId == playContextId &&
          current.revision != null &&
          revision <= current.revision!) {
        return StateEventApplyResult.ignored;
      }
      if (_restartAfterSnapshot) {
        return StateEventApplyResult.recoveryRequired;
      }
      if (_bufferedEvents.length == kStateRecoveryEventBufferLimit) {
        _bufferedEvents.clear();
        _restartAfterSnapshot = true;
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

    final StateEventApplyResult result = _tracker.applyEvent(
      stateAuthorityId: stateAuthorityId,
      playContextId: playContextId,
      baseRevision: baseRevision,
      revision: revision,
      value: value,
      isUnavailable: isUnavailable,
    );
    if (result == StateEventApplyResult.recoveryRequired) {
      unawaited(recover());
    }
    return result;
  }

  /// See [IStateRecoveryService.recover].
  @override
  Future<void> recover() async {
    if (_recoveryTask != null) {
      await _recoveryTask;
      return;
    }
    _tracker.beginRecovery();
    _bufferedEvents.clear();
    _restartAfterSnapshot = false;
    final Completer<void> completion = Completer<void>();
    _recoveryTask = completion.future;
    try {
      while (true) {
        _restartAfterSnapshot = false;
        _bufferedEvents.clear();
        final DovahLinkTrustState? trustState =
            _sessionService.currentTrustState;
        if (trustState == null) {
          throw const DovahLinkConnectionException(
            'Cannot request state recovery without an admitted session.',
          );
        }

        final Envelope envelope;
        try {
          envelope = await _requestService.sendAndAwait(
            messageType: ProtocolMessageType.snapshotRequest,
            payload: SnapshotRequestPayload(
              stateArea: _stateArea,
              knownRevision: _tracker.current.revision,
            ).toJson(),
            expectedType: ProtocolMessageType.stateSnapshot,
            policy: RequestPolicy(
              retrySafe: true,
              requiredTrustState: trustState,
              timeoutClass: TimeoutClass.normal,
            ),
          );
        } on DovahLinkProtocolException catch (error) {
          _tracker.failRecovery();
          if (error.code == ProtocolErrorCode.malformedMessage) {
            _sessionService.onProtocolViolation(
              error,
              orphanRetrySafeOperations: false,
            );
          } else if (error.retryable &&
              _sessionService.connectionState ==
                  DovahLinkConnectionState.connected) {
            _sessionService.onUnhealthy(error);
          }
          return;
        } on Exception catch (error) {
          _tracker.failRecovery();
          if (_sessionService.connectionState ==
              DovahLinkConnectionState.connected) {
            _sessionService.onUnhealthy(error);
          }
          return;
        }

        final StateSnapshotPayload payload = ProtocolPayloadDecoder.decode(
          StateSnapshotPayload.fromJson,
          envelope.payload,
        );
        if (payload.stateArea != _stateArea) {
          throw DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message:
                'Received a snapshot for ${payload.stateArea} while recovering '
                '$_stateArea.',
            retryable: false,
          );
        }
        final ({T value, bool isUnavailable}) decoded =
            ProtocolPayloadDecoder.decode(_decodeState, payload.data);

        if (_restartAfterSnapshot) {
          _bufferedEvents.clear();
          continue;
        }

        final bool accepted = _tracker.applySnapshot(
          stateAuthorityId: envelope.stateAuthorityId!,
          playContextId: envelope.playContextId,
          revision: payload.revision,
          value: decoded.value,
          isUnavailable: decoded.isUnavailable,
        );
        if (!accepted) {
          throw DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message:
                'Recovery Snapshot did not advance or restore $_stateArea.',
            retryable: false,
          );
        }

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
        bool needsAnotherSnapshot = false;
        for (final event in bufferedEvents) {
          if (event.stateAuthorityId != envelope.stateAuthorityId ||
              event.playContextId != envelope.playContextId ||
              event.revision <= payload.revision) {
            continue;
          }
          if (_tracker.applyEvent(
                stateAuthorityId: event.stateAuthorityId,
                playContextId: event.playContextId,
                baseRevision: event.baseRevision,
                revision: event.revision,
                value: event.value,
                isUnavailable: event.isUnavailable,
              ) ==
              StateEventApplyResult.recoveryRequired) {
            _tracker.beginRecovery();
            needsAnotherSnapshot = true;
            break;
          }
        }
        if (needsAnotherSnapshot) {
          continue;
        }
        return;
      }
    } on DovahLinkProtocolException catch (error) {
      _tracker.failRecovery();
      _sessionService.onProtocolViolation(
        error,
        orphanRetrySafeOperations: false,
      );
    } on Exception catch (error) {
      _tracker.failRecovery();
      if (_sessionService.connectionState ==
          DovahLinkConnectionState.connected) {
        _sessionService.onUnhealthy(error);
      }
    } finally {
      _bufferedEvents.clear();
      _restartAfterSnapshot = false;
      _recoveryTask = null;
      completion.complete();
    }
  }
}
