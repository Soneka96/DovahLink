import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_registry.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/reply_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/reply_validator.dart';
import 'package:dovahlink_client_sdk/src/internal/request_sender.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Owns pending requests, timeouts, and retry behavior, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". The SDK owns exactly one of these
/// per [DovahLinkTransport] connection; see `ai/context/sdk/architecture.md`'s "Inbound message
/// handling" for the correlation model this implements.
class RequestManager
    implements RequestSender, ReplyResolver, PendingOperationRegistry {
  /// The live session identity/trust state each outgoing and retried envelope is stamped with or
  /// revalidated against.
  final SessionContext _sessionContext;

  /// Creates a request manager that owns pending requests and retry behavior while delegating
  /// individual wire attempts to [PendingOperationTransmitter].
  RequestManager({
    required DovahLinkTransport transport,
    required Map<TimeoutClass, Duration> timeoutDurations,
    required SessionContext sessionContext,
    required ConnectionLifecycleReporter reporter,
  }) : _sessionContext = sessionContext {
    _transmitter = PendingOperationTransmitter(
      transport: transport,
      timeoutDurations: timeoutDurations,
      sessionContext: sessionContext,
      reporter: reporter,
      registry: this,
    );
  }

  /// Owns one request's wire-attempt mechanics while this manager owns pending-operation state.
  late final PendingOperationTransmitter _transmitter;

  /// Every operation awaiting a correlated reply on the current connection, keyed by the outgoing
  /// `messageId` it was transmitted under.
  final Map<String, PendingOperation> _pendingOperations =
      <String, PendingOperation>{};

  /// Retry-safe operations an ordinary (non-administrative) transport loss orphaned before they
  /// received a reply, awaiting a chance to be retransmitted once the next `hello` succeeds.
  final List<PendingOperation> _orphanedRetryableOperations =
      <PendingOperation>[];

  /// Implements [PendingOperationRegistry.register].
  @override
  void register(String messageId, PendingOperation operation) {
    _pendingOperations[messageId] = operation;
  }

  /// Implements [PendingOperationRegistry.fail].
  @override
  void fail(String messageId, PendingOperation operation, Exception reason) {
    final PendingOperation? registered = _pendingOperations[messageId];
    if (!identical(registered, operation)) {
      return;
    }
    _pendingOperations.remove(messageId);
    if (!operation.completer.isCompleted) {
      operation.completer.completeError(reason);
    }
  }

  /// Sends one envelope carrying [messageType]/[payload], classified against [policy], and
  /// returns its correlated reply. The reply may arrive on a later wire attempt than the one this
  /// call makes: if the connection is lost before a reply arrives and [policy] is `retrySafe`,
  /// this same call keeps waiting while the operation is retransmitted once, automatically, after
  /// the next successful `hello` -- see [retryOrphanedOperations]. A [DovahLinkConnectionException]
  /// means the connection itself is unhealthy (transport failure, timeout, or a
  /// non-retriable/failed retry); a [DovahLinkProtocolException] means the bridge answered on an
  /// otherwise-live connection with a wire-level rejection or an unexpected reply.
  @override
  Future<Envelope> sendAndAwait({
    required ProtocolMessageType messageType,
    required JsonMap payload,
    required ProtocolMessageType expectedType,
    required RequestPolicy policy,
  }) async {
    final PendingOperation operation = PendingOperation(
      messageType: messageType,
      payload: payload,
      policy: policy,
    );
    _transmitter.transmit(operation);
    final Envelope envelope = await operation.completer.future;
    return ReplyValidator.validate(
      expectedType: expectedType,
      envelope: envelope,
    );
  }

  /// Resolves the pending operation matching [correlationId] with [envelope], completing its
  /// [sendAndAwait] future. Returns `false` if no pending operation matches, allowing the routing
  /// layer to fail closed on an unmatched correlation.
  @override
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
  /// retried once is parked in [_orphanedRetryableOperations] instead of being failed immediately,
  /// awaiting [retryOrphanedOperations]; when `false`, every pending operation is failed
  /// immediately and any already-orphaned operation is failed too.
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

  /// Retransmits, at most once each, every operation an earlier ordinary transport loss orphaned,
  /// now that a new session has been admitted. An operation whose
  /// [RequestPolicy.requiredTrustState] the new session no longer satisfies fails without
  /// retransmission instead of being retried into a session it was never classified for.
  void retryOrphanedOperations() {
    final List<PendingOperation> toRetry = _orphanedRetryableOperations
        .toList();
    _orphanedRetryableOperations.clear();
    for (final PendingOperation operation in toRetry) {
      final DovahLinkTrustState? required = operation.policy.requiredTrustState;
      if (required != null && required != _sessionContext.currentTrustState) {
        if (!operation.completer.isCompleted) {
          operation.completer.completeError(
            DovahLinkConnectionException(
              'Cannot retry ${operation.messageType} after reconnect: the new session no '
              'longer satisfies its required trust state.',
            ),
          );
        }
        continue;
      }
      operation.hasRetried = true;
      _transmitter.transmit(operation);
    }
  }
}
