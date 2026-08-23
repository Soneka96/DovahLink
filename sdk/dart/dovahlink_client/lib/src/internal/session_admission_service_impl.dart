import 'package:dovahlink_client_sdk/src/internal/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session_admission_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session_state.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Implements [SessionAdmissionService], per `ai/context/sdk/architecture.md`'s "Composing narrow
/// authority" and "Session-state ownership". Both [state] and [requestService] are supplied by the
/// caller per `ai/context/sdk/architecture.md`'s "Dependency injection" -- this class never
/// constructs one of its own dependencies.
class SessionAdmissionServiceImpl implements SessionAdmissionService {
  /// The single authoritative owner of this session's mutable facts.
  final SessionState _state;

  /// Retransmits any retry-safe operation an earlier ordinary transport loss orphaned, once the
  /// newly admitted session's trust state is known.
  final RequestService _requestService;

  /// Creates a session admission service over [state], retrying orphaned operations through
  /// [requestService].
  SessionAdmissionServiceImpl({
    required SessionState state,
    required RequestService requestService,
  }) : _state = state,
       _requestService = requestService;

  /// Implements [SessionAdmissionService.admitSession].
  @override
  void admitSession({
    required String sessionId,
    required DovahLinkTrustState trustState,
  }) {
    _state.admit(sessionId: sessionId, trustState: trustState);
    _requestService.retryOrphanedOperations();
  }
}
