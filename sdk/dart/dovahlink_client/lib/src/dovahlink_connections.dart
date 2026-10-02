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
import 'package:dovahlink_client_sdk/src/internal/reconnect/reconnect_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
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

  /// Creates the connection view over the client's existing lifecycle owners.
  /// @param sessionService Owns transport and admitted session state.
  /// @param authenticationService Authenticates candidate and Known Host sessions.
  /// @param reconnectService Cancels established-session recovery on deliberate disconnect.
  /// @param subscriptionService Clears desired state subscriptions on deliberate disconnect.
  DovahLinkConnections({
    required ISessionService sessionService,
    required IAuthenticationService authenticationService,
    required IReconnectService reconnectService,
    required ISubscriptionService subscriptionService,
  }) : _sessionService = sessionService,
       _authenticationService = authenticationService,
       _reconnectService = reconnectService,
       _subscriptionService = subscriptionService;

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
