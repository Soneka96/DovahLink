import 'package:dovahlink_client/features/connection/domain/entities/host.entity.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for connection presentation state.
abstract final class ConnectionSelectors {
  /// Returns the Hosts available to select.
  static List<HostEntity> hostsSelector(AppState state) =>
      state.connection.hosts;
}
