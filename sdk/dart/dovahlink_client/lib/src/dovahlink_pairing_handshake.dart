import 'package:dovahlink_client_sdk/src/hello_result.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// The original `hello` result and the trust state after pending pairing recovery.
class DovahLinkPairingHandshake {
  /// Creates the result of authenticating for the pairing flow.
  const DovahLinkPairingHandshake({
    required this.hello,
    required this.trustState,
  });

  /// The Host identity and compatibility result returned by `hello`.
  final HelloResult hello;

  /// The effective trust state after any interrupted confirmation was recovered.
  final DovahLinkTrustState trustState;
}
