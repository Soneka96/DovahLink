import 'package:dovahlink_client_sdk/src/dovahlink_protocol_exception.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Classifies a protocol rejection from re-authentication during bounded automatic reconnect
/// (`ai/context/sdk/architecture.md`'s reconnect policy) as terminal -- stop the recovery cycle
/// immediately without consuming a further attempt -- or retryable -- consume the attempt and
/// continue, up to the existing attempt/deadline bounds.
class ReconnectRejectionClassifier {
  /// Administrative rejection codes that are always terminal regardless of
  /// [DovahLinkProtocolException.retryable] -- retrying the same revoked/blocked/unauthorized
  /// credential, or the same malformed request, cannot succeed.
  static const Set<ProtocolErrorCode> _alwaysTerminalCodes =
      <ProtocolErrorCode>{
        ProtocolErrorCode.revoked,
        ProtocolErrorCode.blocked,
        ProtocolErrorCode.unauthorized,
        ProtocolErrorCode.malformedMessage,
      };

  /// Returns whether [error] should stop bounded automatic reconnect immediately. True for any of
  /// [_alwaysTerminalCodes], or for any other code the bridge itself reported as not
  /// [DovahLinkProtocolException.retryable]; false only for a code the bridge reported as
  /// retryable and that is not one of [_alwaysTerminalCodes] -- for example a transient
  /// `rate_limited` or `internal_error` during an intermediate hello.
  static bool isTerminal(DovahLinkProtocolException error) =>
      !error.retryable || _alwaysTerminalCodes.contains(error.code);
}
