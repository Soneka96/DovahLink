import 'package:dovahlink_client/features/appearance/presentation/state/appearance.reducer.dart';
import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.reducer.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.reducer.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.reducer.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/state/app_state.dart';

/// Applies a Redux action to [state] and returns the next root state.
///
/// Returns [state] itself when no feature reducer changed its slice.
/// @param state The current root state.
/// @param action The Redux action being reduced.
/// @return The current or updated root state.
AppState appReducer(AppState state, Object? action) {
  final ConnectionState connection = connectionReducer(
    state.connection,
    action,
  );
  final PairingState pairing = pairingReducer(state.pairing, action);
  final AppearanceState appearance = appearanceReducer(
    state.appearance,
    action,
  );
  final DeviceIdentityState deviceIdentity = deviceIdentityReducer(
    state.deviceIdentity,
    action,
  );
  if (identical(connection, state.connection) &&
      identical(pairing, state.pairing) &&
      identical(appearance, state.appearance) &&
      identical(deviceIdentity, state.deviceIdentity)) {
    return state;
  }
  return AppState(
    connection: connection,
    pairing: pairing,
    appearance: appearance,
    deviceIdentity: deviceIdentity,
  );
}
