import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
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
}
