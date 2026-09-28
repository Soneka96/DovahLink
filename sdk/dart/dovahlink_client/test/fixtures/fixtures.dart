import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/persistence/pending_pairing_recovery.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_known_host.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/protocol/pairing_status_payload.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import 'package:dovahlink_client_sdk/src/state/state_synchronization.dart';

/// Central test-owned catalog of representative SDK values.
abstract final class Fixtures {
  /// Builds a representative Host identity and endpoint.
  /// @param hostId The stable Host UUID.
  /// @param hostName The display name reported by the Host.
  /// @param endpoint The WebSocket endpoint URI.
  /// @return A representative Host metadata value.
  static DovahLinkHost buildDovahLinkHost({
    String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea',
    String hostName = 'GONCALO-DESKTOP',
    String endpoint = 'ws://127.0.0.1:58231/',
  }) => DovahLinkHost(
    hostId: hostId,
    hostName: hostName,
    endpoint: Uri.parse(endpoint),
  );

  // ---- Request ----

  /// Builds a request policy with retry-safe-unpaired-by-default fields.
  static RequestPolicy buildRequestPolicy({
    bool retrySafe = true,
    DovahLinkTrustState? requiredTrustState = DovahLinkTrustState.unpaired,
    TimeoutClass timeoutClass = TimeoutClass.short,
  }) => RequestPolicy(
    retrySafe: retrySafe,
    requiredTrustState: requiredTrustState,
    timeoutClass: timeoutClass,
  );

  // ---- Internal requests ----

  /// Builds a pending operation with retry-safe-by-default fields.
  static PendingOperation buildPendingOperation({
    ProtocolMessageType messageType = ProtocolMessageType.pairingRequest,
    JsonMap payload = const <String, dynamic>{},
    RequestPolicy? policy,
  }) => PendingOperation(
    messageType: messageType,
    payload: payload,
    policy: policy ?? buildRequestPolicy(),
  );

  // ---- Protocol ----

  /// Builds a decoded protocol envelope with representative identity defaults. The default
  /// [messageType] (`pong`) requires [stateAuthorityId] to be absent, so its default is `null`; a
  /// test building a `hello_ack`/`state_snapshot`/`state_event` envelope overrides both together.
  static Envelope buildEnvelope({
    ProtocolMessageType messageType = ProtocolMessageType.pong,
    String messageId = 'reply-1',
    String? sessionId = 'session-1',
    String? correlationId = 'req-1',
    JsonMap payload = const <String, dynamic>{},
    String? stateAuthorityId,
    String? playContextId,
    String? clientId,
  }) => Envelope(
    messageType: messageType,
    messageId: messageId,
    sessionId: sessionId,
    correlationId: correlationId,
    payload: payload,
    stateAuthorityId: stateAuthorityId,
    playContextId: playContextId,
    clientId: clientId,
  );

  // ---- Authentication ----

  /// Builds a successful Host handshake result with stable identity defaults.
  static HelloResult buildHelloResult({
    String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea',
    String hostName = 'Soneka-Desktop',
    String hostVersion = '0.5.0',
    DovahLinkTrustState trustState = DovahLinkTrustState.trusted,
    CredentialRejectionReason? recoveredFromRejectedCredential,
  }) => HelloResult(
    hostId: hostId,
    hostName: hostName,
    hostVersion: hostVersion,
    trustState: trustState,
    recoveredFromRejectedCredential: recoveredFromRejectedCredential,
  );

  // ---- Pairing ----

  /// Builds a pairing-status payload with representative expiry defaults.
  static PairingStatusPayload buildPairingStatusPayload({
    PairingAvailability state = PairingAvailability.available,
    int? expiresInSeconds = 300,
  }) => PairingStatusPayload(state: state, expiresInSeconds: expiresInSeconds);

  // ---- Persistence ----

  /// Builds persisted client state with one optional Host relationship.
  /// @param clientId The stable client identity, or `null` before it is generated.
  /// @param hostId The Host UUID used when no explicit [host] is supplied.
  /// @param hostName The representative Host display name.
  /// @param endpoint The representative WebSocket endpoint.
  /// @param host An explicit Host value for a scenario-specific relationship.
  /// @param credential The current credential owned by this Host, if any.
  /// @param recoveryState The pending pairing recovery phase to represent.
  /// @return A fresh persisted state with credentials associated with their Host.
  static PersistedClientState buildPersistedClientState({
    String? clientId = 'client-1',
    String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea',
    String hostName = 'GONCALO-DESKTOP',
    String endpoint = 'ws://127.0.0.1:58231/',
    DovahLinkHost? host,
    String? credential,
    PairingRecoveryState recoveryState = PairingRecoveryState.none,
  }) {
    final Map<String, PersistedKnownHost> knownHosts =
        credential == null &&
            recoveryState == PairingRecoveryState.none &&
            host == null
        ? <String, PersistedKnownHost>{}
        : <String, PersistedKnownHost>{
            (host?.hostId ?? hostId): PersistedKnownHost(
              host:
                  host ??
                  DovahLinkHost(
                    hostId: hostId,
                    hostName: hostName,
                    endpoint: Uri.parse(endpoint),
                  ),
              credential: credential,
            ),
          };
    return PersistedClientState(
      clientId: clientId,
      knownHosts: knownHosts,
      pendingPairingRecovery: recoveryState == PairingRecoveryState.confirming
          ? PendingPairingRecovery(
              hostId: host?.hostId ?? hostId,
              state: PairingRecoveryState.confirming,
            )
          : null,
    );
  }

  // ---- State synchronization ----

  /// Builds a synchronization view with no current baseline by default.
  /// @param status The synchronization standing to represent.
  /// @param value The latest typed state, or `null` before a baseline exists.
  /// @param stateAuthorityId The authority identity associated with the baseline.
  /// @param playContextId The play-context identity associated with the baseline.
  /// @param revision The last accepted revision, or `null` before a baseline exists.
  /// @return A fresh synchronization view.
  static StateSynchronization<T> buildStateSynchronization<T>({
    DovahLinkStateStatus status = DovahLinkStateStatus.notSubscribed,
    T? value,
    String? stateAuthorityId,
    String? playContextId,
    int? revision,
  }) => StateSynchronization<T>(
    status: status,
    value: value,
    stateAuthorityId: stateAuthorityId,
    playContextId: playContextId,
    revision: revision,
  );
}
