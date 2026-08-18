import 'package:dovahlink_client/features/connection/presentation/state/connection.state.dart';
import 'package:dovahlink_client/features/pairing/presentation/state/pairing.state.dart';

/// The root immutable state held by the DovahLink Redux store.
class AppState {
  /// Creates the root state from feature states.
  const AppState({required this.connection, required this.pairing});

  /// Current connection state.
  final ConnectionState connection;

  /// Current pairing state.
  final PairingState pairing;

  /// Returns the initial state for a new client session.
  factory AppState.initial() => AppState(
    connection: ConnectionState.initial(),
    pairing: PairingState.initial(),
  );
}
