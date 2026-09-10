import 'dart:async';
import 'dart:convert';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_cache.dart';
import 'package:dovahlink_client_sdk/src/internal/random_id_generator.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope_validator.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/transport/websocket_transport.dart';

/// Owns one pending operation's wire attempt: message-ID generation, registration, timeout
/// arming, envelope construction, and fire-and-forget transport error reporting.
class PendingOperationTransmitter {
  /// The transport used for the wire attempt.
  final IDovahLinkTransport _transport;

  /// The bounded timeout policy applied to the operation's timeout class.
  final Map<TimeoutClass, Duration> _timeoutDurations;

  /// Supplies the session ID stamped onto the outgoing envelope, and receives timeout and
  /// transport-failure notifications.
  final ISessionService _sessionService;

  /// Owns registration and terminal failure of the pending operation.
  final PendingOperationBookkeeping _bookkeeping;

  /// Shares this installation's resolved `clientId`, stamped onto every outgoing envelope whose
  /// message type requires one, per `client_id_cache.dart`'s documented reason a direct dependency
  /// on `AuthenticationService` is impossible.
  final ClientIdCache _clientIdCache;

  /// Creates a transmitter for one request service's transport, pending-operation bookkeeping, and
  /// resolved-clientId cache.
  PendingOperationTransmitter({
    required IDovahLinkTransport transport,
    required Map<TimeoutClass, Duration> timeoutDurations,
    required ISessionService sessionService,
    required PendingOperationBookkeeping bookkeeping,
    required ClientIdCache clientIdCache,
  }) : _transport = transport,
       _timeoutDurations = timeoutDurations,
       _sessionService = sessionService,
       _bookkeeping = bookkeeping,
       _clientIdCache = clientIdCache;

  /// Generates message IDs for wire attempts.
  final RandomIdGenerator _randomIdGenerator = RandomIdGenerator();

  /// Generates a message ID, registers [operation], arms its timeout, and sends its envelope.
  /// Send and timeout failures are reported through [ISessionService] rather than failed directly
  /// here, so a timed-out operation is failed or orphaned for retry through the same
  /// connection-teardown path as every other pending operation on the connection, per
  /// [RequestPolicy.retrySafe] -- not force-failed ahead of its siblings merely because its own
  /// timer happened to be the one that fired.
  ///
  /// Throws [StateError], without registering [operation] or arming its timeout, if
  /// [EnvelopeValidator.isClientIdRequired] requires a `clientId` for [operation]'s message type
  /// but [ClientIdCache.clientId] has not been resolved yet -- a wire-invariant guard against ever
  /// constructing an envelope [EnvelopeValidator.validate] itself would reject. Not a path any
  /// current caller can reach: every implemented client-ID-required request already requires a
  /// trust state only a prior successful `hello` (which always resolves the cache first) can
  /// produce.
  void transmit(PendingOperation operation) {
    final bool requiresClientId = EnvelopeValidator.isClientIdRequired(
      operation.messageType,
    );
    final String? clientId = requiresClientId ? _clientIdCache.clientId : null;
    if (requiresClientId && clientId == null) {
      throw StateError(
        'Cannot transmit ${operation.messageType}: clientId has not been resolved yet.',
      );
    }

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
      clientId: clientId,
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
