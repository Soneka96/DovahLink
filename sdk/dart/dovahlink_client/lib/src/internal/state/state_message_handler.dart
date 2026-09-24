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
  /// Replaces the areas whose state messages this client may apply, marking new areas as recovering.
  /// @param stateAreas The complete accepted set for the current Host session.
  void setSubscribedStateAreas(Set<String> stateAreas);

  /// Handles one canonical state Snapshot or Event envelope.
  /// @param envelope The state envelope from the single inbound reader.
  void handle(Envelope envelope);
}

/// Routes state messages to explicitly registered typed domain definitions.
class StateMessageHandler implements IStateMessageHandler {
  /// Reports malformed state messages through the current session lifecycle.
  final ISessionService _sessionService;

  /// Looks up each supported state area by its canonical name.
  final Map<String, IStateDomainDefinition<Object?>> _domains;

  /// State areas the current Host session accepted for this client.
  final Set<String> _subscribedStateAreas = <String>{};

  /// Creates a handler over the supplied state-area registrations.
  /// @param sessionService Reports malformed messages to the lifecycle.
  /// @param domains The typed definitions for supported state areas.
  StateMessageHandler({
    required ISessionService sessionService,
    required List<IStateDomainDefinition<Object?>> domains,
  }) : _sessionService = sessionService,
       _domains = <String, IStateDomainDefinition<Object?>>{
         for (final IStateDomainDefinition<Object?> domain in domains)
           domain.stateArea: domain,
       };

  /// Implements [IStateMessageHandler.setSubscribedStateAreas].
  @override
  void setSubscribedStateAreas(Set<String> stateAreas) {
    final Set<String> addedAreas = stateAreas.difference(_subscribedStateAreas);
    for (final String area in addedAreas) {
      _domains[area]?.tracker.beginRecovery();
    }
    final Set<String> removedAreas = _subscribedStateAreas.difference(
      stateAreas,
    );
    for (final String area in removedAreas) {
      _domains[area]?.tracker.resetToNotSubscribed();
    }
    _subscribedStateAreas
      ..clear()
      ..addAll(stateAreas);
  }

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
          final IStateDomainDefinition<Object?>? domain =
              _domains[payload.stateArea];
          if (domain == null) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'Received an unregistered state area.',
              retryable: false,
            );
          }
          if (!_subscribedStateAreas.contains(payload.stateArea)) {
            break;
          }
          domain.applySnapshot(envelope: envelope, payload: payload);
          break;
        case ProtocolMessageType.stateEvent:
          final StateEventPayload payload = ProtocolPayloadDecoder.decode(
            StateEventPayload.fromJson,
            envelope.payload,
          );
          final IStateDomainDefinition<Object?>? domain =
              _domains[payload.stateArea];
          if (domain == null) {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'Received an unregistered state area.',
              retryable: false,
            );
          }
          if (!_subscribedStateAreas.contains(payload.stateArea)) {
            break;
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
