import 'package:dovahlink_client/features/connection/domain/entities/bridge.entity.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Static selectors over [AppState] for connection presentation state.
abstract final class ConnectionSelectors {
  /// Returns the Bridges available to select.
  static List<BridgeEntity> bridgesSelector(AppState state) =>
      state.connection.bridges;
}
