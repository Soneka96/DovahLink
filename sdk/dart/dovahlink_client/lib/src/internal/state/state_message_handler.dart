import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Decodes and routes one state envelope to its typed domain revision tracker.
abstract interface class IStateMessageHandler {
  /// Handles one canonical state Snapshot or Event envelope.
  /// @param envelope The state envelope from the single inbound reader.
  void handle(Envelope envelope);
}

/// Routes state messages to explicitly registered typed domain definitions.
class StateMessageHandler implements IStateMessageHandler {
  /// Reports malformed state messages through the current session lifecycle.
  final ISessionService _sessionService;

  /// Looks up each supported state area by its canonical name.
  final Map<String, IStateDomainDefinition> _domains;

  /// Creates a handler over the supplied state-area registrations.
  /// @param sessionService Reports malformed messages to the lifecycle.
  /// @param domains The typed definitions for supported state areas.
  StateMessageHandler({
    required ISessionService sessionService,
    required List<IStateDomainDefinition> domains,
  }) : _sessionService = sessionService,
       _domains = <String, IStateDomainDefinition>{
         for (final IStateDomainDefinition domain in domains)
           domain.stateArea: domain,
       };

  /// See [IStateMessageHandler.handle].
  @override
  void handle(Envelope envelope) {
    try {
      switch (envelope.messageType) {
        case ProtocolMessageType.stateSnapshot:
          final StateSnapshotPayload payload = ProtocolPayloadDecoder.decode(
            StateSnapshotPayload.fromJson,
            envelope.payload,
          );
          final IStateDomainDefinition? domain = _domains[payload.stateArea];
          if (domain == null) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'Received an unregistered state area.',
              retryable: false,
            );
          }
          domain.applySnapshot(envelope: envelope, payload: payload);
          break;
        case ProtocolMessageType.stateEvent:
          final StateEventPayload payload = ProtocolPayloadDecoder.decode(
            StateEventPayload.fromJson,
            envelope.payload,
          );
          final IStateDomainDefinition? domain = _domains[payload.stateArea];
          if (domain == null) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'Received an unregistered state area.',
              retryable: false,
            );
          }
          domain.applyEvent(envelope: envelope, payload: payload);
          break;
        default:
          throw const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'StateMessageHandler received a non-state message.',
            retryable: false,
          );
      }
    } on DovahLinkProtocolException catch (error) {
      _sessionService.onProtocolViolation(
        error,
        orphanRetrySafeOperations: false,
      );
    }
  }
}
