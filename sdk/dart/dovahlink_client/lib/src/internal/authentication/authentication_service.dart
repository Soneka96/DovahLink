import 'package:dovahlink_client_sdk/src/dovahlink_compatibility_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_id.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_host_identity_mismatch_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_known_host_not_found_exception.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_cache.dart';
import 'package:dovahlink_client_sdk/src/internal/authentication/client_id_resolver.dart';
import 'package:dovahlink_client_sdk/src/internal/availability/host_availability_service.dart';
import 'package:dovahlink_client_sdk/src/internal/compatibility/host_version_compatibility.dart';
import 'package:dovahlink_client_sdk/src/internal/persistence/client_state_service.dart';
import 'package:dovahlink_client_sdk/src/internal/protocol_payload_decoder.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/hello_ack_payload.dart';
import 'package:dovahlink_client_sdk/src/protocol/hello_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Defines authentication operations for resolving this installation's
/// [IAuthenticationService.clientId], negotiating Host trust, and recovering from a rejected saved
/// credential.
abstract interface class IAuthenticationService {
  /// This installation's stable client ID, or `null` before [IAuthenticationService.hello] resolves
  /// it.
  String? get clientId;

  /// Sends `hello` and admits the returned session. Resolves and persists this installation's
  /// [IAuthenticationService.clientId] on first use. Does not select a Known Host credential.
  /// After admission, retries any orphaned operation whose trust requirement the new session meets.
  /// @return The current Host handshake and trust result.
  /// @throws [DovahLinkProtocolException] if the Host rejects authentication.
  /// @throws [DovahLinkHostIdentityMismatchException] if a trusted session or an outstanding
  ///     pairing recovery reports a different Host ID from the stored Known Host.
  /// @throws [DovahLinkKnownHostNotFoundException] if [hostId] is not in the persisted collection.
  /// @throws [DovahLinkCompatibilityException] if the Host version is outside the SDK's supported
  ///     range.
  /// @throws [DovahLinkConnectionException] if disconnect cancels an in-flight authentication.
  Future<HelloResult> hello();

  /// Authenticates a reconnect using the relationship most recently established by this client.
  /// @return The result of the admitted reconnect session.
  Future<HelloResult> helloLastKnownHost();

  /// Invalidates any authentication continuation waiting on storage or Host I/O.
  void cancelPendingAuthentication();

  /// Connects to an untrusted candidate at [uri] without using Known Host credentials.
  /// @param uri The candidate endpoint to probe or pair with.
  /// @return The Host identity claim and trust outcome reported by the candidate.
  /// @throws [DovahLinkConnectionException] if the candidate endpoint cannot connect.
  /// @throws [DovahLinkProtocolException] if the candidate returns a malformed or rejected hello.
  /// @throws [DovahLinkCompatibilityException] if the Host version is unsupported.
  Future<HelloResult> authenticateCandidate(Uri uri);

  /// Connects to the persisted endpoint and uses only the credential owned by [hostId]. If the
  /// Host rejects it as
  /// [CredentialRejectionReason.revoked] or [CredentialRejectionReason.unrecognized], discards it
  /// and retries once with [AuthMethod.unpaired].
  /// [HelloResult.recoveredFromRejectedCredential] reports that recovery. Transport failures,
  /// incompatible Host versions, and non-recoverable protocol errors still throw.
  ///
  /// Returns the cached result when [DovahLinkConnectionState.connected] and
  /// [DovahLinkTrustState.trusted]. Otherwise closes any existing connection before opening
  /// another, since each socket can admit only one session.
  /// @throws [DovahLinkConnectionException] if the socket cannot be established (initial or retry).
  /// @throws [DovahLinkProtocolException] if hello is rejected for a non-recoverable reason, or the
  ///     retry attempt is itself rejected.
  /// @throws [DovahLinkCompatibilityException] if the Host version is outside the SDK's supported
  ///     range.
  /// @throws [DovahLinkHostIdentityMismatchException] if a trusted session or an outstanding
  ///     pairing recovery reports a different Host ID from the stored Known Host.
  /// @param hostId The stable identifier of the Known Host to authenticate.
  /// @return The connected Host's handshake and trust result.
  Future<HelloResult> authenticateKnownHost(DovahLinkHostId hostId);

  /// Discards the named Host's credential and its owned recovery state, preserving Host metadata
  /// and [IAuthenticationService.clientId].
  /// @param hostId The stable identifier of the Known Host to update.
  Future<void> forgetCredential(DovahLinkHostId hostId);

  /// Clears the credential for the Host most recently authenticated by this client.
  Future<void> forgetLastKnownCredential();
}

/// Implements [IAuthenticationService] by coordinating credential loading, Host hello requests,
/// trust recovery, and session admission.
class AuthenticationService implements IAuthenticationService {
  /// Connects, disconnects, and reads live connection/trust state.
  final ISessionService _sessionService;

  /// Admits a newly authenticated session -- the only class permitted to.
  final ISessionAdmissionService _sessionAdmissionService;

  /// Sends `hello` and awaits its correlated reply.
  final IRequestService _requestService;

  /// The owner of persisted identity, credentials, recovery state, and Known Host metadata.
  final IClientStateService _clientStateService;

  /// The single owner of runtime Known Host reachability values.
  final IHostAvailabilityService _hostAvailabilityService;

  /// Resolves this installation's persisted client ID on first use.
  final ClientIdResolver _clientIdResolver;

  /// Shares this installation's resolved [IAuthenticationService.clientId] with
  /// [PendingOperationTransmitter], so
  /// authentication and pending retries use the same client identity.
  final ClientIdCache _clientIdCache;

  /// Creates an authentication service over [sessionService], [sessionAdmissionService],
  /// [requestService], [clientStateService], [hostAvailabilityService], [clientIdResolver], and
  /// [clientIdCache].
  /// @param sessionService Connects to the Host and reads live session state.
  /// @param sessionAdmissionService Admits a validated Host session.
  /// @param requestService Sends authentication requests.
  /// @param clientStateService Owns persisted client state and its semantic streams.
  /// @param hostAvailabilityService Owns runtime reachability state for Known Hosts.
  /// @param clientIdResolver Resolves the stable local client ID.
  /// @param clientIdCache Shares the resolved client ID with request transmission.
  AuthenticationService({
    required ISessionService sessionService,
    required ISessionAdmissionService sessionAdmissionService,
    required IRequestService requestService,
    required IClientStateService clientStateService,
    required IHostAvailabilityService hostAvailabilityService,
    required ClientIdResolver clientIdResolver,
    required ClientIdCache clientIdCache,
  }) : _sessionService = sessionService,
       _sessionAdmissionService = sessionAdmissionService,
       _requestService = requestService,
       _clientStateService = clientStateService,
       _hostAvailabilityService = hostAvailabilityService,
       _clientIdResolver = clientIdResolver,
       _clientIdCache = clientIdCache;

  /// The complete result from the last successful Host handshake, cached for an admitted session.
  HelloResult? _lastHelloResult;

  /// Generation invalidating authentication work when the client disconnects.
  int _authenticationGeneration = 0;

  /// Implements [IAuthenticationService.clientId].
  @override
  String? get clientId => _clientIdCache.clientId;

  /// Implements [IAuthenticationService.hello].
  @override
  Future<HelloResult> hello() => _hello(_authenticationGeneration);

  /// Resolves the relationship captured from the session being recovered, if it still exists.
  /// @return The result of the admitted reconnect session.
  @override
  Future<HelloResult> helloLastKnownHost() async {
    final int generation = _authenticationGeneration;
    final PersistedClientState state = await _clientStateService.load();
    _ensureAuthenticationCurrent(generation);
    final DovahLinkHostId? knownHostId = _sessionService.currentKnownHostId;
    final String? normalizedHostId = knownHostId?.value;
    if (normalizedHostId != null &&
        !state.knownHosts.containsKey(normalizedHostId)) {
      throw DovahLinkKnownHostNotFoundException(normalizedHostId);
    }
    return _hello(
      generation,
      knownHostId: normalizedHostId,
      stateSnapshot: state,
    );
  }

  /// Sends hello while [generation] remains the active authentication generation.
  /// @param generation The authentication generation that owns this handshake.
  /// @param knownHostId The selected Known Host ID, or `null` for candidate/unpaired hello.
  /// @param stateSnapshot The coherent state snapshot selected with the endpoint, if already read.
  /// @return The result of the admitted handshake.
  Future<HelloResult> _hello(
    int generation, {
    String? knownHostId,
    PersistedClientState? stateSnapshot,
  }) async {
    bool disconnectAfterFailure = false;
    final String? normalizedKnownHostId = knownHostId == null
        ? null
        : DovahLinkHostId(knownHostId).value;
    try {
      final PersistedClientState state =
          stateSnapshot ?? await _clientStateService.load();
      _ensureAuthenticationCurrent(generation);
      final String clientId = await _clientIdResolver.resolve(state);
      _ensureAuthenticationCurrent(generation);
      final PersistedKnownHost? knownRelationship =
          normalizedKnownHostId == null
          ? null
          : state.knownHosts[normalizedKnownHostId];
      if (normalizedKnownHostId != null && knownRelationship == null) {
        throw DovahLinkKnownHostNotFoundException(normalizedKnownHostId);
      }
      final String? credential =
          state.pendingPairingRecovery?.hostId == normalizedKnownHostId
          ? null
          : knownRelationship?.credential;
      _clientIdCache.set(clientId);

      final HelloPayload payload = HelloPayload(
        clientId: clientId,
        authMethod: credential == null
            ? AuthMethod.unpaired
            : AuthMethod.trustedDeviceCredential,
        authToken: credential,
      );
      disconnectAfterFailure = true;
      final Envelope response = await _requestService.sendAndAwait(
        messageType: ProtocolMessageType.hello,
        payload: payload.toJson(),
        expectedType: ProtocolMessageType.helloAck,
        policy: const RequestPolicy(
          retrySafe: false,
          requiredTrustState: null,
          timeoutClass: TimeoutClass.normal,
        ),
      );
      _ensureAuthenticationCurrent(generation);
      final HelloAckPayload ack = ProtocolPayloadDecoder.decode(
        HelloAckPayload.fromJson,
        response.payload,
      );
      validateHostVersionCompatibility(ack.hostVersion);
      final DovahLinkTrustState trustState = switch (ack.clientIdentityKind) {
        ClientIdentityKind.unpaired => DovahLinkTrustState.unpaired,
        ClientIdentityKind.paired => DovahLinkTrustState.trusted,
      };

      final String? sessionId = response.sessionId;
      if (sessionId == null) {
        // hello_ack requires a sessionId. A null value is malformed and must be rejected before
        // session admission.
        throw const DovahLinkProtocolException(
          code: ProtocolErrorCode.malformedMessage,
          message: 'The host reported hello_ack with no sessionId.',
          retryable: false,
        );
      }
      final Uri? currentEndpoint = _sessionService.currentEndpoint;
      if (currentEndpoint == null) {
        throw const DovahLinkConnectionException(
          'The current connection endpoint is unavailable.',
        );
      }
      final DovahLinkHost currentHost = DovahLinkHost(
        hostId: DovahLinkHostId(ack.hostId).value,
        hostName: ack.hostName,
        endpoint: currentEndpoint,
      );
      final String? pendingPairingHostId = state.pendingPairingRecovery?.hostId;
      if (pendingPairingHostId != null &&
          pendingPairingHostId != currentHost.hostId) {
        throw DovahLinkHostIdentityMismatchException(
          knownHostId: pendingPairingHostId,
          reportedHostId: currentHost.hostId,
        );
      }
      if (normalizedKnownHostId != null &&
          normalizedKnownHostId != currentHost.hostId) {
        throw DovahLinkHostIdentityMismatchException(
          knownHostId: normalizedKnownHostId,
          reportedHostId: currentHost.hostId,
        );
      }
      if (trustState == DovahLinkTrustState.trusted) {
        if (normalizedKnownHostId == null) {
          throw const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message:
                'The Host reported a paired session without Known Host authentication.',
            retryable: false,
          );
        }
        await _clientStateService.updateState((PersistedClientState state) {
          final PersistedKnownHost? relationship =
              state.knownHosts[currentHost.hostId];
          if (relationship == null) {
            throw DovahLinkKnownHostNotFoundException(currentHost.hostId);
          }
          return state.copyWith(
            knownHosts: <String, PersistedKnownHost>{
              ...state.knownHosts,
              currentHost.hostId: PersistedKnownHost(
                host: DovahLinkHost(
                  hostId: relationship.host.hostId,
                  hostName: currentHost.hostName,
                  endpoint: currentHost.endpoint,
                ),
                credential: relationship.credential,
              ),
            },
          );
        });
        _ensureAuthenticationCurrent(generation);
      }
      // admitSession also retransmits any retry-safe operation an earlier ordinary transport
      // loss orphaned, now that this new session's trust state is known -- see
      // ISessionAdmissionService.admitSession's documentation.
      _sessionAdmissionService.admitSession(
        sessionId: sessionId,
        trustState: trustState,
        currentHost: currentHost,
      );
      if (normalizedKnownHostId != null) {
        _sessionService.associateKnownHost(
          DovahLinkHostId(normalizedKnownHostId),
        );
      }
      final HelloResult result = HelloResult(
        hostId: currentHost.hostId,
        hostName: ack.hostName,
        hostVersion: ack.hostVersion,
        trustState: trustState,
      );
      _lastHelloResult = result;

      // The Host always sends an unprompted `capabilities` message right after `hello_ack`; it
      // arrives as an unsolicited (null-correlationId) message and is discarded by
      // MessageRouter -- exposing it is out of this client's current scope. hello() does not
      // wait for it.

      return result;
    } on Object {
      _ensureAuthenticationCurrent(generation);
      if (!disconnectAfterFailure) {
        rethrow;
      }
      // Every HandleHello failure path closes the connection (handshake_handler.cpp's Fail()
      // always sets closeConnection), and a genuine transport failure leaves the socket equally
      // unusable either way -- reset so the next connect() attempt does not find a stale socket
      // WebSocketTransport still considers open (its "Already connected" guard in connect()).
      // disconnect() itself never throws, so this cannot mask the error being rethrown below.
      // Preserves any retry-safe operation an earlier ordinary transport loss already orphaned:
      // this cleanup is one failed attempt within a bounded reconnect cycle that may still retry,
      // not that cycle's own final give-up -- only the cycle's own last disconnect() call (default
      // orphanRetrySafeOperations: false) should finalize/fail what this preserved.
      await _sessionService.disconnect(orphanRetrySafeOperations: true);
      _ensureAuthenticationCurrent(generation);
      rethrow;
    }
  }

  /// Implements [IAuthenticationService.authenticateCandidate].
  @override
  Future<HelloResult> authenticateCandidate(Uri uri) =>
      _authenticate(uri, null);

  /// Implements [IAuthenticationService.authenticateKnownHost].
  @override
  Future<HelloResult> authenticateKnownHost(DovahLinkHostId hostId) async {
    final int generation = _authenticationGeneration;
    final String id = hostId.value;
    final PersistedClientState state = await _clientStateService.load();
    _ensureAuthenticationCurrent(generation);
    final PersistedKnownHost? relationship = state.knownHosts[id];
    if (relationship == null) {
      throw DovahLinkKnownHostNotFoundException(id);
    }
    final PendingPairingRecovery? recovery = state.pendingPairingRecovery;
    if (recovery != null && recovery.hostId != id) {
      throw DovahLinkHostIdentityMismatchException(
        knownHostId: recovery.hostId,
        reportedHostId: id,
      );
    }
    final HelloResult result = await _authenticate(
      relationship.host.endpoint,
      id,
      state,
    );
    _ensureAuthenticationCurrent(generation);
    _hostAvailabilityService.setAvailability(
      hostId,
      DovahLinkHostAvailability.online,
    );
    return result;
  }

  /// Connects and authenticates with only the explicitly selected relationship.
  /// @param uri The SDK-resolved endpoint for the operation.
  /// @param knownHostId The selected relationship owner, or `null` for an untrusted candidate.
  /// @param stateSnapshot The relationship snapshot selected with [uri], if any.
  /// @return The result of the admitted handshake.
  Future<HelloResult> _authenticate(
    Uri uri,
    String? knownHostId, [
    PersistedClientState? stateSnapshot,
  ]) async {
    final int generation = _authenticationGeneration;
    final HelloResult? cachedHelloResult = _lastHelloResult;
    if (knownHostId != null &&
        _sessionService.connectionState == DovahLinkConnectionState.connected &&
        _sessionService.currentTrustState == DovahLinkTrustState.trusted &&
        cachedHelloResult != null &&
        cachedHelloResult.hostId == DovahLinkHostId(knownHostId).value) {
      return HelloResult(
        hostId: cachedHelloResult.hostId,
        hostName: cachedHelloResult.hostName,
        hostVersion: cachedHelloResult.hostVersion,
        trustState: DovahLinkTrustState.trusted,
      );
    }
    // A connection this method (or an earlier hello()) already admitted, but that never reached
    // the cached-and-trusted shortcut above, must be closed before reconnecting -- the transport
    // rejects a second connect() on a socket it has not closed.
    if (_sessionService.connectionState !=
        DovahLinkConnectionState.disconnected) {
      await _sessionService.disconnect(orphanRetrySafeOperations: false);
      _ensureAuthenticationCurrent(generation);
    }
    await _connect(uri, knownHostId, generation);
    _ensureAuthenticationCurrent(generation);
    try {
      final HelloResult result = await _hello(
        generation,
        knownHostId: knownHostId,
        stateSnapshot: stateSnapshot,
      );
      _ensureAuthenticationCurrent(generation);
      return result;
    } on DovahLinkProtocolException catch (error) {
      _ensureAuthenticationCurrent(generation);
      final CredentialRejectionReason? reason =
          CredentialRejectionReason.fromProtocolErrorCode(error.code);
      if (reason == null || knownHostId == null) {
        rethrow;
      }
      await _forgetCredential(knownHostId, generation);
      _ensureAuthenticationCurrent(generation);
      await _connect(uri, knownHostId, generation);
      _ensureAuthenticationCurrent(generation);
      final HelloResult result = await _hello(
        generation,
        knownHostId: knownHostId,
      );
      _ensureAuthenticationCurrent(generation);
      return HelloResult(
        hostId: result.hostId,
        hostName: result.hostName,
        hostVersion: result.hostVersion,
        trustState: result.trustState,
        recoveredFromRejectedCredential: reason,
      );
    }
  }

  /// Connects for an explicit authentication attempt and reports typed transport failure for its
  /// selected Known Host only.
  /// @param uri The endpoint for this authentication attempt.
  /// @param knownHostId The selected Known Host ID, or `null` for an untrusted candidate.
  Future<void> _connect(Uri uri, String? knownHostId, int generation) async {
    try {
      await _sessionService.connect(
        uri,
        knownHostId: knownHostId == null ? null : DovahLinkHostId(knownHostId),
      );
    } on DovahLinkConnectionException {
      _ensureAuthenticationCurrent(generation);
      if (knownHostId != null) {
        _hostAvailabilityService.setAvailability(
          DovahLinkHostId(knownHostId),
          DovahLinkHostAvailability.offline,
        );
      }
      rethrow;
    }
  }

  /// Implements [IAuthenticationService.cancelPendingAuthentication].
  @override
  void cancelPendingAuthentication() {
    _authenticationGeneration++;
  }

  /// Throws when explicit disconnect invalidated [generation] during authentication.
  void _ensureAuthenticationCurrent(int generation) {
    if (generation != _authenticationGeneration) {
      throw const DovahLinkConnectionException(
        'Authentication was cancelled by disconnect.',
      );
    }
  }

  /// Implements [IAuthenticationService.forgetCredential].
  @override
  Future<void> forgetCredential(DovahLinkHostId hostId) =>
      _forgetCredential(hostId.value, null);

  /// Implements [IAuthenticationService.forgetLastKnownCredential].
  @override
  Future<void> forgetLastKnownCredential() async {
    final String? hostId = _lastHelloResult?.hostId;
    if (hostId != null) {
      await _forgetCredential(hostId, null);
    }
  }

  /// Clears persisted trust only while [generation] remains current.
  /// @param hostId The relationship whose credential is being cleared.
  /// @param generation The owning authentication generation, or `null` for explicit cleanup.
  Future<void> _forgetCredential(String hostId, int? generation) async {
    await _clientStateService.updateState((PersistedClientState current) {
      if (generation != null) {
        _ensureAuthenticationCurrent(generation);
      }
      final String normalizedHostId = DovahLinkHostId(hostId).value;
      final PersistedKnownHost? relationship =
          current.knownHosts[normalizedHostId];
      if (relationship == null) {
        return current;
      }
      return current.copyWith(
        knownHosts: <String, PersistedKnownHost>{
          ...current.knownHosts,
          normalizedHostId: PersistedKnownHost(host: relationship.host),
        },
        clearPendingPairingRecovery:
            current.pendingPairingRecovery?.hostId == normalizedHostId,
      );
    });
  }
}
