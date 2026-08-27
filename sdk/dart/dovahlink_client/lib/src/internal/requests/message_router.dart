import 'dart:convert';

import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation_bookkeeping.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/unsolicited_message_handler.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns envelope decoding, correlation, and unsolicited routing, per
/// `ai/context/sdk/architecture.md`'s "Internal composition" and "Inbound message handling". Does
/// not itself own connection state or transport lifecycle -- a decoded event it cannot resolve
/// itself (a correlated reply, an authoritative `session_invalidated` push, a protocol violation)
/// is reported to [ISessionService] or resolved through [PendingOperationBookkeeping] instead.
class MessageRouter {
  /// Where a correlated reply is resolved by its correlation ID.
  final PendingOperationBookkeeping _bookkeeping;

  /// Where an unsolicited `session_invalidated` push and every protocol violation this router
  /// detects are reported.
  final ISessionService _sessionService;

  /// Creates a message router resolving correlated replies through [bookkeeping] and
  /// reporting everything else to [sessionService].
  MessageRouter({
    required PendingOperationBookkeeping bookkeeping,
    required ISessionService sessionService,
  }) : _bookkeeping = bookkeeping,
       _sessionService = sessionService {
    _unsolicitedMessageHandler = UnsolicitedMessageHandler(
      sessionService: sessionService,
    );
  }

  /// Routes decoded unsolicited messages.
  late final UnsolicitedMessageHandler _unsolicitedMessageHandler;

  /// Decodes and routes one inbound message. Matches a correlated reply to its pending operation
  /// strictly by `correlationId`/`messageId` through [PendingOperationBookkeeping.resolveReply];
  /// routes an unsolicited (`correlationId: null`) message by type; reports a protocol violation
  /// for a non-null `correlationId` matching no pending operation, and for malformed JSON, rather
  /// than letting either escape as an uncaught error.
  void handleIncoming(String raw) {
    final Envelope envelope;
    try {
      envelope = Envelope.fromJson(jsonDecode(raw) as JsonMap);
    } on Object catch (error) {
      // A protocol-level anomaly on an otherwise-live connection, not ordinary connectivity
      // loss -- never assumed safe to retry, unlike a send failure/timeout/socket drop.
      _sessionService.onProtocolViolation(
        DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'Invalid protocol envelope: $error',
          retryable: false,
        ),
        orphanRetrySafeOperations: false,
      );
      return;
    }

    final String? correlationId = envelope.correlationId;
    if (correlationId == null) {
      _unsolicitedMessageHandler.handle(envelope);
      return;
    }

    final bool resolved = _bookkeeping.resolveReply(correlationId, envelope);
    if (!resolved) {
      // Protocol violation, not ordinary connectivity loss -- see the malformed-JSON branch
      // above for why this never orphans a retry-safe operation either.
      _sessionService.onProtocolViolation(
        DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message:
              'Received a reply correlated to $correlationId with no matching pending operation.',
          retryable: false,
        ),
        orphanRetrySafeOperations: false,
      );
    }
  }
}
