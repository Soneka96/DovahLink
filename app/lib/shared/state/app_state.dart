import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/device_identity/presentation/state/device_identity.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// The root immutable state held by the DovahLink Redux store.
class AppState {
  /// Creates the root state from feature states. Omitted appearance and device-identity values use
  /// their local defaults; production startup supplies the persisted values.
  /// @param connection Current connection feature state.
  /// @param pairing Current pairing feature state.
  /// @param appearance Current theme selection.
  /// @param deviceIdentity Resolved device name and any name-load failure.
  const AppState({
    required this.connection,
    required this.pairing,
    this.appearance = const AppearanceState(activePreset: defaultThemePreset),
    this.deviceIdentity = const DeviceIdentityState(
      displayName: defaultDeviceName,
    ),
  });

  /// Current connection state.
  final ConnectionState connection;

  /// Current pairing state.
  final PairingState pairing;

  /// Current appearance state.
  final AppearanceState appearance;

  /// Current resolved companion name and any identity-load failure.
  final DeviceIdentityState deviceIdentity;

  /// Returns the initial state for a new client session. Appearance and device identity default to
  /// their initial values when omitted; the application composition root supplies persisted values
  /// resolved during its asynchronous bootstrap.
  /// @param appearance The loaded theme state, if available.
  /// @param deviceIdentity The resolved device identity, or its unavailable state.
  /// @param pairingSupport Whether secure pairing storage is available.
  /// @return A fresh root state for the app's initial route.
  factory AppState.initial({
    AppearanceState? appearance,
    DeviceIdentityState? deviceIdentity,
    PairingSupport pairingSupport = PairingSupport.available,
  }) => AppState(
    connection: ConnectionState.initial(),
    pairing: PairingState.initial(support: pairingSupport),
    appearance: appearance ?? AppearanceState.initial(),
    deviceIdentity: deviceIdentity ?? DeviceIdentityState.initial(),
  );
}
