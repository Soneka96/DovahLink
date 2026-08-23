import 'package:dovahlink_client_sdk/src/internal/requests/pending_operation.dart';
import 'package:dovahlink_client_sdk/src/protocol/json_map.dart';
import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';
import '../../request_policy.fixture.dart';

/// Builds one pending operation with representative, retry-safe-by-default fields. Defaults
/// [policy] to [buildRequestPolicy]'s own representative shape rather than duplicating it here.
PendingOperation buildPendingOperation({
  ProtocolMessageType messageType = ProtocolMessageType.pairingRequest,
  JsonMap payload = const <String, dynamic>{},
  RequestPolicy? policy,
}) => PendingOperation(
  messageType: messageType,
  payload: payload,
  policy: policy ?? buildRequestPolicy(),
);
