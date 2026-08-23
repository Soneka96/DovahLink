import 'package:dovahlink_client_sdk/src/internal/session_state.dart';
import 'package:dovahlink_client_sdk/src/internal/session_trust_service.dart';

/// Implements [SessionTrustService], per `ai/context/sdk/architecture.md`'s "Composing narrow
/// authority". [state] is supplied by the caller per `ai/context/sdk/architecture.md`'s
/// "Dependency injection" -- this class never constructs its own dependency.
class SessionTrustServiceImpl implements SessionTrustService {
  /// The single authoritative owner of this session's mutable facts.
  final SessionState _state;

  /// Creates a session trust service over [state].
  SessionTrustServiceImpl({required SessionState state}) : _state = state;

  /// Implements [SessionTrustService.markTrusted].
  @override
  void markTrusted() => _state.markTrusted();
}
