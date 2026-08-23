import 'package:dovahlink_client_sdk/src/internal/connection_lifecycle_reporter.dart';
import 'package:dovahlink_client_sdk/src/internal/session_service.dart';
import 'package:dovahlink_client_sdk/src/protocol/error_payload.dart';
import 'package:dovahlink_client_sdk/src/shared/enums.dart';

/// Implements [ConnectionLifecycleReporter] by delegating to [SessionService] -- so
/// `RequestServiceImpl`'s still-unchanged owned collaborators (`PendingOperationTransmitter`,
/// `MessageRouter`, and transitively `UnsolicitedMessageHandler`) can be handed a real, distinct
/// object under its own true identity, rather than `RequestServiceImpl` implementing this port
/// itself and passing itself under a second name. Eliminated, along with
/// `ConnectionLifecycleReporter`, at the composition-root cutover.
class ConnectionLifecycleReporterImpl implements ConnectionLifecycleReporter {
  /// The session this reporter forwards every connection-lifecycle event to.
  final SessionService _sessionService;

  /// Creates a lifecycle-reporter view over [sessionService].
  ConnectionLifecycleReporterImpl(this._sessionService);

  /// Implements [ConnectionLifecycleReporter.onUnhealthy].
  @override
  void onUnhealthy(Exception reason) => _sessionService.onUnhealthy(reason);

  /// Implements [ConnectionLifecycleReporter.onProtocolViolation].
  @override
  void onProtocolViolation(
    Exception reason, {
    required bool orphanRetrySafeOperations,
  }) => _sessionService.onProtocolViolation(
    reason,
    orphanRetrySafeOperations: orphanRetrySafeOperations,
  );

  /// Implements [ConnectionLifecycleReporter.onSessionInvalidated].
  @override
  void onSessionInvalidated(AdministrativeInvalidationReason reason) =>
      _sessionService.onSessionInvalidated(reason);

  /// Implements [ConnectionLifecycleReporter.onUnsolicitedError].
  @override
  void onUnsolicitedError(ErrorPayload error) =>
      _sessionService.onUnsolicitedError(error);
}
