import 'package:dovahlink_client_sdk/src/dovahlink_host.dart';
import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/internal/state/subscription_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Admits a newly authenticated session, a privileged capability injected only into
/// `AuthenticationService`, per `ai/context/sdk/architecture.md`'s "Composing narrow
/// authority". Nothing else may ever commit a newly authenticated session.
abstract interface class ISessionAdmissionService {
  /// Admits a newly authenticated session, recording [sessionId] and [trustState] and triggering
  /// retransmission of any retry-safe operation an earlier ordinary transport loss orphaned, per
  /// `ai/context/sdk/architecture.md`'s "Session-state ownership". A trusted admission also starts
  /// best-effort restoration of the remembered state-area subscriptions.
  /// @param sessionId The server-issued identity for this connection's session.
  /// @param trustState The trust tier admitted by the Host.
  /// @param currentHost The Host identity and endpoint reported for this session.
  void admitSession({
    required String sessionId,
    required DovahLinkTrustState trustState,
    required DovahLinkHost currentHost,
  });
}

/// Implements [ISessionAdmissionService], per `ai/context/sdk/architecture.md`'s "Composing narrow
/// authority" and "Session-state ownership". Both [state] and [requestService] are supplied by the
/// caller per `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never
/// constructs one of its own dependencies.
class SessionAdmissionService implements ISessionAdmissionService {
  /// The single authoritative owner of this session's mutable facts.
  final SessionState _state;

  /// Retransmits any retry-safe operation an earlier ordinary transport loss orphaned, once the
  /// newly admitted session's trust state is known.
  final IRequestService _requestService;

  /// Restores desired state-area subscriptions after trusted admission.
  final ISubscriptionService _subscriptionService;

  /// Creates a session admission service over [state], retrying orphaned operations through
  /// [requestService], then restoring trusted subscriptions through [subscriptionService].
  /// @param state The single owner of current session state.
  /// @param requestService Retries eligible operations after admission.
  /// @param subscriptionService Restores desired state areas for trusted sessions.
  SessionAdmissionService({
    required SessionState state,
    required IRequestService requestService,
    required ISubscriptionService subscriptionService,
  }) : _state = state,
       _requestService = requestService,
       _subscriptionService = subscriptionService;

  /// Implements [ISessionAdmissionService.admitSession].
  @override
  void admitSession({
    required String sessionId,
    required DovahLinkTrustState trustState,
    required DovahLinkHost currentHost,
  }) {
    _state.admit(
      sessionId: sessionId,
      trustState: trustState,
      currentHost: currentHost,
    );
    _requestService.retryOrphanedOperations();
    if (trustState == DovahLinkTrustState.trusted) {
      _subscriptionService.restoreDesiredStateAreas();
    }
  }
}
