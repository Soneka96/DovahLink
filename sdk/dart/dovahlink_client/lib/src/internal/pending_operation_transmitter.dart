import 'dart:async';
import 'dart:convert';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/message_id_generator.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/pending_operation_registry.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
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

  /// Supplies the session ID stamped onto the outgoing envelope.
  final SessionContext _sessionContext;

  /// Receives timeout and transport-failure notifications.
  final ConnectionLifecycleReporter _reporter;

  /// Owns registration and terminal failure of the pending operation.
  final PendingOperationRegistry _registry;

  /// Creates a transmitter for one request manager's transport and pending-operation registry.
  PendingOperationTransmitter({
    required DovahLinkTransport transport,
    required Map<TimeoutClass, Duration> timeoutDurations,
    required SessionContext sessionContext,
    required ConnectionLifecycleReporter reporter,
    required PendingOperationRegistry registry,
  }) : _transport = transport,
       _timeoutDurations = timeoutDurations,
       _sessionContext = sessionContext,
       _reporter = reporter,
       _registry = registry;

  /// Generates message IDs for wire attempts.
  final MessageIdGenerator _messageIdGenerator = MessageIdGenerator();

  /// Generates a message ID, registers [operation], arms its timeout, and sends its envelope.
  /// Send and timeout failures are reported through [ConnectionLifecycleReporter] rather than
  /// failed directly here, so a timed-out operation is failed or orphaned for retry through the
  /// same connection-teardown path as every other pending operation on the connection, per
  /// [RequestPolicy.retrySafe] -- not force-failed ahead of its siblings merely because its own
  /// timer happened to be the one that fired.
  void transmit(PendingOperation operation) {
    final String messageId = _messageIdGenerator.generate();
    _registry.register(messageId, operation);
    operation.timer = Timer(
      _timeoutDurations[operation.policy.timeoutClass]!,
      () => _reporter.onUnhealthy(
        DovahLinkConnectionException(
          'Timed out awaiting a reply to ${operation.messageType}.',
        ),
      ),
    );

    final Envelope outgoing = Envelope(
      messageType: operation.messageType,
      messageId: messageId,
      sessionId: _sessionContext.currentSessionId,
      correlationId: null,
      payload: operation.payload,
      bridgeInstanceId: null,
      playContextId: null,
      clientId: null,
    );
    unawaited(
      _transport.send(jsonEncode(outgoing.toJson())).catchError((Object error) {
        _reporter.onUnhealthy(
          DovahLinkConnectionException(
            'Failed to send ${operation.messageType}: $error',
          ),
        );
      }),
    );
  }
}
