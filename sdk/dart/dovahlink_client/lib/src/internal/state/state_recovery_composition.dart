import 'package:dovahlink_client_sdk/src/internal/requests/request_service.dart';
import 'package:dovahlink_client_sdk/src/internal/session/session_service.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_domain_definition.dart';
import 'package:dovahlink_client_sdk/src/internal/state/state_recovery_service.dart';

/// Starts exactly one [StateRecoveryService] for each Event-capable registered definition.
///
/// Recovery follows the definition's [IStateDomainDefinition.supportsEvents] capability, never a
/// domain's identity, so a new Event-capable domain cannot miss recovery. Snapshot-only definitions
/// receive no service because they cannot fall behind through an Event revision gap.
/// @param domains The same registered definitions the shared state-message handler dispatches to.
/// @param requestService The shared request boundary carrying correlated `snapshot_request`s.
/// @param sessionService The shared connection lifecycle used to report a failed recovery.
/// @return The started services, one per Event-capable definition, in registration order.
List<IStateRecoveryService<Object?>> startStateRecovery({
  required List<IStateDomainDefinition<Object?>> domains,
  required IRequestService requestService,
  required ISessionService sessionService,
}) {
  final List<IStateRecoveryService<Object?>> services =
      <IStateRecoveryService<Object?>>[
        for (final IStateDomainDefinition<Object?> domain in domains)
          if (domain.supportsEvents)
            StateRecoveryService<Object?>(
              domain: domain,
              requestService: requestService,
              sessionService: sessionService,
            ),
      ];
  for (final IStateRecoveryService<Object?> service in services) {
    service.start();
  }
  return List<IStateRecoveryService<Object?>>.unmodifiable(services);
}
