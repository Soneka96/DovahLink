import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';

/// The root immutable state held by the DovahLink Redux store.
class AppState {
  /// Creates the root state from feature states. [appearance] defaults to the app's default
  /// theme preset so existing call sites that only care about [connection]/[pairing] (most
  /// pre-appearance tests) are unaffected; production startup always supplies the persisted
  /// preset explicitly instead, through [AppState.initial] or directly.
  const AppState({
    required this.connection,
    required this.pairing,
    this.appearance = const AppearanceState(activePreset: defaultThemePreset),
  });

  /// Current connection state.
  final ConnectionState connection;

  /// Current pairing state.
  final PairingState pairing;

  /// Current appearance state.
  final AppearanceState appearance;

  /// Returns the initial state for a new client session. [appearance] defaults to
  /// [AppearanceState.initial] when omitted; the application composition root normally supplies
  /// the persisted preset resolved during its async bootstrap instead.
  factory AppState.initial({AppearanceState? appearance}) => AppState(
    connection: ConnectionState.initial(),
    pairing: PairingState.initial(),
    appearance: appearance ?? AppearanceState.initial(),
  );
}
