import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Admits a newly authenticated session, a privileged capability injected only into
/// `AuthenticationServiceImpl`, per `ai/context/sdk/architecture.md`'s "Composing narrow
/// authority". Nothing else may ever commit a newly authenticated session.
abstract interface class SessionAdmissionService {
  /// Admits a newly authenticated session, recording [sessionId] and [trustState] and triggering
  /// retransmission of any retry-safe operation an earlier ordinary transport loss orphaned, per
  /// `ai/context/sdk/architecture.md`'s "Session-state ownership".
  void admitSession({
    required String sessionId,
    required DovahLinkTrustState trustState,
  });
}
