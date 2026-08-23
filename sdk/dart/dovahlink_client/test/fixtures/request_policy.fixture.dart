import 'package:dovahlink_client_sdk/src/request_policy.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Builds a request policy with representative, retry-safe-unpaired-by-default fields.
RequestPolicy buildRequestPolicy({
  bool retrySafe = true,
  DovahLinkTrustState? requiredTrustState = DovahLinkTrustState.unpaired,
  TimeoutClass timeoutClass = TimeoutClass.short,
}) => RequestPolicy(
  retrySafe: retrySafe,
  requiredTrustState: requiredTrustState,
  timeoutClass: timeoutClass,
);
