import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/internal/client_session.dart';
import 'package:dovahlink_client_sdk/src/internal/session_context.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Connects, disconnects, and admits a newly authenticated session, for a collaborator that
/// orchestrates authentication but does not itself own transport lifecycle or connection state --
/// see `ai/context/sdk/architecture.md`'s "Internal composition". Implemented by [ClientSession].
/// Extends [SessionContext] because every consumer of this port also needs read access to the
/// current session identity/trust state (a collaborator that only needs the read side takes
/// [SessionContext] alone instead).
abstract interface class SessionConnector implements SessionContext {
  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState;

  /// Establishes the transport connection to [uri].
  /// @throws [DovahLinkConnectionException] if the socket cannot be established.
  Future<void> connect(Uri uri);

  /// Closes the connection and resets in-memory session state. Idempotent.
  Future<void> disconnect();

  /// Admits a newly authenticated session, recording [sessionId] and [trustState] and triggering
  /// retransmission of any retry-safe operation an earlier ordinary transport loss orphaned, per
  /// `ai/context/sdk/architecture.md`'s "Session-state ownership".
  void admitSession({
    required String sessionId,
    required DovahLinkTrustState trustState,
  });
}
