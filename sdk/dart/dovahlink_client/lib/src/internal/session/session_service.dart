import 'package:dovahlink_client_sdk/src/dovahlink_connection_exception.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Owns transport lifecycle, connection state, and stream ownership, per
/// `ai/context/sdk/architecture.md`'s "Internal composition". The single architectural interface
/// implemented by `SessionServiceImpl`, the sole authoritative owner of every session-scoped
/// mutable fact this engine has (backed internally by `SessionState`). Every read and command a
/// legitimate consumer of the connection's lifecycle needs -- `connect`/`disconnect`, its current
/// phase and identity, and reacting to a connection-health signal a collaborator elsewhere detects
/// -- lives on this one contract; see `ai/context/sdk/architecture.md`'s "Request/session boundary"
/// for why the four reactive report methods belong here rather than on a separate interface or
/// callback.
abstract interface class SessionService {
  /// The current connection lifecycle phase.
  DovahLinkConnectionState get connectionState;

  /// The server-issued session identifier of the current session, or `null` before one is
  /// admitted.
  String? get currentSessionId;

  /// The current trust standing, or `null` before one is admitted.
  DovahLinkTrustState? get currentTrustState;

  /// The reason [connectionState] is [DovahLinkConnectionState.administrativelyInvalidated], or
  /// `null` otherwise.
  AdministrativeInvalidationReason? get invalidationReason;

  /// Establishes the transport connection to [uri].
  /// @throws [DovahLinkConnectionException] if the socket cannot be established.
  Future<void> connect(Uri uri);

  /// Closes the connection and resets in-memory session state. Idempotent. A `retrySafe` pending
  /// or already-orphaned operation is failed rather than preserved unless [orphanRetrySafeOperations]
  /// is `true` -- callers finalizing bounded recovery (successfully or not) want the default; a
  /// caller that expects more recovery attempts to follow passes `true` to preserve them, with
  /// [reason] describing why this specific call closed the connection, when it differs from a
  /// caller's own default.
  Future<void> disconnect({
    bool orphanRetrySafeOperations = false,
    Exception? reason,
  });

  /// Reports that the connection is no longer healthy (a send failure, a timeout, or a transport
  /// error/close) and must be torn down.
  void onUnhealthy(Exception reason);

  /// Reports a protocol-level anomaly on an otherwise-live connection (malformed JSON, an
  /// unmatched correlation ID, or an unrecognized DTO-boundary value) that must be torn down
  /// without treating it as safe to retry. [orphanRetrySafeOperations] controls whether a
  /// `retrySafe` pending operation is parked for a later retry instead of failed immediately, per
  /// `ai/context/sdk/api-design.md`'s "Request retry safety, session requirement, and timeout
  /// class".
  void onProtocolViolation(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  });

  /// Reports an authoritative `session_invalidated` push, decoded and validated by the caller.
  void onSessionInvalidated(AdministrativeInvalidationReason reason);

  /// Reports a decoded unsolicited `error` push (`correlationId: null`), sent for a violation the
  /// bridge detects before it can correlate a reply -- for example before decoding completes, or
  /// before a session exists. Not ordinary connectivity loss: the connection is torn down without
  /// automatic reconnect, carrying [error]'s own code/message/retryable classification rather than
  /// a generic malformed-message reason.
  void onUnsolicitedError(ErrorPayload error);
}
