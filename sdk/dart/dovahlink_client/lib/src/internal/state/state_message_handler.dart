import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_revision_tracker.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_event_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/state_snapshot_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/character_health_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_level_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_magicka_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_stamina_state.dart';
import 'package:dovahlink_client_sdk/src/state/character_xp_state.dart';

/// Decodes and routes one state envelope to its typed domain revision tracker.
abstract interface class IStateMessageHandler {
  /// Handles one canonical state Snapshot or Event envelope.
  /// @param envelope The validated state envelope from the single inbound reader.
  void handle(Envelope envelope);
}

/// Routes registered state areas to their independent synchronization trackers.
class StateMessageHandler implements IStateMessageHandler {
  /// Reports malformed state messages through the current session lifecycle.
  final ISessionService _sessionService;

  /// Owns the independently versioned experience state.
  final IStateRevisionTracker<CharacterXpState> _characterXp;

  /// Owns the independently versioned health state.
  final IStateRevisionTracker<CharacterHealthState> _characterHealth;

  /// Owns the independently versioned magicka state.
  final IStateRevisionTracker<CharacterMagickaState> _characterMagicka;

  /// Owns the independently versioned stamina state.
  final IStateRevisionTracker<CharacterStaminaState> _characterStamina;

  /// Owns the Event-mode character level state.
  final IStateRevisionTracker<CharacterLevelState> _characterLevel;

  /// Creates a state-message handler over all currently registered state domains.
  /// @param sessionService Reports malformed messages to the connection lifecycle.
  /// @param characterXp Owns character experience revisions.
  /// @param characterHealth Owns character health revisions.
  /// @param characterMagicka Owns character magicka revisions.
  /// @param characterStamina Owns character stamina revisions.
  /// @param characterLevel Owns character level revisions and recovery buffering.
  StateMessageHandler({
    required ISessionService sessionService,
    required IStateRevisionTracker<CharacterXpState> characterXp,
    required IStateRevisionTracker<CharacterHealthState> characterHealth,
    required IStateRevisionTracker<CharacterMagickaState> characterMagicka,
    required IStateRevisionTracker<CharacterStaminaState> characterStamina,
    required IStateRevisionTracker<CharacterLevelState> characterLevel,
  }) : _sessionService = sessionService,
       _characterXp = characterXp,
       _characterHealth = characterHealth,
       _characterMagicka = characterMagicka,
       _characterStamina = characterStamina,
       _characterLevel = characterLevel;

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
          final String stateAuthorityId = envelope.stateAuthorityId!;
          switch (payload.stateArea) {
            case 'character_xp':
              final CharacterXpState state = ProtocolPayloadDecoder.decode(
                CharacterXpState.fromJson,
                payload.data,
              );
              _characterXp.applySnapshot(
                stateAuthorityId: stateAuthorityId,
                playContextId: envelope.playContextId,
                revision: payload.revision,
                value: state,
                isUnavailable: state.value == null,
              );
              break;
            case 'character_health':
              final CharacterHealthState state = ProtocolPayloadDecoder.decode(
                CharacterHealthState.fromJson,
                payload.data,
              );
              _characterHealth.applySnapshot(
                stateAuthorityId: stateAuthorityId,
                playContextId: envelope.playContextId,
                revision: payload.revision,
                value: state,
                isUnavailable: state.value == null,
              );
              break;
            case 'character_magicka':
              final CharacterMagickaState state = ProtocolPayloadDecoder.decode(
                CharacterMagickaState.fromJson,
                payload.data,
              );
              _characterMagicka.applySnapshot(
                stateAuthorityId: stateAuthorityId,
                playContextId: envelope.playContextId,
                revision: payload.revision,
                value: state,
                isUnavailable: state.value == null,
              );
              break;
            case 'character_stamina':
              final CharacterStaminaState state = ProtocolPayloadDecoder.decode(
                CharacterStaminaState.fromJson,
                payload.data,
              );
              _characterStamina.applySnapshot(
                stateAuthorityId: stateAuthorityId,
                playContextId: envelope.playContextId,
                revision: payload.revision,
                value: state,
                isUnavailable: state.value == null,
              );
              break;
            case 'character_level':
              final CharacterLevelState state = ProtocolPayloadDecoder.decode(
                CharacterLevelState.fromJson,
                payload.data,
              );
              _characterLevel.applySnapshot(
                stateAuthorityId: stateAuthorityId,
                playContextId: envelope.playContextId,
                revision: payload.revision,
                value: state,
                isUnavailable: state.value == null,
              );
              break;
            default:
              throw const DovahLinkProtocolException(
                code: ProtocolErrorCode.malformedMessage,
                message: 'Received an unregistered state area.',
                retryable: false,
              );
          }
          break;
        case ProtocolMessageType.stateEvent:
          final StateEventPayload payload = ProtocolPayloadDecoder.decode(
            StateEventPayload.fromJson,
            envelope.payload,
          );
          if (payload.stateArea != 'character_level') {
            throw const DovahLinkProtocolException(
              code: ProtocolErrorCode.malformedMessage,
              message: 'Received an Event for a non-Event state area.',
              retryable: false,
            );
          }
          final CharacterLevelState state = ProtocolPayloadDecoder.decode(
            CharacterLevelState.fromJson,
            payload.data,
          );
          _characterLevel.applyEvent(
            stateAuthorityId: envelope.stateAuthorityId!,
            playContextId: envelope.playContextId,
            baseRevision: payload.baseRevision,
            revision: payload.revision,
            value: state,
            isUnavailable: state.value == null,
          );
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
