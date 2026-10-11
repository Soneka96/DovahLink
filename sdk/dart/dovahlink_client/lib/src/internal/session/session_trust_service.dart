import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';

/// Upgrades trust standing, a privileged capability injected only into `PairingService`, per
/// `ai/context/sdk/architecture.md`'s "Composing narrow authority". Nothing else may ever upgrade
/// trust standing.
abstract interface class ISessionTrustService {
  /// Upgrades the current session's trust standing to `DovahLinkTrustState.trusted`, then starts
  /// best-effort restoration of the remembered state-area subscriptions when a session is still
  /// admitted. This is one of the two paths on which a session becomes trusted, alongside trusted
  /// admission in `SessionAdmissionService`.
  void markTrusted();
}

/// Implements [ISessionTrustService], per `ai/context/sdk/architecture.md`'s "Composing narrow
/// authority". [state] and [subscriptionService] are supplied by the caller per
/// `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never constructs its
/// own dependencies.
class SessionTrustService implements ISessionTrustService {
  /// The single authoritative owner of this session's mutable facts.
  final SessionState _state;

  /// Restores desired state-area subscriptions once the session becomes trusted.
  final ISubscriptionService _subscriptionService;

  /// Creates a session trust service over [state], restoring subscriptions through
  /// [subscriptionService].
  /// @param state The single owner of current session state.
  /// @param subscriptionService Restores desired state areas for the newly trusted session.
  SessionTrustService({
    required SessionState state,
    required ISubscriptionService subscriptionService,
  }) : _state = state,
       _subscriptionService = subscriptionService;

  /// Implements [ISessionTrustService.markTrusted].
  @override
  void markTrusted() {
    _state.markTrusted();
    if (_state.sessionId != null) {
      _subscriptionService.restoreDesiredStateAreas();
    }
  }
}
