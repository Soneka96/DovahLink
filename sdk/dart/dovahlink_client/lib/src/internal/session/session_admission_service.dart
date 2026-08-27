import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Admits a newly authenticated session, a privileged capability injected only into
/// `AuthenticationServiceImpl`, per `ai/context/sdk/architecture.md`'s "Composing narrow
/// authority". Nothing else may ever commit a newly authenticated session.
abstract interface class ISessionAdmissionService {
  /// Admits a newly authenticated session, recording [sessionId] and [trustState] and triggering
  /// retransmission of any retry-safe operation an earlier ordinary transport loss orphaned, per
  /// `ai/context/sdk/architecture.md`'s "Session-state ownership".
  void admitSession({
    required String sessionId,
    required DovahLinkTrustState trustState,
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
  final RequestService _requestService;

  /// Creates a session admission service over [state], retrying orphaned operations through
  /// [requestService].
  SessionAdmissionService({
    required SessionState state,
    required RequestService requestService,
  }) : _state = state,
       _requestService = requestService;

  /// Implements [ISessionAdmissionService.admitSession].
  @override
  void admitSession({
    required String sessionId,
    required DovahLinkTrustState trustState,
  }) {
    _state.admit(sessionId: sessionId, trustState: trustState);
    _requestService.retryOrphanedOperations();
  }
}
