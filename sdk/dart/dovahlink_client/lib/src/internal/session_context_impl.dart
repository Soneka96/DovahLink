import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Implements [SessionContext] by delegating to [SessionService] -- so `RequestServiceImpl`'s
/// still-unchanged owned collaborator `PendingOperationTransmitter` (built against the
/// pre-decomposition `SessionContext` port) can be handed a real, distinct object under its own
/// true identity, rather than `RequestServiceImpl` implementing this port itself and passing
/// itself under a second name. Eliminated, along with `SessionContext`, at the composition-root
/// cutover.
class SessionContextImpl implements SessionContext {
  /// The session this view reads identity and trust state from.
  final SessionService _sessionService;

  /// Creates a session-context view over [sessionService].
  SessionContextImpl(this._sessionService);

  /// Implements [SessionContext.currentSessionId].
  @override
  String? get currentSessionId => _sessionService.currentSessionId;

  /// Implements [SessionContext.currentTrustState].
  @override
  DovahLinkTrustState? get currentTrustState =>
      _sessionService.currentTrustState;
}
