import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';

/// Upgrades trust standing, a privileged capability injected only into `PairingService`, per
/// `ai/context/sdk/architecture.md`'s "Composing narrow authority". Nothing else may ever upgrade
/// trust standing.
abstract interface class ISessionTrustService {
  /// Upgrades the current session's trust standing to `DovahLinkTrustState.trusted`.
  void markTrusted();
}

/// Implements [ISessionTrustService], per `ai/context/sdk/architecture.md`'s "Composing narrow
/// authority". [state] is supplied by the caller per `ai/context/sdk/architecture.md`'s
/// "Dependency injection" -- this class never constructs its own dependency.
class SessionTrustService implements ISessionTrustService {
  /// The single authoritative owner of this session's mutable facts.
  final SessionState _state;

  /// Creates a session trust service over [state].
  SessionTrustService({required SessionState state}) : _state = state;

  /// Implements [ISessionTrustService.markTrusted].
  @override
  void markTrusted() => _state.markTrusted();
}
