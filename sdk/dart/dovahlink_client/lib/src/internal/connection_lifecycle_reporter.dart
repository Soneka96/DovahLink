import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Reports a connection-level event upward to whichever class owns the connection, for a
/// collaborator that detects the event but does not itself own transport lifecycle or connection
/// state -- see `ai/context/sdk/architecture.md`'s "Internal composition". Implemented by
/// [ClientSession].
abstract interface class ConnectionLifecycleReporter {
  /// Reports that the connection is no longer healthy (a send failure, a timeout, or a transport
  /// error/close) and must be torn down.
  void onUnhealthy(Exception reason);

  /// Reports a decoded unsolicited `error` push (`correlationId: null`), sent for a violation the
  /// bridge detects before it can correlate a reply -- for example before decoding completes, or
  /// before a session exists. Not ordinary connectivity loss: the connection is torn down without
  /// automatic reconnect, carrying [error]'s own code/message/retryable classification rather than
  /// a generic malformed-message reason.
  void onUnsolicitedError(ErrorPayload error);

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
}
