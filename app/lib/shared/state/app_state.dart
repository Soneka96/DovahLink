import 'package:dovahlink_client/features/appearance/presentation/state/appearance.state.dart';
import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';
import 'package:dovahlink_client/shared/constants/constants.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';

/// The root immutable state held by the DovahLink Redux store.
class AppState {
  /// Creates the root state from feature states. When [appearance] is omitted, its active preset
  /// defaults to [defaultThemePreset]. Production startup supplies the persisted preset.
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
  factory AppState.initial({
    AppearanceState? appearance,
    PairingSupport pairingSupport = PairingSupport.available,
  }) => AppState(
    connection: ConnectionState.initial(),
    pairing: PairingState.initial(support: pairingSupport),
    appearance: appearance ?? AppearanceState.initial(),
  );
}
