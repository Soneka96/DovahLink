import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/readiness/gameplay_readiness_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/gameplay_readiness_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/session_invalidated_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Routes decoded unsolicited messages to their typed SDK lifecycle surface.
abstract interface class IUnsolicitedMessageHandler {
  /// Handles one unsolicited Host message.
  /// @param envelope The decoded, uncorrelated protocol envelope.
  void handle(Envelope envelope);

  /// Handles a correlated baseline error when it belongs to a pending accepted subscription.
  /// @param envelope The Host error envelope whose request already received its acknowledgement.
  /// @return Whether this handler consumed the response; unknown correlations return `false`.
  bool handleCorrelatedBaselineError(Envelope envelope);
}

/// Routes decoded unsolicited messages to their typed SDK lifecycle surface.
class UnsolicitedMessageHandler implements IUnsolicitedMessageHandler {
  /// Where lifecycle events and malformed unsolicited messages are reported.
  final ISessionService _sessionService;

  /// Routes state-domain Snapshot and Event messages.
  final IStateMessageHandler _stateMessageHandler;

  /// Owns the accepted gameplay-readiness fact. Readiness is not a state area, so it never goes
  /// through [_stateMessageHandler].
  final IGameplayReadinessService _gameplayReadinessService;

  /// Creates a handler routing lifecycle, state, and readiness messages through their owners.
  /// @param sessionService The connection lifecycle boundary for errors and invalidation.
  /// @param stateMessageHandler The typed state-domain message boundary.
  /// @param gameplayReadinessService The owner of the accepted readiness fact.
  UnsolicitedMessageHandler({
    required ISessionService sessionService,
    required IStateMessageHandler stateMessageHandler,
    required IGameplayReadinessService gameplayReadinessService,
  }) : _sessionService = sessionService,
       _stateMessageHandler = stateMessageHandler,
       _gameplayReadinessService = gameplayReadinessService;

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
      case ProtocolMessageType.gameplayReadiness:
        try {
          final String? sessionId = envelope.sessionId;
          final String? stateAuthorityId = envelope.stateAuthorityId;
          if (sessionId == null || stateAuthorityId == null) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'A gameplay_readiness message has no session identity.',
              retryable: false,
            );
          }
          _gameplayReadinessService.observe(
            sessionId: sessionId,
            stateAuthorityId: stateAuthorityId,
            payload: ProtocolPayloadDecoder.decode(
              GameplayReadinessPayload.fromJson,
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
      default:
        // A known but currently unsupported unsolicited message is ignored. An unknown wire value
        // was already rejected while decoding [Envelope].
        break;
    }
  }

  /// Implements [IUnsolicitedMessageHandler.handleCorrelatedBaselineError].
  @override
  bool handleCorrelatedBaselineError(Envelope envelope) {
    final String? correlationId = envelope.correlationId;
    if (envelope.messageType != ProtocolMessageType.error ||
        correlationId == null) {
      return false;
    }

    final ErrorPayload error;
    try {
      error = ProtocolPayloadDecoder.decode(
        ErrorPayload.fromJson,
        envelope.payload,
      );
    } on DovahLinkProtocolException catch (protocolError) {
      _sessionService.onProtocolViolation(
        protocolError,
        orphanRetrySafeOperations: false,
      );
      return true;
    }

    return error.code == ProtocolErrorCode.temporarilyUnavailable &&
        error.retryable &&
        _stateMessageHandler.isPendingBaselineCorrelation(correlationId);
  }
}
