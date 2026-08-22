import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/protocol_format_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/session_invalidated_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Routes decoded unsolicited messages to their typed SDK lifecycle surface.
class UnsolicitedMessageHandler {
  /// Where lifecycle events and malformed unsolicited messages are reported.
  final ConnectionLifecycleReporter _reporter;

  /// Creates a handler reporting lifecycle events and protocol violations through [reporter].
  UnsolicitedMessageHandler({required ConnectionLifecycleReporter reporter})
    : _reporter = reporter;

  /// Handles one unsolicited [envelope], ignoring known unsupported message types.
  void handle(Envelope envelope) {
    switch (envelope.messageType) {
      case ProtocolMessageType.capabilities:
        // Declared once after hello_ack; exposing it is out of this client's current scope.
        break;
      case ProtocolMessageType.sessionInvalidated:
        try {
          final SessionInvalidatedPayload payload =
              SessionInvalidatedPayload.fromJson(envelope.payload);
          _reporter.onSessionInvalidated(payload.reason);
        } on ProtocolFormatException catch (error) {
          _reporter.onProtocolViolation(
            DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: error.message,
              retryable: false,
            ),
            orphanRetrySafeOperations: false,
          );
        }
        break;
      default:
        // A known but currently unsupported unsolicited message is ignored. An unknown wire value
        // was already rejected while decoding [Envelope].
        break;
    }
  }
}
