import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_invalidation.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_not_found_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_pairing_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_storage_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/authentication_service.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/rename_outcome_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/rename_request_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Exposes operations and lifecycle state for the client's single active session.
abstract interface class IDovahLinkConnections {
  /// The current connection lifecycle phase.
  DovahLinkConnectionState get state;

  /// Emits the current lifecycle phase on listen and every subsequent transition.
  Stream<DovahLinkConnectionState> get stateChanges;

  /// Replays `inactive` or `retrying` immediately, then emits initial-retry transitions.
  Stream<DovahLinkInitialConnectionRetryStatus>
  get initialConnectionRetryChanges;

  /// The administrative reason for invalidation, or `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason;

  /// Emits terminal invalidations for admitted Known Host sessions with their reason attached.
  Stream<DovahLinkKnownHostInvalidation> get knownHostInvalidations;

  /// Renames this client in the active Host's trust record.
  /// @param displayName The new display name, or an empty string to clear it.
  /// @return The Host's typed rename outcome.
  /// @throws [DovahLinkConnectionException] if no trusted session is active or transport fails.
  /// @throws [DovahLinkProtocolException] if the Host reports a protocol failure or malformed reply.
  Future<RenameOutcome> renameDevice(String displayName);

  /// Connects and authenticates a discovered candidate without using Known Host credentials.
  /// An initial connection or retryable protocol failure is retried by the SDK every three seconds
  /// until success, cancellation, or administrative invalidation.
  /// @param uri The candidate endpoint.
  /// @return The admitted Host handshake and trust result.
  /// @throws [DovahLinkConnectionException] if connection or authentication fails.
  /// @throws [DovahLinkProtocolException] if the Host rejects or malforms the handshake.
  /// @throws [DovahLinkHostIdentityMismatchException] if pending recovery names another Host.
  /// @throws [DovahLinkCompatibilityException] if the Host version is unsupported.
  /// @throws [DovahLinkStorageException] if client identity persistence fails.
  Future<HelloResult> connectCandidate(Uri uri);

  /// Connects and authenticates the Known Host selected by stable identity.
  /// An initial connection or retryable protocol failure is retried by the SDK every three seconds
  /// until success, cancellation, or administrative invalidation.
  /// @param hostId The Known Host relationship whose endpoint and credential are used.
  /// @return The admitted Host handshake and trust result.
  /// @throws [DovahLinkKnownHostNotFoundException] if [hostId] is not known.
  /// @throws [DovahLinkConnectionException] if connection or authentication fails.
  /// @throws [DovahLinkProtocolException] if the Host rejects or malforms the handshake.
  /// @throws [DovahLinkHostIdentityMismatchException] if the peer or pending recovery names another Host.
  /// @throws [DovahLinkCompatibilityException] if the Host version is unsupported.
  /// @throws [DovahLinkPairingException] if pending pairing recovery is rejected.
  /// @throws [DovahLinkStorageException] if identity or credential persistence fails.
  Future<HelloResult> connectKnownHost(DovahLinkHostId hostId);

  /// Deliberately disconnects and cancels pending authentication and recovery.
  Future<void> disconnect();
}

/// Implements [IDovahLinkConnections] over the existing session and authentication owners.
class DovahLinkConnections implements IDovahLinkConnections {
  /// Owns connection and authentication state.
  final ISessionService _sessionService;

  /// Owns candidate and Known Host authentication.
  final IAuthenticationService _authenticationService;

  /// Owns bounded established-session recovery.
  final IReconnectService _reconnectService;

  /// Owns desired game-state subscription intent.
  final ISubscriptionService _subscriptionService;

  /// Sends correlated requests over the active session.
  final IRequestService _requestService;

  /// Creates the connection view over the client's existing lifecycle owners.
  /// @param sessionService Owns transport and admitted session state.
  /// @param authenticationService Authenticates candidate and Known Host sessions.
  /// @param reconnectService Cancels established-session recovery on deliberate disconnect.
  /// @param subscriptionService Clears desired state subscriptions on deliberate disconnect.
  /// @param requestService Sends trusted-session protocol requests.
  DovahLinkConnections({
    required ISessionService sessionService,
    required IAuthenticationService authenticationService,
    required IReconnectService reconnectService,
    required ISubscriptionService subscriptionService,
    required IRequestService requestService,
  }) : _sessionService = sessionService,
       _authenticationService = authenticationService,
       _reconnectService = reconnectService,
       _subscriptionService = subscriptionService,
       _requestService = requestService;

  /// Implements [IDovahLinkConnections.state].
  @override
  DovahLinkConnectionState get state => _sessionService.connectionState;

  /// Implements [IDovahLinkConnections.stateChanges].
  @override
  Stream<DovahLinkConnectionState> get stateChanges =>
      _sessionService.connectionStateChanges;

  /// Implements [IDovahLinkConnections.initialConnectionRetryChanges].
  @override
  Stream<DovahLinkInitialConnectionRetryStatus>
  get initialConnectionRetryChanges =>
      _reconnectService.initialConnectionRetryChanges;

  /// Implements [IDovahLinkConnections.invalidationReason].
  @override
  AdministrativeInvalidationReason? get invalidationReason =>
      _sessionService.invalidationReason;

  /// Implements [IDovahLinkConnections.knownHostInvalidations].
  @override
  Stream<DovahLinkKnownHostInvalidation> get knownHostInvalidations =>
      _sessionService.knownHostInvalidations;

  /// Implements [IDovahLinkConnections.connectCandidate].
  @override
  Future<HelloResult> connectCandidate(Uri uri) =>
      _reconnectService.connectWithInitialRetry(
        () => _authenticationService.authenticateCandidate(uri),
      );

  /// Implements [IDovahLinkConnections.connectKnownHost].
  @override
  Future<HelloResult> connectKnownHost(DovahLinkHostId hostId) =>
      _reconnectService.connectWithInitialRetry(
        () => _authenticationService.authenticateKnownHost(hostId),
      );

  /// Implements [IDovahLinkConnections.renameDevice].
  @override
  Future<RenameOutcome> renameDevice(String displayName) async {
    final Envelope response = await _requestService.sendAndAwait(
      messageType: ProtocolMessageType.renameRequest,
      payload: RenameRequestPayload(displayName: displayName).toJson(),
      expectedType: ProtocolMessageType.renameOutcome,
      policy: const RequestPolicy(
        retrySafe: false,
        requiredTrustState: DovahLinkTrustState.trusted,
        timeoutClass: TimeoutClass.normal,
      ),
    );
    final RenameOutcomePayload outcome;
    try {
      outcome = ProtocolPayloadDecoder.decode(
        RenameOutcomePayload.fromJson,
        response.payload,
      );
    } on DovahLinkProtocolException catch (error) {
      _sessionService.onProtocolViolation(
        error,
        orphanRetrySafeOperations: false,
      );
      rethrow;
    }
    return outcome.outcome;
  }

  /// Implements [IDovahLinkConnections.disconnect].
  @override
  Future<void> disconnect() async {
    _authenticationService.cancelPendingAuthentication();
    _reconnectService.stopInitialConnectionRetry();
    _reconnectService.stopRecovery();
    _subscriptionService.clearDesiredStateAreas();
    await _sessionService.disconnect();
  }
}
