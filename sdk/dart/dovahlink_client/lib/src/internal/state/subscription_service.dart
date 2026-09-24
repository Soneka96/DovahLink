import 'dart:async';

import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_message_handler.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/subscribe_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/subscription_ack_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns per-domain subscription intent and updates the Host's complete active set.
abstract interface class ISubscriptionService {
  /// The state domains this client currently wants, retained across transport loss.
  Set<DovahLinkStateArea> get desiredStateAreas;

  /// Adds [area] to the desired set and sends that complete set to the Host.
  /// @param area The state domain to request.
  /// @return The domains the Host rejected from the complete desired set.
  /// @throws [DovahLinkConnectionException] if no trusted session is active.
  /// @throws [DovahLinkProtocolException] if the Host returns a malformed acknowledgement.
  Future<Set<DovahLinkStateArea>> subscribeStateArea(DovahLinkStateArea area);

  /// Removes [area] from the desired set and sends that complete set to the Host.
  /// @param area The state domain to remove.
  /// @return The domains the Host rejected from the complete desired set.
  /// @throws [DovahLinkConnectionException] if no trusted session is active.
  /// @throws [DovahLinkProtocolException] if the Host returns a malformed acknowledgement.
  Future<Set<DovahLinkStateArea>> unsubscribeStateArea(DovahLinkStateArea area);

  /// Resends the current desired set to the Host on an authenticated session.
  /// @return The domains the Host rejected from the complete desired set.
  /// @throws [DovahLinkConnectionException] if no trusted session is active.
  /// @throws [DovahLinkProtocolException] if the Host returns a malformed acknowledgement.
  Future<Set<DovahLinkStateArea>> synchronizeDesiredStateAreas();

  /// Starts best-effort restoration after an authenticated session becomes trusted.
  void restoreDesiredStateAreas();

  /// Clears the accepted state gate when the current socket session ends, preserving intent.
  void onSessionEnded();

  /// Clears both desired intent and the current accepted state gate.
  void clearDesiredStateAreas();
}

/// Implements per-domain subscription intent over the shared request and state-message paths.
class SubscriptionService implements ISubscriptionService {
  /// Sends correlated protocol requests.
  final IRequestService _requestService;

  /// Reports malformed subscription acknowledgements as protocol violations.
  final ISessionService _sessionService;

  /// Applies only the state domains the Host accepted for this session.
  final IStateMessageHandler _stateMessageHandler;

  /// The state domains this client wants independent of the current socket session.
  final Set<DovahLinkStateArea> _desiredStateAreas = <DovahLinkStateArea>{};

  /// Changes whenever the desired set changes, preventing an older acknowledgement from
  /// replacing the state-message gate for a newer request.
  int _intentGeneration = 0;

  /// Changes whenever a session ends, preventing its late acknowledgement from reopening the gate.
  int _sessionGeneration = 0;

  /// Creates a subscription service over the shared request, session, and state-message owners.
  /// @param requestService Sends correlated subscription updates.
  /// @param sessionService Receives malformed-acknowledgement violations.
  /// @param stateMessageHandler gates state messages to areas accepted by the Host.
  SubscriptionService({
    required IRequestService requestService,
    required ISessionService sessionService,
    required IStateMessageHandler stateMessageHandler,
  }) : _requestService = requestService,
       _sessionService = sessionService,
       _stateMessageHandler = stateMessageHandler;

  /// See [ISubscriptionService.desiredStateAreas].
  @override
  Set<DovahLinkStateArea> get desiredStateAreas =>
      Set<DovahLinkStateArea>.unmodifiable(_desiredStateAreas);

  /// See [ISubscriptionService.subscribeStateArea].
  @override
  Future<Set<DovahLinkStateArea>> subscribeStateArea(DovahLinkStateArea area) {
    if (_desiredStateAreas.add(area)) {
      _intentGeneration++;
    }
    return synchronizeDesiredStateAreas();
  }

  /// See [ISubscriptionService.unsubscribeStateArea].
  @override
  Future<Set<DovahLinkStateArea>> unsubscribeStateArea(
    DovahLinkStateArea area,
  ) {
    if (_desiredStateAreas.remove(area)) {
      _intentGeneration++;
    }
    return synchronizeDesiredStateAreas();
  }

  /// See [ISubscriptionService.synchronizeDesiredStateAreas].
  @override
  Future<Set<DovahLinkStateArea>> synchronizeDesiredStateAreas() async {
    final int requestGeneration = _intentGeneration;
    final int requestSessionGeneration = _sessionGeneration;
    final Set<DovahLinkStateArea> requestedAreas = Set<DovahLinkStateArea>.of(
      _desiredStateAreas,
    );
    final List<String> protocolAreas = <String>[
      for (final DovahLinkStateArea area in DovahLinkStateArea.values)
        if (requestedAreas.contains(area)) area.protocolValue,
    ];
    final Envelope response = await _requestService.sendAndAwait(
      messageType: ProtocolMessageType.subscribe,
      payload: SubscribePayload(stateAreas: protocolAreas).toJson(),
      expectedType: ProtocolMessageType.subscriptionAck,
      policy: const RequestPolicy(
        retrySafe: true,
        requiredTrustState: DovahLinkTrustState.trusted,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    final SubscriptionAckPayload acknowledgement;
    try {
      acknowledgement = ProtocolPayloadDecoder.decode(
        SubscriptionAckPayload.fromJson,
        response.payload,
      );
    } on DovahLinkProtocolException catch (error) {
      _sessionService.onProtocolViolation(
        error,
        orphanRetrySafeOperations: false,
      );
      rethrow;
    }
    final Set<DovahLinkStateArea> acceptedAreas = _decodeAreas(
      acknowledgement.acceptedStateAreas,
    );
    final Set<DovahLinkStateArea> rejectedAreas = _decodeAreas(
      acknowledgement.rejectedStateAreas,
    );
    final Set<DovahLinkStateArea> acknowledgedAreas = <DovahLinkStateArea>{
      ...acceptedAreas,
      ...rejectedAreas,
    };
    if (acknowledgedAreas.length !=
            acknowledgement.acceptedStateAreas.length +
                acknowledgement.rejectedStateAreas.length ||
        acknowledgedAreas.length != requestedAreas.length ||
        !acknowledgedAreas.containsAll(requestedAreas)) {
      _reportMalformedAcknowledgement();
    }

    if (_intentGeneration == requestGeneration &&
        _sessionGeneration == requestSessionGeneration) {
      _stateMessageHandler.setSubscribedStateAreas(<String>{
        for (final DovahLinkStateArea area in acceptedAreas) area.protocolValue,
      });
    }
    return Set<DovahLinkStateArea>.unmodifiable(rejectedAreas);
  }

  /// See [ISubscriptionService.restoreDesiredStateAreas].
  @override
  void restoreDesiredStateAreas() {
    if (_desiredStateAreas.isEmpty) {
      return;
    }
    unawaited(_restoreDesiredStateAreasInBackground());
  }

  /// Sends the desired set without letting a recovery error escape an authentication operation.
  Future<void> _restoreDesiredStateAreasInBackground() async {
    try {
      await synchronizeDesiredStateAreas();
    } on Object {
      // RequestService routes transport failures and malformed acknowledgements through
      // SessionService before this best-effort lifecycle restoration completes.
    }
  }

  /// See [ISubscriptionService.onSessionEnded].
  @override
  void onSessionEnded() {
    _sessionGeneration++;
    _stateMessageHandler.setSubscribedStateAreas(<String>{});
  }

  /// See [ISubscriptionService.clearDesiredStateAreas].
  @override
  void clearDesiredStateAreas() {
    if (_desiredStateAreas.isNotEmpty) {
      _desiredStateAreas.clear();
      _intentGeneration++;
    }
    onSessionEnded();
  }

  /// Decodes protocol state-area identifiers into the SDK's typed domains.
  /// @param protocolAreas The accepted or rejected identifiers in the acknowledgement.
  /// @return The decoded state domains.
  Set<DovahLinkStateArea> _decodeAreas(List<String> protocolAreas) {
    final Set<DovahLinkStateArea> areas = <DovahLinkStateArea>{};
    for (final String protocolArea in protocolAreas) {
      final DovahLinkStateArea? area = DovahLinkStateArea.fromProtocolValue(
        protocolArea,
      );
      if (area == null) {
        _reportMalformedAcknowledgement();
      }
      areas.add(area);
    }
    return areas;
  }

  /// Reports an acknowledgement that does not partition the requested set, then throws.
  Never _reportMalformedAcknowledgement() {
    const DovahLinkProtocolException error = DovahLinkProtocolException(
      code: ProtocolErrorCode.malformedMessage,
      message: 'The Host returned an invalid subscription acknowledgement.',
      retryable: false,
    );
    _sessionService.onProtocolViolation(
      error,
      orphanRetrySafeOperations: false,
    );
    throw error;
  }
}
