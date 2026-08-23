import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';

/// Owns every pending and orphaned-for-retry operation on the current connection, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". A plain, no-interface supporting
/// object per "Not everything is a Service" -- it holds state and the small set of transitions
/// that state can undergo, and performs no wire I/O or session/trust reasoning of its own, so
/// `RequestServiceImpl` can hand [ReplyResolverImpl] and [PendingOperationRegistryImpl] a real,
/// distinct object over this bookkeeping instead of implementing those pre-decomposition ports
/// itself.
class PendingOperationBookkeeping {
  /// Every operation awaiting a correlated reply on the current connection, keyed by the outgoing
  /// `messageId` it was transmitted under.
  final Map<String, PendingOperation> _pendingOperations =
      <String, PendingOperation>{};

  /// Retry-safe operations an ordinary (non-administrative) transport loss orphaned before they
  /// received a reply, awaiting a chance to be retransmitted once the next `hello` succeeds.
  final List<PendingOperation> _orphanedRetryableOperations =
      <PendingOperation>[];

  /// Associates [messageId] with [operation] before its wire attempt is sent.
  void register(String messageId, PendingOperation operation) {
    _pendingOperations[messageId] = operation;
  }

  /// Resolves the pending operation matching [correlationId] with [envelope], completing its
  /// awaited future. Returns `false` if no pending operation matches, allowing the caller to fail
  /// closed on an unmatched correlation.
  bool resolveReply(String correlationId, Envelope envelope) {
    final PendingOperation? operation = _pendingOperations.remove(
      correlationId,
    );
    if (operation == null) {
      return false;
    }
    operation.timer?.cancel();
    if (!operation.completer.isCompleted) {
      operation.completer.complete(envelope);
    }
    return true;
  }

  /// Resolves every pending operation, used for both an ordinary transport-level teardown and an
  /// unconditional failure (a protocol violation or a deliberate disconnect). When
  /// [orphanRetrySafeOperations] is `true`, a `retrySafe` operation that has not already been
  /// retried once is parked for [takeOrphaned] instead of being failed immediately; when `false`,
  /// every pending operation is failed immediately and any already-orphaned operation is failed
  /// too.
  void failAll(Exception reason, {required bool orphanRetrySafeOperations}) {
    final List<PendingOperation> pending = _pendingOperations.values.toList();
    _pendingOperations.clear();
    for (final PendingOperation operation in pending) {
      operation.timer?.cancel();
      if (orphanRetrySafeOperations &&
          operation.policy.retrySafe &&
          !operation.hasRetried) {
        _orphanedRetryableOperations.add(operation);
      } else if (!operation.completer.isCompleted) {
        operation.completer.completeError(reason);
      }
    }

    if (!orphanRetrySafeOperations) {
      final List<PendingOperation> orphaned = _orphanedRetryableOperations
          .toList();
      _orphanedRetryableOperations.clear();
      for (final PendingOperation operation in orphaned) {
        if (!operation.completer.isCompleted) {
          operation.completer.completeError(reason);
        }
      }
    }
  }

  /// Returns and clears every currently orphaned operation, for the caller to revalidate against
  /// the new session and retransmit.
  List<PendingOperation> takeOrphaned() {
    final List<PendingOperation> orphaned = _orphanedRetryableOperations
        .toList();
    _orphanedRetryableOperations.clear();
    return orphaned;
  }
}
