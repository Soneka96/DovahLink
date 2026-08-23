import 'dart:async';
import 'dart:convert';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/random_id_generator.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/dovahlink_transport.dart';

/// Owns one pending operation's wire attempt: message-ID generation, registration, timeout
/// arming, envelope construction, and fire-and-forget transport error reporting.
class PendingOperationTransmitter {
  /// The transport used for the wire attempt.
  final DovahLinkTransport _transport;

  /// The bounded timeout policy applied to the operation's timeout class.
  final Map<TimeoutClass, Duration> _timeoutDurations;

  /// Supplies the session ID stamped onto the outgoing envelope, and receives timeout and
  /// transport-failure notifications.
  final SessionService _sessionService;

  /// Owns registration and terminal failure of the pending operation.
  final PendingOperationBookkeeping _bookkeeping;

  /// Creates a transmitter for one request service's transport and pending-operation bookkeeping.
  PendingOperationTransmitter({
    required DovahLinkTransport transport,
    required Map<TimeoutClass, Duration> timeoutDurations,
    required SessionService sessionService,
    required PendingOperationBookkeeping bookkeeping,
  }) : _transport = transport,
       _timeoutDurations = timeoutDurations,
       _sessionService = sessionService,
       _bookkeeping = bookkeeping;

  /// Generates message IDs for wire attempts.
  final RandomIdGenerator _randomIdGenerator = RandomIdGenerator();

  /// Generates a message ID, registers [operation], arms its timeout, and sends its envelope.
  /// Send and timeout failures are reported through [SessionService] rather than failed directly
  /// here, so a timed-out operation is failed or orphaned for retry through the same
  /// connection-teardown path as every other pending operation on the connection, per
  /// [RequestPolicy.retrySafe] -- not force-failed ahead of its siblings merely because its own
  /// timer happened to be the one that fired.
  void transmit(PendingOperation operation) {
    final String messageId = _randomIdGenerator.generateMessageId();
    _bookkeeping.register(messageId, operation);
    operation.timer = Timer(
      _timeoutDurations[operation.policy.timeoutClass]!,
      () => _sessionService.onUnhealthy(
        DovahLinkConnectionException(
          'Timed out awaiting a reply to ${operation.messageType}.',
        ),
      ),
    );

    final Envelope outgoing = Envelope(
      messageType: operation.messageType,
      messageId: messageId,
      sessionId: _sessionService.currentSessionId,
      correlationId: null,
      payload: operation.payload,
      bridgeInstanceId: null,
      playContextId: null,
      clientId: null,
    );
    unawaited(
      _transport.send(jsonEncode(outgoing.toJson())).catchError((Object error) {
        _sessionService.onUnhealthy(
          DovahLinkConnectionException(
            'Failed to send ${operation.messageType}: $error',
          ),
        );
      }),
    );
  }
}
