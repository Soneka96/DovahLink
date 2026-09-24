import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/snapshot_request_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/constants.dart';
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
  /// Owns the state identity, typed policy, and revision tracker being recovered.
  final IStateDomainDefinition<T> _domain;

  /// Sends the correlated recovery request.
  final IRequestService _requestService;

  /// Reports a failed recovery through connection lifecycle handling.
  final ISessionService _sessionService;

  /// Delay before each recovery attempt; the first attempt runs immediately.
  final List<Duration> _retryDelays;

  /// Tracker state subscription used to coordinate recovery requests.
  StreamSubscription<StateSynchronization<T>>? _stateChanges;

  /// Whether a stale transition arrived during the active Snapshot request.
  bool _restartAfterSnapshot = false;

  /// The single in-flight recovery operation, when any.
  Future<void>? _recoveryTask;

  /// Creates recovery using one registered domain's typed state policy.
  /// @param domain The registered area, decoder, availability policy, and tracker.
  /// @param requestService The authenticated client request boundary.
  /// @param sessionService The connection lifecycle boundary used after recovery failure.
  /// @param retryDelays The bounded retry schedule, starting with the immediate attempt.
  StateRecoveryService({
    required IStateDomainDefinition<T> domain,
    required IRequestService requestService,
    required ISessionService sessionService,
    List<Duration> retryDelays = kReconnectAttemptDelays,
  }) : _domain = domain,
       _requestService = requestService,
       _sessionService = sessionService,
       _retryDelays = retryDelays;

  /// See [IStateRecoveryService.start].
  @override
  void start() {
    if (_stateChanges != null) {
      return;
    }
    _stateChanges = _domain.tracker.changes.listen((StateSynchronization<T> _) {
      final StateSynchronization<T> current = _domain.tracker.current;
      if (_recoveryTask != null) {
        if (current.status == DovahLinkStateStatus.stale &&
            _domain.tracker.recoveryBufferOverflowed) {
          _restartAfterSnapshot = true;
        }
        return;
      }
      if (current.status == DovahLinkStateStatus.stale ||
          (current.status == DovahLinkStateStatus.recovering &&
              current.stateAuthorityId != null)) {
        unawaited(recover());
      }
    });
    final StateSynchronization<T> current = _domain.tracker.current;
    if (current.status == DovahLinkStateStatus.stale ||
        (current.status == DovahLinkStateStatus.recovering &&
            current.stateAuthorityId != null)) {
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
    if (_domainIsNotSubscribed) {
      return;
    }
    final Completer<void> completion = Completer<void>();
    _recoveryTask = completion.future;
    _domain.tracker.beginRecovery();
    int retryIndex = 0;
    try {
      while (true) {
        if (_domainIsNotSubscribed) {
          return;
        }
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
              stateArea: _domain.stateArea,
              knownRevision: _domain.tracker.current.revision,
            ).toJson(),
            expectedType: ProtocolMessageType.stateSnapshot,
            policy: RequestPolicy(
              retrySafe: true,
              requiredTrustState: trustState,
              timeoutClass: TimeoutClass.normal,
            ),
          );
        } on DovahLinkProtocolException catch (error) {
          if (_domainIsNotSubscribed) {
            return;
          }
          if (error.code == ProtocolErrorCode.malformedMessage) {
            _domain.tracker.failRecovery();
            _sessionService.onProtocolViolation(
              error,
              orphanRetrySafeOperations: false,
            );
            return;
          }
          final DovahLinkStateStatus status = _domain.tracker.current.status;
          if (status == DovahLinkStateStatus.synchronized ||
              status == DovahLinkStateStatus.unavailable) {
            return;
          }
          if (error.code == ProtocolErrorCode.temporarilyUnavailable &&
              error.retryable) {
            if (retryIndex + 1 < _retryDelays.length) {
              retryIndex++;
              await Future<void>.delayed(_retryDelays[retryIndex]);
              continue;
            }
            _domain.tracker.failRecovery();
            return;
          }
          _domain.tracker.failRecovery();
          if (error.retryable &&
              _sessionService.connectionState ==
                  DovahLinkConnectionState.connected) {
            _sessionService.onUnhealthy(error);
          }
          return;
        } on Exception catch (error) {
          if (_domainIsNotSubscribed) {
            return;
          }
          final DovahLinkStateStatus status = _domain.tracker.current.status;
          if (status == DovahLinkStateStatus.synchronized ||
              status == DovahLinkStateStatus.unavailable) {
            return;
          }
          _domain.tracker.failRecovery();
          if (_sessionService.connectionState ==
              DovahLinkConnectionState.connected) {
            _sessionService.onUnhealthy(error);
          }
          return;
        }

        if (_domainIsNotSubscribed) {
          return;
        }
        final StateSnapshotPayload payload = ProtocolPayloadDecoder.decode(
          StateSnapshotPayload.fromJson,
          envelope.payload,
        );
        if (payload.stateArea != _domain.stateArea) {
          throw DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message:
                'Received a snapshot for ${payload.stateArea} while recovering '
                '${_domain.stateArea}.',
            retryable: false,
          );
        }
        final ({T value, bool isUnavailable}) decoded = _domain.decodeState(
          payload.data,
        );

        if (_restartAfterSnapshot) {
          _domain.tracker.beginRecovery();
          continue;
        }

        final StateSynchronization<T> current = _domain.tracker.current;
        if ((current.status == DovahLinkStateStatus.synchronized ||
                current.status == DovahLinkStateStatus.unavailable) &&
            (current.stateAuthorityId != envelope.stateAuthorityId ||
                current.playContextId != envelope.playContextId ||
                (current.revision != null &&
                    current.revision! >= payload.revision))) {
          return;
        }

        final bool accepted = _domain.tracker.applySnapshot(
          stateAuthorityId: envelope.stateAuthorityId!,
          playContextId: envelope.playContextId,
          revision: payload.revision,
          value: decoded.value,
          isUnavailable: decoded.isUnavailable,
        );
        final DovahLinkStateStatus status = _domain.tracker.current.status;
        if (status == DovahLinkStateStatus.notSubscribed) {
          return;
        }
        if (status == DovahLinkStateStatus.stale) {
          _domain.tracker.beginRecovery();
          continue;
        }
        if (status == DovahLinkStateStatus.synchronized ||
            status == DovahLinkStateStatus.unavailable) {
          return;
        }
        if (!accepted) {
          throw DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message:
                'Recovery Snapshot did not establish ${_domain.stateArea}.',
            retryable: false,
          );
        }
        _domain.tracker.beginRecovery();
      }
    } on DovahLinkProtocolException catch (error) {
      if (_domainIsNotSubscribed) {
        return;
      }
      _domain.tracker.failRecovery();
      _sessionService.onProtocolViolation(
        error,
        orphanRetrySafeOperations: false,
      );
    } on Exception catch (error) {
      if (_domainIsNotSubscribed) {
        return;
      }
      _domain.tracker.failRecovery();
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

  /// Whether the subscription gate has removed this domain during recovery.
  bool get _domainIsNotSubscribed =>
      _domain.tracker.current.status == DovahLinkStateStatus.notSubscribed;
}
