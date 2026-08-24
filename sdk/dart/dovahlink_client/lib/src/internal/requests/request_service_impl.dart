import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/reply_validator.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Implements [RequestService], per `ai/context/sdk/architecture.md`'s "Internal composition" and
/// "Request/session boundary". Every collaborator ([PendingOperationBookkeeping],
/// [PendingOperationTransmitter], [MessageRouter]) is supplied by the caller per
/// `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never constructs one of
/// its own dependencies. [PendingOperationTransmitter] and [MessageRouter] depend directly on
/// [SessionService] and [PendingOperationBookkeeping], the same instances this class holds --
/// no adapter stands between them.
class RequestServiceImpl implements RequestService {
  /// The session this service reads identity/trust from.
  final SessionService _sessionService;

  /// Owns every pending and orphaned-for-retry operation this service tracks.
  final PendingOperationBookkeeping _bookkeeping;

  /// Owns one request's wire-attempt mechanics while [_bookkeeping] owns pending-operation state.
  final PendingOperationTransmitter _transmitter;

  /// Owns envelope decoding, correlation, and unsolicited routing.
  final MessageRouter _messageRouter;

  /// Creates a request service over already-constructed [sessionService], [bookkeeping],
  /// [transmitter], and [messageRouter].
  RequestServiceImpl({
    required SessionService sessionService,
    required PendingOperationBookkeeping bookkeeping,
    required PendingOperationTransmitter transmitter,
    required MessageRouter messageRouter,
  }) : _sessionService = sessionService,
       _bookkeeping = bookkeeping,
       _transmitter = transmitter,
       _messageRouter = messageRouter;

  /// Implements [RequestService.sendAndAwait]. Fails immediately, before registering or
  /// transmitting anything, unless [SessionService.connectionState] is currently `connected` --
  /// replacing the eliminated `ensureReceiving` mechanism per
  /// `ai/context/sdk/architecture.md`'s "Request/session boundary". The one narrow exception is
  /// [ProtocolMessageType.hello] itself while `reauthenticating` -- that is precisely the message
  /// a bounded-recovery attempt sends to find out whether this device is trusted, blocked, or
  /// revoked, so it cannot itself wait for trust to already be confirmed. No other message type is
  /// ever exempted: whether this device is blocked or revoked is still unknown while
  /// `reauthenticating`, so an ordinary application request must not be allowed to reach the wire
  /// during that window merely because the transport happens to be up again. `reconnecting` fails
  /// outright (no exception, not even for `hello`): it spans a whole bounded-recovery cycle
  /// (backoff delay, then an in-flight `connect()` attempt) during which no transport is guaranteed
  /// usable at all. An already-pending `retrySafe` operation orphaned by the transport loss that
  /// triggered recovery is unaffected by this guard; see [retryOrphanedOperations].
  @override
  Future<Envelope> sendAndAwait({
    required ProtocolMessageType messageType,
    required JsonMap payload,
    required ProtocolMessageType expectedType,
    required RequestPolicy policy,
  }) async {
    final DovahLinkConnectionState connectionState =
        _sessionService.connectionState;
    final bool canSend =
        connectionState == DovahLinkConnectionState.connected ||
        (connectionState == DovahLinkConnectionState.reauthenticating &&
            messageType == ProtocolMessageType.hello);
    if (!canSend) {
      throw DovahLinkConnectionException(
        'Cannot send $messageType: no active connection.',
      );
    }
    final DovahLinkTrustState? requiredTrustState =
        policy.requiredTrustState;
    if (requiredTrustState != null &&
        requiredTrustState != _sessionService.currentTrustState) {
      throw DovahLinkConnectionException(
        'Cannot send $messageType: the current session does not have the '
        'required trust state.',
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

  /// Implements [RequestService.failAll].
  @override
  void failAll(Exception reason, {required bool orphanRetrySafeOperations}) =>
      _bookkeeping.failAll(
        reason,
        orphanRetrySafeOperations: orphanRetrySafeOperations,
      );

  /// Implements [RequestService.retryOrphanedOperations]. An operation whose
  /// [RequestPolicy.requiredTrustState] the new session no longer satisfies fails without
  /// retransmission instead of being retried into a session it was never classified for.
  @override
  void retryOrphanedOperations() {
    final List<PendingOperation> toRetry = _bookkeeping.takeOrphaned();
    for (final PendingOperation operation in toRetry) {
      final DovahLinkTrustState? required = operation.policy.requiredTrustState;
      if (required != null && required != _sessionService.currentTrustState) {
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
