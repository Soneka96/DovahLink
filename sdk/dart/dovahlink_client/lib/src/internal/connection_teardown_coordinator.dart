import 'dart:async';

import 'package:dovahlink_client_sdk/src/internal/lifecycle_operation_queue.dart';
import 'package:dovahlink_client_sdk/src/internal/session_lifecycle_state.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Coordinates ordered connection-resource cleanup while [SessionLifecycleState] retains
/// ownership of connection state and stream fields.
class ConnectionTeardownCoordinator {
  /// The transport whose resources must be closed.
  final DovahLinkTransport _transport;

  /// Serializes teardown with connect and administrative invalidation cleanup.
  final LifecycleOperationQueue _lifecycleQueue;

  /// Fails or orphans pending operations after resource cleanup completes. A plain callback,
  /// rather than a `PendingOperationFailureHandler` port, so a caller whose own pending-operation
  /// owner does not exist yet at construction time (see `SessionServiceImpl.onTeardown`) can
  /// supply a forwarding closure instead of having to implement that port on itself and pass
  /// itself under a second name.
  final void Function(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  })
  _pendingOperationFailureHandler;

  /// The session-owned state changed by the teardown transition.
  final SessionLifecycleState _state;

  /// Creates a coordinator for one session's transport, lifecycle queue, pending-operation failure
  /// callback, and state port.
  ConnectionTeardownCoordinator({
    required DovahLinkTransport transport,
    required LifecycleOperationQueue lifecycleQueue,
    required void Function(
      Exception reason, {
      required bool orphanRetrySafeOperations,
    })
    pendingOperationFailureHandler,
    required SessionLifecycleState state,
  }) : _transport = transport,
       _lifecycleQueue = lifecycleQueue,
       _pendingOperationFailureHandler = pendingOperationFailureHandler,
       _state = state;

  /// Tears down an unhealthy or deliberately disconnected connection. Ordinary transport loss
  /// may orphan one retry-safe operation; deliberate disconnect and protocol failure can disable
  /// that behavior through [orphanRetrySafeOperations] -- the same flag also controls whether an
  /// already-`reconnecting` session stays `reconnecting` (an intermediate teardown mid-recovery)
  /// or resolves to `disconnected` (a final one), per
  /// [SessionLifecycleState.resetAfterConnectionTeardown]. A no-op if a call queued ahead of this one
  /// already tore down the same connection generation -- for example a stream's `onError` and
  /// `onDone` both firing for one dead subscription -- so the redundant second call cannot double
  /// -run cleanup or a second `failAll`. Does not skip a call made after the connection actually
  /// moved on (a fresh `connect()` bumps the generation itself), so a deliberate, later teardown
  /// -- for example finalizing operations an earlier teardown preserved for retry -- still runs.
  Future<void> tearDown(
    Exception reason, {
    bool orphanRetrySafeOperations = true,
  }) {
    final int callGeneration = _state.connectionGeneration;
    return _lifecycleQueue.run(() async {
      if (_state.isAdministrativelyInvalidated) {
        return;
      }
      if (_state.connectionGeneration != callGeneration) {
        return;
      }
      _state.bumpConnectionGeneration();
      final int generation = _state.connectionGeneration;
      final StreamSubscription<String>? subscription = _state
          .detachMessageSubscription();
      await cancelSubscription(subscription);
      if (_state.isAdministrativelyInvalidated ||
          generation != _state.connectionGeneration) {
        return;
      }
      await closeTransport();
      if (_state.isAdministrativelyInvalidated ||
          generation != _state.connectionGeneration) {
        return;
      }
      _state.resetAfterConnectionTeardown(
        preserveReconnecting: orphanRetrySafeOperations,
      );
      _pendingOperationFailureHandler(
        reason,
        orphanRetrySafeOperations: orphanRetrySafeOperations,
      );
    });
  }

  /// Closes resources after an administrative invalidation without changing its typed state.
  /// Stale cleanup from an older generation is ignored, and a newer connection cannot be closed
  /// by a callback belonging to the invalidated one.
  Future<void> closeAfterInvalidation(int generation) =>
      _lifecycleQueue.run(() async {
        if (generation != _state.connectionGeneration) {
          return;
        }
        final StreamSubscription<String>? subscription = _state
            .detachMessageSubscription();
        await cancelSubscription(subscription);
        if (generation != _state.connectionGeneration) {
          return;
        }
        await closeTransport();
        if (generation != _state.connectionGeneration) {
          return;
        }
      });

  /// Best-effort cancels [subscription].
  Future<void> cancelSubscription(
    StreamSubscription<String>? subscription,
  ) async {
    if (subscription != null) {
      try {
        await subscription.cancel();
      } on Object {
        // In-memory state transitions remain authoritative if cancellation fails.
      }
    }
  }

  /// Best-effort closes the transport.
  Future<void> closeTransport() async {
    try {
      await _transport.close();
    } on Object {
      // In-memory state transitions remain authoritative if transport close fails.
    }
  }
}
