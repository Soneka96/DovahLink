import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/message_router.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_transmitter.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/reply_validator.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// Defines request/reply operations for one [IDovahLinkTransport] connection, including correlated
/// replies, unsolicited messages, and protocol violations.
abstract interface class IRequestService {
  /// Sends [messageType] with [payload] under [policy] and awaits [expectedType]. The connection
  /// state guard accepts requests while [DovahLinkConnectionState.connected], or only
  /// [ProtocolMessageType.hello] while [DovahLinkConnectionState.reauthenticating]. All other
  /// states fail before registering or transmitting. The request's
  /// [RequestPolicy.requiredTrustState] is checked separately.
  Future<Envelope> sendAndAwait({
    required ProtocolMessageType messageType,
    required JsonMap payload,
    required ProtocolMessageType expectedType,
    required RequestPolicy policy,
  });

  /// Decodes and routes one inbound message: correlated replies, unsolicited pushes, and protocol
  /// violations.
  void handleIncoming(String raw);

  /// Resolves every pending operation. An operation allowed by [RequestPolicy.retrySafe] that has
  /// not already been retried is parked for [IRequestService.retryOrphanedOperations] instead of
  /// being failed immediately when [orphanRetrySafeOperations] is `true`.
  void failAll(Exception reason, {required bool orphanRetrySafeOperations});

  /// Retransmits, at most once each, every operation an earlier ordinary transport loss orphaned.
  void retryOrphanedOperations();
}

/// Routes requests and inbound messages while coordinating pending operations, transmission, and
/// reply validation through constructor-supplied collaborators.
class RequestService implements IRequestService {
  /// The session this service reads identity/trust from.
  final ISessionService _sessionService;

  /// Owns every pending and orphaned-for-retry operation this service tracks.
  final PendingOperationBookkeeping _bookkeeping;

  /// Owns one request's wire-attempt mechanics while [RequestService._bookkeeping] owns
  /// pending-operation state.
  final PendingOperationTransmitter _transmitter;

  /// Owns envelope decoding, correlation, and unsolicited routing.
  final IMessageRouter _messageRouter;

  /// Creates a request service over already-constructed [sessionService], [bookkeeping],
  /// [transmitter], and [messageRouter].
  RequestService({
    required ISessionService sessionService,
    required PendingOperationBookkeeping bookkeeping,
    required PendingOperationTransmitter transmitter,
    required IMessageRouter messageRouter,
  }) : _sessionService = sessionService,
       _bookkeeping = bookkeeping,
       _transmitter = transmitter,
       _messageRouter = messageRouter;

  /// Implements [IRequestService.sendAndAwait]. Requires a
  /// [DovahLinkConnectionState.connected] session before registering or transmitting a request.
  /// During [DovahLinkConnectionState.reauthenticating], only
  /// [ProtocolMessageType.hello] may be sent because that request determines whether the current
  /// device is trusted. During [DovahLinkConnectionState.reconnecting], all requests fail because
  /// the transport may be in backoff or still connecting. Existing pending operations allowed by
  /// [RequestPolicy.retrySafe] are unaffected by this guard; see
  /// [IRequestService.retryOrphanedOperations].
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
    final DovahLinkTrustState? requiredTrustState = policy.requiredTrustState;
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

  /// Implements [IRequestService.handleIncoming].
  @override
  void handleIncoming(String raw) => _messageRouter.handleIncoming(raw);

  /// Implements [IRequestService.failAll].
  @override
  void failAll(Exception reason, {required bool orphanRetrySafeOperations}) =>
      _bookkeeping.failAll(
        reason,
        orphanRetrySafeOperations: orphanRetrySafeOperations,
      );

  /// Implements [IRequestService.retryOrphanedOperations]. An operation whose
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
