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
  /// Called with the complete desired set when a `subscribe` is sent, so a baseline that follows its
  /// acknowledgement on the wire is never discarded, and again with the Host's accepted set once the
  /// acknowledgement is processed.
  /// @param stateAreas The complete set whose state messages may be applied.
  /// @param baselineCorrelationId The `subscribe` request ID authorizing delayed initial baselines;
  ///   omitted for the provisional call made when the request is sent.
  void setSubscribedStateAreas(
    Set<String> stateAreas, {
    String? baselineCorrelationId,
  });

  /// Closes the accepted-area gate for an ended session that automatic recovery may replace, keeping
  /// each admitted area's last accepted value, identity, and revision under the non-current
  /// `recovering` status. Retention never authorizes a state message: nothing is applied until the
  /// next [setSubscribedStateAreas] admits the area again. A later call reconciles every retained
  /// area against its set: areas absent from it are reset, and a changed identity clears the rest.
  void suspendStateAreas();

  /// Whether [correlationId] still identifies an accepted area's initial baseline.
  /// @param correlationId The Host response correlation to check.
  /// @return Whether the correlation belongs to a baseline still being established.
  bool isPendingBaselineCorrelation(String correlationId);

  /// Decides whether a correlated recovery Snapshot may be applied, observing its identity first.
  /// A recovery request can be carried over from an ended session, so its reply may arrive before
  /// the replacement session's acknowledgement. Only an area the Host has acknowledged in the
  /// current session is admitted; a provisionally admitted, retained, or absent area is not, and the
  /// restored subscription's own baseline establishes it instead. An admitted Snapshot's identity
  /// goes through the same cross-domain reset as any other state message.
  /// @param stateArea The area the recovery Snapshot belongs to.
  /// @param envelope The correlated Snapshot envelope.
  /// @return Whether the caller may apply the Snapshot to the area's tracker.
  /// @throws [DovahLinkProtocolException] when an admitted Snapshot has no authority identity.
  bool admitRecoverySnapshot(String stateArea, Envelope envelope);

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

  /// State areas whose incoming state messages may currently be applied.
  ///
  /// During a pending `subscribe`, this provisionally contains the complete desired set so an
  /// ACK-adjacent baseline cannot race ahead of the gate. After the acknowledgement, it is
  /// reconciled to the Host-accepted set.
  final Set<String> _subscribedStateAreas = <String>{};

  /// Areas whose gate closed with [suspendStateAreas] and whose trackers still hold the last accepted
  /// observation. They are outside [_subscribedStateAreas], so they can never accept a message.
  final Set<String> _retainedStateAreas = <String>{};

  /// Marks an area admitted when its `subscribe` was sent, whose request ID is not yet known to be
  /// authoritative. The acknowledgement replaces it with the real request ID; it never matches one.
  static const String _provisionalBaselineCorrelation = '';

  /// Current subscribe correlation for accepted areas still waiting for their first Snapshot.
  final Map<String, String> _pendingBaselineCorrelations = <String, String>{};

  /// The authority and play context currently installed across subscribed trackers.
  ({String stateAuthorityId, String? playContextId})? _currentIdentity;

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
  void setSubscribedStateAreas(
    Set<String> stateAreas, {
    String? baselineCorrelationId,
  }) {
    for (final String area in _retainedStateAreas.difference(stateAreas)) {
      _domains[area]?.tracker.resetToNotSubscribed();
    }
    _retainedStateAreas.clear();
    final Set<String> addedAreas = stateAreas.difference(_subscribedStateAreas);
    for (final String area in addedAreas) {
      _domains[area]?.tracker.beginRecovery();
    }
    final Set<String> removedAreas = _subscribedStateAreas.difference(
      stateAreas,
    );
    for (final String area in removedAreas) {
      _domains[area]?.tracker.resetToNotSubscribed();
      _pendingBaselineCorrelations.remove(area);
    }
    if (baselineCorrelationId != null) {
      for (final String area in stateAreas) {
        if (addedAreas.contains(area) ||
            _pendingBaselineCorrelations.containsKey(area)) {
          _pendingBaselineCorrelations[area] = baselineCorrelationId;
        }
      }
    } else {
      for (final String area in addedAreas) {
        _pendingBaselineCorrelations[area] = _provisionalBaselineCorrelation;
      }
    }
    _subscribedStateAreas
      ..clear()
      ..addAll(stateAreas);
  }

  /// Implements [IStateMessageHandler.suspendStateAreas].
  @override
  void suspendStateAreas() {
    for (final String area in _subscribedStateAreas) {
      _domains[area]?.tracker.beginRecovery();
      _retainedStateAreas.add(area);
    }
    _subscribedStateAreas.clear();
    _pendingBaselineCorrelations.clear();
  }

  /// Implements [IStateMessageHandler.isPendingBaselineCorrelation].
  @override
  bool isPendingBaselineCorrelation(String correlationId) =>
      correlationId != _provisionalBaselineCorrelation &&
      _pendingBaselineCorrelations.values.contains(correlationId);

  /// Implements [IStateMessageHandler.admitRecoverySnapshot].
  @override
  bool admitRecoverySnapshot(String stateArea, Envelope envelope) {
    final String? baselineCorrelation = _pendingBaselineCorrelations[stateArea];
    if (!_subscribedStateAreas.contains(stateArea) ||
        baselineCorrelation == _provisionalBaselineCorrelation) {
      return false;
    }
    _observeIdentity(envelope);
    return true;
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
          _observeIdentity(envelope);
          final bool snapshotAccepted = domain.applySnapshot(
            envelope: envelope,
            payload: payload,
          );
          if (snapshotAccepted) {
            _pendingBaselineCorrelations.remove(payload.stateArea);
          }
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
          _observeIdentity(envelope);
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

  /// Clears every accepted tracker before the first message for a new identity is routed.
  /// @param envelope The state envelope carrying the newly observed identity pair.
  /// @throws [DovahLinkProtocolException] when a state message has no authority identity.
  void _observeIdentity(Envelope envelope) {
    final String? stateAuthorityId = envelope.stateAuthorityId;
    if (stateAuthorityId == null) {
      throw const DovahLinkProtocolException(
        code: ProtocolErrorCode.malformedMessage,
        message: 'A state message has no authority identity.',
        retryable: false,
      );
    }

    final ({String stateAuthorityId, String? playContextId}) incomingIdentity =
        (
          stateAuthorityId: stateAuthorityId,
          playContextId: envelope.playContextId,
        );
    if (_currentIdentity == incomingIdentity) {
      return;
    }

    for (final String area in _subscribedStateAreas) {
      _domains[area]?.tracker.resetForIdentity(
        stateAuthorityId: incomingIdentity.stateAuthorityId,
        playContextId: incomingIdentity.playContextId,
      );
    }
    _currentIdentity = incomingIdentity;
  }
}
