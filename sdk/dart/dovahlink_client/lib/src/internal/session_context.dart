import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Read-only access to the current session's identity and trust standing, for a collaborator that
/// needs to know the live session without commanding it -- see
/// `ai/context/sdk/architecture.md`'s "Internal composition". Implemented by [ClientSession], the
/// sole owner of this state.
abstract interface class SessionContext {
  /// The server-issued session identifier of the current session, or `null` before one is
  /// admitted.
  String? get currentSessionId;

  /// The current trust standing, or `null` before one is admitted.
  DovahLinkTrustState? get currentTrustState;
}
