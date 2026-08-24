import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/persistence/persisted_client_state.dart';
import 'package:dovahlink_client_sdk/src/protocol/envelope.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Central test-owned catalog of representative SDK values.
abstract final class Fixtures {
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

  /// Builds a decoded protocol envelope with representative identity defaults.
  static Envelope buildEnvelope({
    ProtocolMessageType messageType = ProtocolMessageType.pong,
    String messageId = 'reply-1',
    String? sessionId = 'session-1',
    String? correlationId = 'req-1',
    JsonMap payload = const <String, dynamic>{},
    String? bridgeInstanceId = 'bridge-1',
    String? playContextId,
    String? clientId,
  }) => Envelope(
    messageType: messageType,
    messageId: messageId,
    sessionId: sessionId,
    correlationId: correlationId,
    payload: payload,
    bridgeInstanceId: bridgeInstanceId,
    playContextId: playContextId,
    clientId: clientId,
  );

  // ---- Persistence ----

  /// Builds a persisted client state with a representative resolved client ID.
  static PersistedClientState buildPersistedClientState({
    String? clientId = 'client-1',
    String? credential,
    PairingRecoveryState recoveryState = PairingRecoveryState.none,
  }) => PersistedClientState(
    clientId: clientId,
    credential: credential,
    recoveryState: recoveryState,
  );
}
