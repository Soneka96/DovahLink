import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_failure_handler.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_registry.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/reply_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/reply_validator.dart';
import 'package:dovahlink_client_sdk/src/internal/request_sender.dart';
import 'package:dovahlink_client_sdk/src/internal/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Implements [RequestService], per `ai/context/sdk/architecture.md`'s "Internal composition" and
/// "Request/session boundary". Also implements [SessionContext], [ConnectionLifecycleReporter],
/// [ReplyResolver], and [PendingOperationRegistry] -- the pre-decomposition ports
/// [MessageRouter] and [PendingOperationTransmitter] are themselves unchanged internally and still
/// depend on -- purely to hand itself to those collaborators the same way `RequestManager` does
/// today, forwarding each reactive report directly to [SessionService]; every one of these ports
/// is eliminated when `RequestManager` is deleted at the composition-root cutover.
class RequestServiceImpl
    implements
        RequestService,
        RequestSender,
        ReplyResolver,
        PendingOperationRegistry,
        PendingOperationFailureHandler,
        SessionContext,
        ConnectionLifecycleReporter {
  /// The session this service reads identity/trust from and reports connection events to.
  final SessionService _sessionService;

  /// Creates a request service that owns pending requests, retry behavior, and inbound message
  /// routing, over [transport] and [sessionService].
  RequestServiceImpl({
    required DovahLinkTransport transport,
    required Map<TimeoutClass, Duration> timeoutDurations,
    required SessionService sessionService,
  }) : _sessionService = sessionService {
    _transmitter = PendingOperationTransmitter(
      transport: transport,
      timeoutDurations: timeoutDurations,
      sessionContext: this,
      reporter: this,
      registry: this,
    );
    _messageRouter = MessageRouter(replyResolver: this, reporter: this);
  }

  /// Owns one request's wire-attempt mechanics while this service owns pending-operation state.
  late final PendingOperationTransmitter _transmitter;

  /// Owns envelope decoding, correlation, and unsolicited routing.
  late final MessageRouter _messageRouter;

  /// Every operation awaiting a correlated reply on the current connection, keyed by the outgoing
  /// `messageId` it was transmitted under.
  final Map<String, PendingOperation> _pendingOperations =
      <String, PendingOperation>{};

  /// Retry-safe operations an ordinary (non-administrative) transport loss orphaned before they
  /// received a reply, awaiting a chance to be retransmitted once the next `hello` succeeds.
  final List<PendingOperation> _orphanedRetryableOperations =
      <PendingOperation>[];

  /// Implements [SessionContext.currentSessionId].
  @override
  String? get currentSessionId => _sessionService.currentSessionId;

  /// Implements [SessionContext.currentTrustState].
  @override
  DovahLinkTrustState? get currentTrustState =>
      _sessionService.currentTrustState;

  /// Implements [PendingOperationRegistry.register].
  @override
  void register(String messageId, PendingOperation operation) {
    _pendingOperations[messageId] = operation;
  }

  /// Implements [RequestService.sendAndAwait]/[RequestSender.sendAndAwait]. Fails immediately,
  /// before registering or transmitting anything, unless [SessionService.connectionState] is
  /// currently `connected` or `reconnecting` -- replacing the eliminated `ensureReceiving`
  /// mechanism per `ai/context/sdk/architecture.md`'s "Request/session boundary".
  @override
  Future<Envelope> sendAndAwait({
    required ProtocolMessageType messageType,
    required JsonMap payload,
    required ProtocolMessageType expectedType,
    required RequestPolicy policy,
  }) async {
    final DovahLinkConnectionState connectionState =
        _sessionService.connectionState;
    if (connectionState != DovahLinkConnectionState.connected &&
        connectionState != DovahLinkConnectionState.reconnecting) {
      throw DovahLinkConnectionException(
        'Cannot send $messageType: no active connection.',
      );
    }
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

  /// Implements [RequestService.handleIncoming].
  @override
  void handleIncoming(String raw) => _messageRouter.handleIncoming(raw);

  /// Resolves the pending operation matching [correlationId] with [envelope], completing its
  /// [sendAndAwait] future. Returns `false` if no pending operation matches, allowing
  /// [MessageRouter] to fail closed on an unmatched correlation.
  /// Implements [ReplyResolver.resolveReply].
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

  /// Implements [RequestService.failAll]/[PendingOperationFailureHandler.failAll].
  @override
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

  /// Implements [RequestService.retryOrphanedOperations].
  @override
  void retryOrphanedOperations() {
    final List<PendingOperation> toRetry = _orphanedRetryableOperations
        .toList();
    _orphanedRetryableOperations.clear();
    for (final PendingOperation operation in toRetry) {
      final DovahLinkTrustState? required = operation.policy.requiredTrustState;
      if (required != null && required != currentTrustState) {
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

  /// Implements [ConnectionLifecycleReporter.onUnhealthy].
  @override
  void onUnhealthy(Exception reason) => _sessionService.onUnhealthy(reason);

  /// Implements [ConnectionLifecycleReporter.onProtocolViolation].
  @override
  void onProtocolViolation(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  }) => _sessionService.onProtocolViolation(
    reason,
    orphanRetrySafeOperations: orphanRetrySafeOperations,
  );

  /// Implements [ConnectionLifecycleReporter.onSessionInvalidated].
  @override
  void onSessionInvalidated(AdministrativeInvalidationReason reason) =>
      _sessionService.onSessionInvalidated(reason);

  /// Implements [ConnectionLifecycleReporter.onUnsolicitedError].
  @override
  void onUnsolicitedError(ErrorPayload error) =>
      _sessionService.onUnsolicitedError(error);
}
