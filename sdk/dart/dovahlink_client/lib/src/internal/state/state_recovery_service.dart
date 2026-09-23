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
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Requests authoritative baselines when one domain's revision tracker becomes stale.
abstract interface class IStateRecoveryService<T> {
  /// Starts listening for stale/recovering transitions from the domain tracker.
  void start();

  /// Requests and reconciles authoritative Snapshots until recovery completes or fails.
  /// Concurrent calls share the same in-flight recovery.
  /// @return The current recovery operation.
  Future<void> recover();
}

/// Implements correlated `snapshot_request` recovery for one state domain.
class StateRecoveryService<T> implements IStateRecoveryService<T> {
  /// The registered state area this service recovers.
  final String _stateArea;

  /// Validates, buffers, and applies authoritative state revisions.
  final IStateRevisionTracker<T> _tracker;

  /// Sends the correlated recovery request.
  final IRequestService _requestService;

  /// Reports a failed recovery through connection lifecycle handling.
  final ISessionService _sessionService;

  /// Decodes one registered area's canonical `data` object into typed state.
  final ({T value, bool isUnavailable}) Function(JsonMap data) _decodeState;

  /// Tracker state subscription used to coordinate recovery requests.
  StreamSubscription<StateSynchronization<T>>? _stateChanges;

  /// Whether a stale transition arrived during the active Snapshot request.
  bool _restartAfterSnapshot = false;

  /// The single in-flight recovery operation, when any.
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

  /// See [IStateRecoveryService.start].
  @override
  void start() {
    if (_stateChanges != null) {
      return;
    }
    _stateChanges = _tracker.changes.listen((StateSynchronization<T> _) {
      final DovahLinkStateStatus status = _tracker.current.status;
      if (_recoveryTask != null) {
        if (status == DovahLinkStateStatus.stale &&
            _tracker.recoveryBufferOverflowed) {
          _restartAfterSnapshot = true;
        }
        return;
      }
      if (status == DovahLinkStateStatus.stale ||
          status == DovahLinkStateStatus.recovering) {
        unawaited(recover());
      }
    });
    final DovahLinkStateStatus currentStatus = _tracker.current.status;
    if (currentStatus == DovahLinkStateStatus.stale ||
        currentStatus == DovahLinkStateStatus.recovering) {
      unawaited(recover());
    }
  }

  /// See [IStateRecoveryService.recover].
  @override
  Future<void> recover() async {
    if (_recoveryTask != null) {
      await _recoveryTask;
      return;
    }
    final Completer<void> completion = Completer<void>();
    _recoveryTask = completion.future;
    _tracker.beginRecovery();
    try {
      while (true) {
        _restartAfterSnapshot = false;
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
          if (error.code == ProtocolErrorCode.malformedMessage) {
            _tracker.failRecovery();
            _sessionService.onProtocolViolation(
              error,
              orphanRetrySafeOperations: false,
            );
            return;
          }
          final DovahLinkStateStatus status = _tracker.current.status;
          if (status == DovahLinkStateStatus.synchronized ||
              status == DovahLinkStateStatus.unavailable) {
            return;
          }
          _tracker.failRecovery();
          if (error.retryable &&
              _sessionService.connectionState ==
                  DovahLinkConnectionState.connected) {
            _sessionService.onUnhealthy(error);
          }
          return;
        } on Exception catch (error) {
          final DovahLinkStateStatus status = _tracker.current.status;
          if (status == DovahLinkStateStatus.synchronized ||
              status == DovahLinkStateStatus.unavailable) {
            return;
          }
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
          _tracker.beginRecovery();
          continue;
        }

        final StateSynchronization<T> current = _tracker.current;
        if ((current.status == DovahLinkStateStatus.synchronized ||
                current.status == DovahLinkStateStatus.unavailable) &&
            (current.stateAuthorityId != envelope.stateAuthorityId ||
                current.playContextId != envelope.playContextId ||
                (current.revision != null &&
                    current.revision! >= payload.revision))) {
          return;
        }

        final bool accepted = _tracker.applySnapshot(
          stateAuthorityId: envelope.stateAuthorityId!,
          playContextId: envelope.playContextId,
          revision: payload.revision,
          value: decoded.value,
          isUnavailable: decoded.isUnavailable,
        );
        final DovahLinkStateStatus status = _tracker.current.status;
        if (status == DovahLinkStateStatus.stale) {
          _tracker.beginRecovery();
          continue;
        }
        if (status == DovahLinkStateStatus.synchronized ||
            status == DovahLinkStateStatus.unavailable) {
          return;
        }
        if (!accepted) {
          throw DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'Recovery Snapshot did not establish $_stateArea.',
            retryable: false,
          );
        }
        _tracker.beginRecovery();
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
      _restartAfterSnapshot = false;
      _recoveryTask = null;
      completion.complete();
    }
  }
}
