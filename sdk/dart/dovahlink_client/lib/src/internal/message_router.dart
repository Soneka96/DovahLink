import 'dart:convert';

import '../dovahlink_client_exception.dart';
import '../protocol/envelope.dart';
import '../protocol/json_map.dart';
import '../protocol/protocol_format_exception.dart';
import '../protocol/session_invalidated_payload.dart';
import 'connection_lifecycle_reporter.dart';
import 'request_manager.dart';

/// Owns envelope decoding, correlation, and unsolicited routing, per
/// `ai/context/sdk/architecture.md`'s "Internal composition" and "Inbound message handling". Does
/// not itself own connection state or transport lifecycle -- a decoded event it cannot resolve
/// itself (a correlated reply, an authoritative `session_invalidated` push, a protocol violation)
/// is reported to [ConnectionLifecycleReporter] or resolved through [RequestManager] instead.
class MessageRouter {
  /// Creates a message router resolving correlated replies through [requestManager] and
  /// reporting everything else to [reporter].
  MessageRouter({
    required RequestManager requestManager,
    required ConnectionLifecycleReporter reporter,
  }) : _requestManager = requestManager,
       _reporter = reporter;

  /// Where a correlated reply is resolved.
  final RequestManager _requestManager;

  /// Where an unsolicited `session_invalidated` push and every protocol violation this router
  /// detects are reported.
  final ConnectionLifecycleReporter _reporter;

  /// Decodes and routes one inbound message. Matches a correlated reply to its pending operation
  /// strictly by `correlationId`/`messageId` through [RequestManager.resolveReply]; routes an
  /// unsolicited (`correlationId: null`) message by type; reports a protocol violation for a
  /// non-null `correlationId` matching no pending operation, and for malformed JSON, rather than
  /// letting either escape as an uncaught error.
  void handleIncoming(String raw) {
    final Envelope envelope;
    try {
      envelope = Envelope.fromJson(jsonDecode(raw) as JsonMap);
    } on Object catch (error) {
      // A protocol-level anomaly on an otherwise-live connection, not ordinary connectivity
      // loss -- never assumed safe to retry, unlike a send failure/timeout/socket drop.
      _reporter.onProtocolViolation(
        DovahLinkConnectionException(
          'Received malformed JSON from the bridge: $error',
        ),
        orphanRetrySafeOperations: false,
      );
      return;
    }

    final String? correlationId = envelope.correlationId;
    if (correlationId == null) {
      _handleUnsolicited(envelope);
      return;
    }

    final bool resolved = _requestManager.resolveReply(correlationId, envelope);
    if (!resolved) {
      // Protocol violation, not ordinary connectivity loss -- see the malformed-JSON branch
      // above for why this never orphans a retry-safe operation either.
      _reporter.onProtocolViolation(
        DovahLinkProtocolException(
          code: 'unexpected_correlation_id',
          message:
              'Received a reply correlated to $correlationId with no matching pending operation.',
          retryable: false,
        ),
        orphanRetrySafeOperations: false,
      );
    }
  }

  /// Routes one unsolicited (`correlationId: null`) inbound message by its type.
  void _handleUnsolicited(Envelope envelope) {
    switch (envelope.messageType) {
      case 'capabilities':
        // Declared once after hello_ack; exposing it is out of this client's current scope.
        break;
      case 'session_invalidated':
        _handleSessionInvalidated(envelope);
        break;
      default:
        // Forward-compatible: an unsolicited message type this client does not yet model is
        // ignored rather than treated as a protocol violation -- only a non-null correlationId
        // matching no pending operation fails closed.
        break;
    }
  }

  /// Decodes an authoritative `session_invalidated` push and reports it through
  /// [ConnectionLifecycleReporter.onSessionInvalidated]. Whether a session is even currently
  /// authenticated to receive one is the reporter's own state to check, not this router's --
  /// decoding is all this method owns.
  void _handleSessionInvalidated(Envelope envelope) {
    try {
      final SessionInvalidatedPayload payload =
          SessionInvalidatedPayload.fromJson(envelope.payload);
      _reporter.onSessionInvalidated(payload.reason);
    } on ProtocolFormatException catch (error) {
      _reporter.onProtocolViolation(
        _malformedMessageException(error),
        orphanRetrySafeOperations: true,
      );
    }
  }

  /// Translates a DTO decode boundary failure into the typed exception SDK consumers already
  /// handle, per `ai/context/sdk/api-design.md`'s "Protocol DTO decoding" boundary translation.
  DovahLinkProtocolException _malformedMessageException(
    ProtocolFormatException error,
  ) => DovahLinkProtocolException(
    code: 'malformed_message',
    message: error.message,
    retryable: false,
  );
}
