import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/session_invalidated_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Routes decoded unsolicited messages to their typed SDK lifecycle surface.
abstract interface class IUnsolicitedMessageHandler {
  /// Handles one unsolicited Host message.
  /// @param envelope The decoded, uncorrelated protocol envelope.
  void handle(Envelope envelope);
}

/// Routes decoded unsolicited messages to their typed SDK lifecycle surface.
class UnsolicitedMessageHandler implements IUnsolicitedMessageHandler {
  /// Where lifecycle events and malformed unsolicited messages are reported.
  final ISessionService _sessionService;

  /// Routes state-domain Snapshot and Event messages.
  final IStateMessageHandler _stateMessageHandler;

  /// Creates a handler routing lifecycle and state messages through their owning capabilities.
  /// @param sessionService The connection lifecycle boundary for errors and invalidation.
  /// @param stateMessageHandler The typed state-domain message boundary.
  UnsolicitedMessageHandler({
    required ISessionService sessionService,
    required IStateMessageHandler stateMessageHandler,
  }) : _sessionService = sessionService,
       _stateMessageHandler = stateMessageHandler;

  /// Handles one unsolicited [envelope], ignoring known unsupported message types.
  @override
  void handle(Envelope envelope) {
    switch (envelope.messageType) {
      case ProtocolMessageType.capabilities:
        // Declared once after hello_ack; exposing it is out of this client's current scope.
        break;
      case ProtocolMessageType.sessionInvalidated:
        try {
          _sessionService.onSessionInvalidated(
            ProtocolPayloadDecoder.decode(
              SessionInvalidatedPayload.fromJson,
              envelope.payload,
            ).reason,
          );
        } on DovahLinkProtocolException catch (error) {
          _sessionService.onProtocolViolation(
            error,
            orphanRetrySafeOperations: false,
          );
        }
        break;
      case ProtocolMessageType.error:
        try {
          _sessionService.onUnsolicitedError(
            ProtocolPayloadDecoder.decode(
              ErrorPayload.fromJson,
              envelope.payload,
            ),
          );
        } on DovahLinkProtocolException catch (error) {
          _sessionService.onProtocolViolation(
            error,
            orphanRetrySafeOperations: false,
          );
        }
        break;
      case ProtocolMessageType.stateSnapshot:
      case ProtocolMessageType.stateEvent:
        _stateMessageHandler.handle(envelope);
        break;
      default:
        // A known but currently unsupported unsolicited message is ignored. An unknown wire value
        // was already rejected while decoding [Envelope].
        break;
    }
  }
}
